import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { axe } from '../test/axe'

// A fully controllable Auth0 for this file (overriding the signed-in default in test/setup), so the gate can be
// exercised in each state.
const h = vi.hoisted(() => ({
  loginWithRedirect: vi.fn(),
  logout: vi.fn(),
  getAccessTokenSilently: vi.fn(async () => 'bridge-token'),
  state: { isAuthenticated: false, isLoading: false, error: undefined as { message: string } | undefined },
}))

vi.mock('@auth0/auth0-react', () => ({
  useAuth0: () => ({
    isAuthenticated: h.state.isAuthenticated,
    isLoading: h.state.isLoading,
    error: h.state.error,
    user: { email: 'stranger@example.test' },
    loginWithRedirect: h.loginWithRedirect,
    logout: h.logout,
    getAccessTokenSilently: h.getAccessTokenSilently,
  }),
}))

import { apiRequest, setAccessTokenProvider } from '../api/client'
import { createQueryClient } from '../api/queries'
import { AuthGate } from './AuthGate'

/**
 * The gate now makes one API call above the router — the access check that tells an admitted account from a
 * signed-in stranger. Every render therefore needs a query client and an answer to that call.
 */
function renderGate(children = <div />) {
  return render(
    <QueryClientProvider client={createQueryClient()}>
      <AuthGate>{children}</AuthGate>
    </QueryClientProvider>,
  )
}

/** The access check answering however this test needs it to; everything else is irrelevant to the gate. */
function mockAccess(response: () => Response) {
  vi.stubGlobal('fetch', vi.fn(async () => response()))
}

const admitted = () =>
  new Response(JSON.stringify({ authenticated: true }), {
    status: 200,
    headers: { 'Content-Type': 'application/json' },
  })

const refused = () =>
  new Response(
    JSON.stringify({ type: 'signup-not-invited', title: 'Not yet invited', detail: 'This address is not on the list.', status: 403 }),
    { status: 403, headers: { 'Content-Type': 'application/problem+json' } },
  )

beforeEach(() => mockAccess(admitted))

afterEach(() => {
  h.state = { isAuthenticated: false, isLoading: false, error: undefined }
  h.loginWithRedirect.mockClear()
  h.logout.mockClear()
  setAccessTokenProvider(null)
  vi.unstubAllGlobals()
})

describe('AuthGate', () => {
  it('walls off the app when signed out and shows the public landing page', () => {
    renderGate(<div>secret garage</div>)
    // The app is not rendered — nothing can flash another user's data before the redirect.
    expect(screen.queryByText('secret garage')).not.toBeInTheDocument()
    // The landing page, not a bare login prompt: a stranger is told what they are signing in to.
    expect(screen.getByRole('heading', { level: 1 })).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /log in/i }).length).toBeGreaterThan(0)
    expect(screen.getAllByRole('button', { name: /sign up/i }).length).toBeGreaterThan(0)
  })

  it('starts the Auth0 redirect on log in, and the signup hint on sign up', async () => {
    renderGate()
    const user = userEvent.setup()

    // The landing page repeats both CTAs; either instance must carry the same arguments.
    await user.click(screen.getAllByRole('button', { name: /log in/i })[0]!)
    expect(h.loginWithRedirect).toHaveBeenLastCalledWith()

    // The whole point of the sign-up button: without screen_hint a newcomer lands on the LOGIN form and is
    // asked for credentials they do not have. This assertion is the only thing proving it is sent.
    await user.click(screen.getAllByRole('button', { name: /sign up/i })[0]!)
    expect(h.loginWithRedirect).toHaveBeenLastCalledWith({ authorizationParams: { screen_hint: 'signup' } })
  })

  it('shows a spinner-free splash while the session is still loading', () => {
    h.state.isLoading = true
    renderGate(<div>secret garage</div>)
    expect(screen.queryByText('secret garage')).not.toBeInTheDocument()
    expect(screen.getByText(/checking your session/i)).toBeInTheDocument()
  })

  it('renders the app once authenticated and attaches the bearer to API calls', async () => {
    h.state.isAuthenticated = true
    renderGate(<div>secret garage</div>)

    // Not synchronously: the access check runs before the app does, so a signed-in stranger never gets a frame
    // of someone else's screen while the answer is in flight.
    expect(await screen.findByText('secret garage')).toBeInTheDocument()

    // The bridge registered the token getter; a request now carries it same-origin to /api.
    const fetchMock = vi.fn(
      async (_url: string | URL, _init?: RequestInit) =>
        new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } }),
    )
    vi.stubGlobal('fetch', fetchMock)

    await apiRequest('/api/meta')

    const headers = fetchMock.mock.calls[0]![1]!.headers as Headers
    expect(headers.get('Authorization')).toBe('Bearer bridge-token')
  })

  it('refuses a signed-in stranger with the not-invited panel, and neither the app nor the landing page', async () => {
    h.state.isAuthenticated = true
    mockAccess(refused)
    renderGate(<div>secret garage</div>)

    expect(await screen.findByRole('heading', { name: /not yet invited/i })).toBeInTheDocument()
    expect(screen.queryByText('secret garage')).not.toBeInTheDocument()
    // Not LandingPage either: inviting someone to sign up for what they were just refused is worse than saying
    // nothing. Its sign-up CTA is the tell.
    expect(screen.queryByRole('button', { name: /sign up/i })).not.toBeInTheDocument()

    // The address is named, because the commonest cause is signing in with a different one from the invitation.
    expect(screen.getByText('stranger@example.test')).toBeInTheDocument()

    const user = userEvent.setup()
    await user.click(screen.getByRole('button', { name: /sign out/i }))
    expect(h.logout).toHaveBeenCalledWith({ logoutParams: { returnTo: window.location.origin } })
  })

  it('lets the app render when the access check fails for any other reason', async () => {
    h.state.isAuthenticated = true
    mockAccess(() => new Response('{}', { status: 500, headers: { 'Content-Type': 'application/json' } }))
    renderGate(<div>secret garage</div>)

    // A gate that locked everyone out whenever it could not reach the server would turn a transient outage into
    // a lockout. Only the one specific refusal stops the app.
    expect(await screen.findByText('secret garage')).toBeInTheDocument()
  })

  it('has no axe violations on the login wall', async () => {
    const { container } = renderGate()
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no axe violations on the not-invited panel', async () => {
    h.state.isAuthenticated = true
    mockAccess(refused)
    const { container } = renderGate()

    await screen.findByRole('heading', { name: /not yet invited/i })
    expect(await axe(container)).toHaveNoViolations()
  })
})
