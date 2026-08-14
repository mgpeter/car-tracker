import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../../api/queries'
import { ToastProvider } from '../../shell/Toast'
import { axe } from '../../test/axe'

// The panel's success path ends in a sign-out, so the session has to be controllable from the test. The global
// mock in test/setup builds a fresh vi.fn() on every call, which nothing outside the component can assert on.
const h = vi.hoisted(() => ({ logout: vi.fn() }))

vi.mock('@auth0/auth0-react', () => ({
  useAuth0: () => ({
    isAuthenticated: true,
    isLoading: false,
    user: { email: 'you@example.test' },
    logout: h.logout,
    loginWithRedirect: vi.fn(),
    getAccessTokenSilently: vi.fn(async () => 'test-access-token'),
  }),
}))

import { __resetScrollLock } from '../../lib/useScrollLock'
import { DangerZonePanel } from './DangerZonePanel'

const ACCOUNT = {
  email: 'you@example.test',
  createdAt: '2026-07-24T00:25:36Z',
  vehicleCount: 1,
  logEntryCount: 214,
  documentCount: 6,
  documentBytes: 4_718_592,
  assistantTokenCount: 2,
}

const META = {
  applicationName: 'CarTracker',
  version: '0.13.0',
  environment: 'Test',
  serverTimeUtc: '2026-08-13T09:00:00Z',
  identityDeletionConfigured: true,
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

/** Every request the panel makes, so a test can assert what was sent as well as what was rendered. */
interface Call {
  url: string
  method: string
  body: string | null
}

function mockApi(options: { deletionConfigured?: boolean; deleteStatus?: number; deleteBody?: unknown } = {}) {
  const calls: Call[] = []

  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL, init?: RequestInit) => {
      const path = String(url)
      calls.push({ url: path, method: init?.method ?? 'GET', body: init?.body === undefined ? null : String(init.body) })

      if (path === '/api/meta') {
        return json({ ...META, identityDeletionConfigured: options.deletionConfigured ?? true })
      }
      if (path === '/api/account/summary') return json(ACCOUNT)
      if (path === '/api/account/export') {
        // A string body rather than a Blob: jsdom's Blob does not stream, and Response accepts either.
        return new Response('{"exportedAt":"2026-08-13T09:00:00Z"}', {
          status: 200,
          headers: {
            'Content-Type': 'application/json',
            'Content-Disposition': 'attachment; filename="cartracker-export-2026-08-13.json"',
          },
        })
      }
      if (path === '/api/account' && init?.method === 'DELETE') {
        const status = options.deleteStatus ?? 204
        return status === 204 ? new Response(null, { status: 204 }) : json(options.deleteBody, status)
      }
      return json({})
    }),
  )

  return calls
}

const renderPanel = () =>
  render(
    <QueryClientProvider client={createQueryClient()}>
      <ToastProvider>
        <div id="root">
          <DangerZonePanel />
        </div>
      </ToastProvider>
    </QueryClientProvider>,
  )

beforeEach(() => {
  __resetScrollLock()
  h.logout.mockClear()
  URL.createObjectURL = vi.fn(() => 'blob:mock')
  URL.revokeObjectURL = vi.fn()
})

afterEach(() => vi.unstubAllGlobals())

describe('settings — your account', () => {
  it('states what the account holds before the confirmation will arm', async () => {
    mockApi()
    renderPanel()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /delete account/i }))

    // The counts in prose. "This will delete everything" without saying how much everything is asks for
    // consent it has not informed.
    expect(
      screen.getByText(/1 vehicle, 214 log entries, 6 documents and 2 assistant tokens/),
    ).toBeInTheDocument()
    // Including the login, which is the part nothing else on the screen would lead you to expect.
    expect(screen.getByText(/your login/i)).toBeInTheDocument()

    expect(screen.getByRole('button', { name: /delete everything/i })).toBeDisabled()
  })

  it('arms only on the exact address, and signs out once the account is gone', async () => {
    const calls = mockApi()
    renderPanel()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /delete account/i }))
    const confirm = screen.getByRole('button', { name: /delete everything/i })
    const field = screen.getByLabelText(/type your email/i)

    // A near miss is still a miss — this is the gate, not a formality.
    await user.type(field, 'you@example.tes')
    expect(confirm).toBeDisabled()

    await user.type(field, 't')
    expect(confirm).toBeEnabled()

    await user.click(confirm)

    const deletion = await vi.waitFor(() => {
      const call = calls.find((c) => c.method === 'DELETE')
      expect(call).toBeDefined()
      return call!
    })
    // The endpoint requires the address too: the client is not the only possible caller.
    expect(deletion.url).toBe('/api/account')
    expect(JSON.parse(deletion.body!)).toEqual({ confirmEmail: 'you@example.test' })

    // There is no account behind the session any more, so the last act is a sign-out and not a re-render.
    await vi.waitFor(() => expect(h.logout).toHaveBeenCalled())
    expect(await screen.findByText(/Everything this account held is gone/)).toBeInTheDocument()
  })

  it('marks the field when the server refuses the confirmation', async () => {
    mockApi({
      deleteStatus: 400,
      deleteBody: {
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: { confirmEmail: ['Type you@example.test exactly to confirm. This is irreversible.'] },
      },
    })
    renderPanel()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: /delete account/i }))
    await user.type(screen.getByLabelText(/type your email/i), 'you@example.test')
    await user.click(screen.getByRole('button', { name: /delete everything/i }))

    // Beside the field, not a banner over a screen whose only control is a destructive button.
    expect(await screen.findByRole('alert')).toHaveTextContent(/exactly to confirm/)
    expect(screen.getByLabelText(/type your email/i)).toHaveAttribute('aria-invalid', 'true')
    expect(h.logout).not.toHaveBeenCalled()
  })

  it('degrades to export-only when the deployment cannot erase the login', async () => {
    mockApi({ deletionConfigured: false })
    renderPanel()

    // A reason in place of the button, rather than a button that answers 503.
    expect(await screen.findByText(/Deletion is unavailable on this deployment/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /delete account/i })).not.toBeInTheDocument()
    // The export is still offered — the data is the owner's either way.
    expect(screen.getByRole('button', { name: /download my data/i })).toBeInTheDocument()
  })

  it('downloads the export through the authenticated seam, under the name the server gave it', async () => {
    const calls = mockApi()
    const { container } = renderPanel()
    const user = userEvent.setup()

    const clicks: HTMLAnchorElement[] = []
    const created = window.document.createElement.bind(window.document)
    vi.spyOn(window.document, 'createElement').mockImplementation((tag: string) => {
      const element = created(tag)
      if (tag === 'a') {
        const anchor = element as HTMLAnchorElement
        anchor.click = () => clicks.push(anchor)
      }
      return element
    })

    await user.click(await screen.findByRole('button', { name: /download my data/i }))

    await vi.waitFor(() => expect(clicks).toHaveLength(1))
    // An object URL, not a link to the endpoint: a plain navigation carries cookies, not our bearer, and would
    // save a 401 page instead of the export.
    expect(clicks[0]!.href).toContain('blob:mock')
    expect(clicks[0]!.download).toBe('cartracker-export-2026-08-13.json')
    expect(container.querySelector('a[href*="/api/account/export"]')).toBeNull()
    expect(calls.some((c) => c.url === '/api/account/export' && c.method === 'GET')).toBe(true)
  })

  it('has no axe violations, panel and confirmation', async () => {
    mockApi()
    const { container } = renderPanel()
    const user = userEvent.setup()

    await screen.findByRole('button', { name: /download my data/i })
    expect(await axe(container)).toHaveNoViolations()

    // document.body, not the container: the sheet is portalled out of #root so `inert` can be set on it, and a
    // sweep of the container would report clean on a dialog it never looked at.
    await user.click(screen.getByRole('button', { name: /delete account/i }))
    expect(await axe(document.body)).toHaveNoViolations()
  })
})
