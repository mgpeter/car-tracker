import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { axe } from '../test/axe'

// A fully controllable Auth0 for this file (overriding the signed-in default in test/setup), so the gate can be
// exercised in each state.
const h = vi.hoisted(() => ({
  loginWithRedirect: vi.fn(),
  getAccessTokenSilently: vi.fn(async () => 'bridge-token'),
  state: { isAuthenticated: false, isLoading: false, error: undefined as { message: string } | undefined },
}))

vi.mock('@auth0/auth0-react', () => ({
  useAuth0: () => ({
    isAuthenticated: h.state.isAuthenticated,
    isLoading: h.state.isLoading,
    error: h.state.error,
    loginWithRedirect: h.loginWithRedirect,
    getAccessTokenSilently: h.getAccessTokenSilently,
  }),
}))

import { apiRequest, setAccessTokenProvider } from '../api/client'
import { AuthGate } from './AuthGate'

afterEach(() => {
  h.state = { isAuthenticated: false, isLoading: false, error: undefined }
  h.loginWithRedirect.mockClear()
  setAccessTokenProvider(null)
  vi.unstubAllGlobals()
})

describe('AuthGate', () => {
  it('walls off the app when signed out and offers login and signup', () => {
    render(
      <AuthGate>
        <div>secret garage</div>
      </AuthGate>,
    )
    // The app is not rendered — nothing can flash another user's data before the redirect.
    expect(screen.queryByText('secret garage')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /log in/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /sign up/i })).toBeInTheDocument()
  })

  it('starts the Auth0 redirect on log in, and the signup hint on sign up', async () => {
    render(
      <AuthGate>
        <div />
      </AuthGate>,
    )
    const user = userEvent.setup()

    await user.click(screen.getByRole('button', { name: /log in/i }))
    expect(h.loginWithRedirect).toHaveBeenLastCalledWith()

    await user.click(screen.getByRole('button', { name: /sign up/i }))
    expect(h.loginWithRedirect).toHaveBeenLastCalledWith({ authorizationParams: { screen_hint: 'signup' } })
  })

  it('shows a spinner-free splash while the session is still loading', () => {
    h.state.isLoading = true
    render(
      <AuthGate>
        <div>secret garage</div>
      </AuthGate>,
    )
    expect(screen.queryByText('secret garage')).not.toBeInTheDocument()
    expect(screen.getByText(/checking your session/i)).toBeInTheDocument()
  })

  it('renders the app once authenticated and attaches the bearer to API calls', async () => {
    h.state.isAuthenticated = true
    render(
      <AuthGate>
        <div>secret garage</div>
      </AuthGate>,
    )
    expect(screen.getByText('secret garage')).toBeInTheDocument()

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

  it('has no axe violations on the login wall', async () => {
    const { container } = render(
      <AuthGate>
        <div />
      </AuthGate>,
    )
    expect(await axe(container)).toHaveNoViolations()
  })
})
