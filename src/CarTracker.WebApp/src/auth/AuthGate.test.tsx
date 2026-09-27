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
  h.getAccessTokenSilently.mockReset()
  h.getAccessTokenSilently.mockImplementation(async () => 'bridge-token')
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

describe('the refusal names its own reason', () => {
  /**
   * The server writes three different sentences — nobody could read your address, nobody has proved it is
   * yours, or it is yours and not on the list — because they are three different things to do next. This
   * panel used to discard all three and assert the last one, while naming the address from the ID token,
   * which the browser has and the API does not. So a deployment with no Management credential told its owner
   * their address was uninvited, naming an address the server had never resolved.
   */
  const refusedBecause = (detail: string) => () =>
    new Response(
      JSON.stringify({ type: 'signup-not-invited', title: 'Not yet invited', detail, status: 403 }),
      { status: 403, headers: { 'Content-Type': 'application/problem+json' } },
    )

  it('renders the server sentence rather than assuming the address was uninvited', async () => {
    h.state = { isAuthenticated: true, isLoading: false, error: undefined }
    mockAccess(
      refusedBecause(
        'We could not read the email address behind this sign-in, so it cannot be checked against the invitation list.',
      ),
    )
    renderGate(<div>secret garage</div>)

    expect(await screen.findByText(/could not read the email address/i)).toBeInTheDocument()
    // The sentence this panel used to assert regardless of which refusal fired.
    expect(screen.queryByText(/has not been invited/i)).not.toBeInTheDocument()
    expect(screen.queryByText('secret garage')).not.toBeInTheDocument()
  })

  it('still names the signed-in address, because using the wrong one is the commonest cause', async () => {
    h.state = { isAuthenticated: true, isLoading: false, error: undefined }
    mockAccess(refusedBecause('This CarTracker is invitation-only, and someone@example.test is not on the list.'))
    renderGate(<div>secret garage</div>)

    expect(await screen.findByText(/is not on the list/i)).toBeInTheDocument()
    expect(screen.getByText('stranger@example.test')).toBeInTheDocument()
  })
})

describe('a session the SDK still believes in, whose refresh token is dead', () => {
  /**
   * After a few weeks away the refresh token has expired. The SDK's `checkSession` swallows the refusal and its
   * cached user still reads as signed in, so the gate is handed `isAuthenticated: true` with no way to get a
   * token. It used to render the app anyway, every request went out bare and 401'd, and each one asked Auth0
   * again until the tenant rate-limited the page into a permanent splash.
   */
  const authError = (error: string, message = error) => Object.assign(new Error(message), { error })

  it.each(['invalid_grant', 'missing_refresh_token', 'login_required'])(
    'ends the session on %s and says so on the sign-in page, without calling the API',
    async (code) => {
      h.state.isAuthenticated = true
      h.getAccessTokenSilently.mockRejectedValue(authError(code))
      const fetchMock = vi.fn(async () => admitted())
      vi.stubGlobal('fetch', fetchMock)

      renderGate(<div>secret garage</div>)

      // The mock's logout does not flip isAuthenticated the way the real one does; what matters is that the
      // local session is cleared without leaving for Auth0's logout page.
      await vi.waitFor(() => expect(h.logout).toHaveBeenCalledWith({ openUrl: false }))
      expect(h.logout).toHaveBeenCalledTimes(1)
      expect(screen.queryByText('secret garage')).not.toBeInTheDocument()
      expect(fetchMock).not.toHaveBeenCalledWith('/api/meta/authenticated', expect.anything())
    },
  )

  it('shows the expiry notice once the session has ended', async () => {
    h.state.isAuthenticated = true
    h.getAccessTokenSilently.mockRejectedValue(authError('invalid_grant'))
    const view = renderGate(<div>secret garage</div>)

    await vi.waitFor(() => expect(h.logout).toHaveBeenCalled())
    // What the real logout does next: the SDK drops its cached user.
    h.state.isAuthenticated = false
    view.rerender(
      <QueryClientProvider client={createQueryClient()}>
        <AuthGate>
          <div>secret garage</div>
        </AuthGate>
      </QueryClientProvider>,
    )

    expect(await screen.findByRole('status')).toHaveTextContent(/session has expired/i)
    expect(screen.getAllByRole('button', { name: /log in/i }).length).toBeGreaterThan(0)
  })

  it('holds on a retry splash for a transient failure, and recovers on Try again', async () => {
    h.state.isAuthenticated = true
    h.getAccessTokenSilently.mockRejectedValueOnce(authError('too_many_requests', 'Global rate limit exceeded'))
    const fetchMock = vi.fn(async () => admitted())
    vi.stubGlobal('fetch', fetchMock)

    renderGate(<div>secret garage</div>)

    const retry = await screen.findByRole('button', { name: /try again/i })
    // A rate limit is not a dead session: signing somebody out whenever the tenant hiccups would be worse.
    expect(h.logout).not.toHaveBeenCalled()
    // And nothing below the gate rendered, so nothing is left asking. (`/api/meta` is anonymous and always asked.)
    expect(fetchMock).not.toHaveBeenCalledWith('/api/meta/authenticated', expect.anything())

    await userEvent.setup().click(retry)
    expect(await screen.findByText('secret garage')).toBeInTheDocument()
  })

  it('ends the session once when requests mid-visit discover the refresh token has died', async () => {
    h.state.isAuthenticated = true
    renderGate(<div>secret garage</div>)
    expect(await screen.findByText('secret garage')).toBeInTheDocument()

    h.getAccessTokenSilently.mockRejectedValue(authError('invalid_grant'))
    vi.stubGlobal('fetch', vi.fn(async () => new Response('{}', { status: 401 })))
    vi.spyOn(console, 'warn').mockImplementation(() => {})

    await Promise.all([apiRequest('/api/vehicles'), apiRequest('/api/meta/authenticated')])

    await vi.waitFor(() => expect(h.logout).toHaveBeenCalledWith({ openUrl: false }))
    expect(h.logout).toHaveBeenCalledTimes(1)
  })
})
