import { SCREEN_IDS } from '../shell/nav'

/**
 * Where a visitor goes after signing in.
 *
 * A filing link opened on a phone that is not signed in goes through Auth0, which comes back to the origin. The
 * path and query ride along as `appState.returnTo` and are restored here (assumed: A2). The value comes back
 * through the browser, so it is untrusted: an unchecked return path is an open redirect, and the app's sign-in
 * page is exactly where someone would plant one.
 *
 * So the guard is narrow on purpose. A value is restored only when it is a plain string that starts with one
 * `/`, carries no whitespace, control character, backslash or encoded slash, resolves against this origin to
 * this origin, and names one of the app's gated routes. Anything else lands on the garage.
 */

/** Gated routes with no vehicle in them. The legal pages are public and need no return. */
const UNSCOPED = new Set(['file', 'account', 'admin', 'gallery'])

/** Screens under a vehicle: the nav table's, and the assistant, which has a route and no nav entry. */
const SCOPED = new Set<string>([...SCREEN_IDS, 'assistant'])

/** A registration as the app writes it in a URL: letters and digits, no spaces (`hrefFor`). */
const SLUG = /^[a-z0-9]{1,16}$/i

/** Never in an app path, raw or percent-encoded: whitespace, controls, backslashes, `%2F`, `%5C` and encoded controls. */
// eslint-disable-next-line no-control-regex
const SUSPECT_PATH = /[\s\u0000-\u001f\u007f\\]|%2f|%5c|%0|%1|%7f/i

/** Never in a query either: raw whitespace and controls. An encoded slash is fine there (a title of "1/2 tank"). */
// eslint-disable-next-line no-control-regex
const SUSPECT_QUERY = /[\s\u0000-\u001f\u007f]/

function isAppRoute(pathname: string): boolean {
  if (pathname === '/') return true
  const segments = pathname.slice(1).split('/')
  if (segments.length === 1) return UNSCOPED.has(segments[0]!)
  if (segments.length === 2) return SLUG.test(segments[0]!) && SCOPED.has(segments[1]!)
  return false
}

/** The path and query to restore, or `/` when the value is anything but a safe path to an app route. */
export function safeReturnTo(value: unknown): string {
  if (typeof value !== 'string' || value === '') return '/'
  if (!value.startsWith('/') || value.startsWith('//')) return '/'
  const queryAt = value.indexOf('?')
  const path = queryAt === -1 ? value : value.slice(0, queryAt)
  const query = queryAt === -1 ? '' : value.slice(queryAt)
  if (SUSPECT_PATH.test(path) || SUSPECT_QUERY.test(query)) return '/'

  let url: URL
  try {
    url = new URL(value, window.location.origin)
  } catch {
    return '/'
  }

  if (url.origin !== window.location.origin) return '/'
  if (!isAppRoute(url.pathname)) return '/'

  // Rebuilt from the parsed URL rather than echoed, so what is restored is what was checked. The fragment is
  // dropped: no screen reads one.
  return url.pathname + url.search
}

/** What to carry through sign-in from where the visitor is now, or nothing when there is nowhere to return to. */
export function returnToFrom(location: { pathname: string; search: string }): string | undefined {
  const here = location.pathname + location.search
  const safe = safeReturnTo(here)
  return safe === '/' ? undefined : safe
}

/**
 * Auth0's redirect callback: go to the saved path, or to the garage, replacing the callback URL so its `code`
 * and `state` do not stay in the address bar or the history.
 *
 * Navigates through the router rather than `history.replaceState`, because the router has already read the
 * callback URL and would not notice the address changing underneath it.
 */
export function restoreAfterSignIn(appState: { returnTo?: unknown } | undefined, navigate: (to: string) => void) {
  navigate(safeReturnTo(appState?.returnTo))
}
