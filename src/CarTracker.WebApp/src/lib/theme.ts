/** The user's choice. `system` means "follow the OS", and is the default. */
export type Theme = 'light' | 'dark' | 'system'

/** What `system` actually resolves to right now. */
export type ResolvedTheme = 'light' | 'dark'

/** Kept from the design (`archive/dashboard-full-claude-design`), which used the same key. */
export const THEME_STORAGE_KEY = 'ct-theme'

export const DARK_QUERY = '(prefers-color-scheme: dark)'

function isTheme(value: unknown): value is Theme {
  return value === 'light' || value === 'dark' || value === 'system'
}

/** The stored preference, or null when absent or unparseable. */
export function readStoredTheme(): Theme | null {
  try {
    const raw = localStorage.getItem(THEME_STORAGE_KEY)
    return isTheme(raw) ? raw : null
  } catch {
    // localStorage throws in private-mode Safari and when storage is disabled. A missing preference is a
    // supported state — the OS decides — so this is not worth surfacing.
    return null
  }
}

export function storeTheme(theme: Theme): void {
  try {
    localStorage.setItem(THEME_STORAGE_KEY, theme)
  } catch {
    // As above: the theme still applies for this session, it just will not survive a reload.
  }
}

/** Resolution order: stored preference, then the OS, then light. */
export function resolveTheme(theme: Theme, prefersDark: boolean): ResolvedTheme {
  if (theme === 'system') return prefersDark ? 'dark' : 'light'
  return theme
}

/**
 * Write the choice to <html>.
 *
 * `system` REMOVES the attribute rather than writing a resolved value, which is what lets the OS preference
 * be handled entirely in CSS (`@media (prefers-color-scheme: dark) :root:not([data-theme='light'])`). That
 * matters for more than tidiness: it means the pre-paint script never has to ask the OS anything, and the
 * theme keeps tracking the OS live without JS re-running. The design used the same convention.
 */
export function applyTheme(theme: Theme, root: HTMLElement = document.documentElement): void {
  if (theme === 'system') root.removeAttribute('data-theme')
  else root.setAttribute('data-theme', theme)
}

export function prefersDark(): boolean {
  return typeof matchMedia === 'function' && matchMedia(DARK_QUERY).matches
}
