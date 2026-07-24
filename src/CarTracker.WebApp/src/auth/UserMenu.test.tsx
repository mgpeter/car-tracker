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
