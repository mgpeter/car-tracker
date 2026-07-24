import { useAuth0 } from '@auth0/auth0-react'
import { Icon } from '../components/Icon'

/**
 * The signed-in identity and sign-out, in the top bar. A native `<details>` like the More menu — CSS-only,
 * keyboard-accessible, no open/close state to own. Renders nothing when not authenticated (the AuthGate means
 * that state never reaches a real screen, but the guard keeps it honest).
 */
export function UserMenu() {
  const { isAuthenticated, user, logout } = useAuth0()

  if (!isAuthenticated) return null

  const label = user?.email ?? user?.name ?? 'Account'

  return (
    <details className="more usermenu">
      <summary aria-label={`Account: ${label}`}>
        {label} <Icon name="caret-down" />
      </summary>
      <div className="more-panel">
        <button
          type="button"
          onClick={() => logout({ logoutParams: { returnTo: window.location.origin } })}
        >
          Sign out
        </button>
      </div>
    </details>
  )
}
