import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../api/queries'
import { IconSprite } from '../components/IconSprite'
import { axe } from '../test/axe'

// Controllable Auth0, overriding the signed-in default from test/setup for this file.
const h = vi.hoisted(() => ({
  logout: vi.fn(),
  state: { isAuthenticated: true },
  user: { email: 'you@example.test', name: 'Test Owner' } as { email?: string; name?: string },
}))

vi.mock('@auth0/auth0-react', () => ({
  useAuth0: () => ({ isAuthenticated: h.state.isAuthenticated, user: h.user, logout: h.logout }),
}))

import { UserMenu } from './UserMenu'

afterEach(() => {
  h.state.isAuthenticated = true
  h.user = { email: 'you@example.test', name: 'Test Owner' }
  h.logout.mockClear()
  vi.unstubAllGlobals()
})

// The menu reads `/api/meta/authenticated` for the admin capability, so it needs a query client and something
// to answer with. Not an administrator here: the Admin link's presence is asserted in `shell.test.tsx`, which
// is where the whole bar is rendered.
beforeEach(() => {
  vi.stubGlobal(
    'fetch',
    vi.fn(
      async () =>
        new Response(
          JSON.stringify({ authenticated: true, admin: { canReadAdmin: false, canWritePlans: false } }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        ),
    ),
  )
})

/**
 * **Wrapped in a `QueryClientProvider`, which it did not need until 0.26.0.** `UserMenu` was pure - Auth0 and
 * a link renderer - and gating the Admin entry point on a server-supplied capability gave it a data
 * dependency. In the app there is always a client above it; here it has to be supplied.
 */
const renderMenu = () =>
  render(
    <QueryClientProvider client={createQueryClient()}>
      <IconSprite />
      <UserMenu />
    </QueryClientProvider>,
  )

describe('UserMenu', () => {
  it('shows the signed-in email and signs out returning to this origin', async () => {
    renderMenu()
    expect(screen.getByText('you@example.test')).toBeInTheDocument()

    await userEvent.setup().click(screen.getByRole('button', { name: /sign out/i }))
    expect(h.logout).toHaveBeenCalledWith({ logoutParams: { returnTo: window.location.origin } })
  })

  it('offers the account screen above sign out', async () => {
    renderMenu()

    // The only way to /account. Everything on that screen belongs to the person rather than to a car, so it
    // has no business in a nav bar whose every other entry is scoped to a registration.
    const account = screen.getByRole('link', { name: 'Account' })
    expect(account).toHaveAttribute('href', '/account')

    // Above sign-out, because it is what you came for; sign-out is the exit.
    const panel = account.closest('.more-panel') as HTMLElement
    const order = [...panel.children].map((el) => el.textContent)
    expect(order).toEqual(['Account', 'Sign out'])
  })

  it('renders nothing when signed out', () => {
    h.state.isAuthenticated = false
    const { container } = render(
      <QueryClientProvider client={createQueryClient()}>
        <UserMenu />
      </QueryClientProvider>,
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('has no axe violations', async () => {
    const { container } = renderMenu()
    expect(await axe(container)).toHaveNoViolations()
  })
})
