import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
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
})

const renderMenu = () =>
  render(
    <>
      <IconSprite />
      <UserMenu />
    </>,
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
    const { container } = render(<UserMenu />)
    expect(container).toBeEmptyDOMElement()
  })

  it('has no axe violations', async () => {
    const { container } = renderMenu()
    expect(await axe(container)).toHaveNoViolations()
  })
})
