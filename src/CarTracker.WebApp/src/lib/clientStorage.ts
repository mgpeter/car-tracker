/**
 * Every key this app writes to the browser, in one place.
 *
 * **This is the source the cookie notice renders**, rather than a hand-written list beside it. A prose
 * inventory of what a program stores drifts from what it stores - the same failure as the hand-typed expense
 * categories that drifted from the seed and 400'd on save, except that here the drift is a false statement in
 * a legal document rather than a bad request. `clientStorage.test.ts` fails the build on a key in the codebase
 * that is not declared here.
 *
 * **It is also where the no-banner decision comes up for review** (DEC-024). There is no cookie consent banner
 * because nothing here needs consent: two of these are the session, three are preferences the visitor chose,
 * and none of it is measurement, advertising or shared with anybody. Adding an entry whose `kind` is neither
 * `session` nor `preference` is the moment that stops being true, and the type makes you write it down.
 */

/**
 * Why a key exists, which is what decides whether consent is needed.
 *
 * A third member - anything analytics-shaped - is deliberately absent. Adding one is a decision about consent
 * and not a typing convenience, so it should not be possible to reach for absent-mindedly.
 */
export type StorageKind = 'session' | 'preference'

export interface StorageEntry {
  /** The literal key, or a description of its shape where one key exists per subject. */
  key: string
  /** What it is called on the page. */
  label: string
  /** What it is for, in a sentence a car owner can read. */
  purpose: string
  /** How long it stays. */
  lifetime: string
  kind: StorageKind
  /**
   * Set where the key is composed by a library at runtime, so the guard cannot resolve it from source and it
   * is registered by hand. Rendered nowhere; it exists so the exception is visible here rather than in a test.
   */
  unverifiable?: string
}

export const THEME_KEY = 'ct-theme'
export const FUEL_UNIT_KEY = 'ct-fuel-unit'
export const SETTINGS_KEY = 'cartracker.settings'
export const DISMISSED_KEY_PREFIX = 'ct-attn-dismissed:'

export const CLIENT_STORAGE: readonly StorageEntry[] = [
  {
    key: '@@auth0spajs@@…',
    label: 'Your sign-in',
    purpose:
      'Keeps you signed in between page loads. Without it you would have to log in again on every screen.',
    lifetime: 'Until you sign out.',
    kind: 'session',
    unverifiable:
      "Composed at runtime by @auth0/auth0-react from the tenant and client id, so no literal appears in this "
      + "codebase to check against. Set by cacheLocation='localstorage' in main.tsx.",
  },
  {
    key: SETTINGS_KEY,
    label: 'App settings',
    purpose:
      'Holds an API key used by an older way of signing in. It is not how anybody signs in today and grants no '
      + 'access to any vehicle.',
    lifetime: 'Until you clear your browser storage.',
    kind: 'session',
  },
  {
    key: THEME_KEY,
    label: 'Light or dark',
    purpose: 'Remembers the theme you picked, so the page does not flash the wrong one while it loads.',
    lifetime: 'Until you change it or clear your browser storage.',
    kind: 'preference',
  },
  {
    key: FUEL_UNIT_KEY,
    label: 'Fuel units',
    purpose: 'Remembers whether you read fuel economy as MPG or L/100 km.',
    lifetime: 'Until you change it or clear your browser storage.',
    kind: 'preference',
  },
  {
    key: `${DISMISSED_KEY_PREFIX}<registration>`,
    label: 'Dismissed notices',
    purpose:
      'Remembers that you closed the attention panel on a particular car, so it stays closed. One entry per '
      + 'car you dismiss it on.',
    lifetime: 'Until the panel has something new to say, or you clear your browser storage.',
    kind: 'preference',
  },
]

/**
 * True while nothing stored needs consent.
 *
 * Read by the cookie notice, so the page cannot claim no consent is required after somebody adds something
 * that requires it. If this ever returns false the notice says so rather than quietly continuing to promise
 * otherwise - and at that point DEC-024's banner decision is due for review.
 */
export const everythingIsExempt = (): boolean =>
  CLIENT_STORAGE.every((entry) => entry.kind === 'session' || entry.kind === 'preference')
