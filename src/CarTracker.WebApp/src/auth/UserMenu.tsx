import { useAuth0 } from '@auth0/auth0-react'
import { Icon } from '../components/Icon'
import { useLinkRenderer } from '../lib/link'

/** The account screen's path, written here because it is not in the nav table - see `CurrentScreen`. */
export const ACCOUNT_PATH = '/account'

/**
 * The signed-in identity, the account screen and sign-out, in the top bar. A native `<details>` like the More
 * menu - CSS-only, keyboard-accessible, no open/close state to own. Renders nothing when not authenticated
 * (the AuthGate means that state never reaches a real screen, but the guard keeps it honest).
 *
 * This is the *only* way to the account screen, and deliberately: everything on it - your data, the assistant
 * tokens, the reference lists, the unit preference - belongs to the person rather than to a car, so it has no
 * business in a nav bar whose every other entry is scoped to a registration. The URL is built here rather than
 * through `hrefFor` for the same reason `NavMoreSheet` builds the assistant's by hand: neither screen is in
 * the nav table, so there is no `ScreenId` to ask.
 */
export function UserMenu() {
  const { isAuthenticated, user, logout } = useAuth0()
  const renderLink = useLinkRenderer()

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
        {/* Above sign-out, because it is the thing you came here to do; sign-out is the exit. `.more-panel a`
            already styles this to match the button beside it, and gives it the aria-current accent bar the
            button can never have. */}
        {renderLink({ href: ACCOUNT_PATH, children: 'Account' })}
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
