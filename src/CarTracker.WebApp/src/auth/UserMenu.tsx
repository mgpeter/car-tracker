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
  const initial = label.trim().charAt(0) || '?'

  return (
    <details className="more usermenu">
      {/* Two renderings of one control: the address on a desktop bar, a single initial in a ring on a phone,
          where a 26ch email was the widest unshrinkable item in the row. The accessible name is on the
          <summary> and carries the full address in both cases, so the phone form loses nothing but pixels. */}
      <summary aria-label={`Account: ${label}`}>
        <span className="um-full">{label}</span>
        <span className="um-initial" aria-hidden="true">
          {initial}
        </span>
        <Icon name="caret-down" />
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
