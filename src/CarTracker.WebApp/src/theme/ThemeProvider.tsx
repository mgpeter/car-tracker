import { createContext, use, useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import {
  applyTheme,
  DARK_QUERY,
  readStoredTheme,
  resolveTheme,
  storeTheme,
  type ResolvedTheme,
  type Theme,
} from '../lib/theme'

interface ThemeContextValue {
  /** What the user chose: light, dark, or system. */
  theme: Theme
  /** What that currently means on screen. */
  resolved: ResolvedTheme
  setTheme: (theme: Theme) => void
}

const ThemeContext = createContext<ThemeContextValue | null>(null)

/**
 * One theme provider, replacing the copy of this logic that the design pastes into all 17 screens — where it
 * is 17 competing writers to a single global (`document.documentElement`).
 */
export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(() => readStoredTheme() ?? 'system')
  const [systemDark, setSystemDark] = useState(
    () => typeof matchMedia === 'function' && matchMedia(DARK_QUERY).matches,
  )

  // `system` must keep tracking the OS while the app is open, not only at mount.
  useEffect(() => {
    if (typeof matchMedia !== 'function') return
    const mq = matchMedia(DARK_QUERY)
    const onChange = (e: MediaQueryListEvent) => setSystemDark(e.matches)
    mq.addEventListener('change', onChange)
    setSystemDark(mq.matches)
    return () => mq.removeEventListener('change', onChange)
  }, [])

  // Reconcile with the pre-paint script's work. It has already set the attribute for an explicit choice, so
  // in the common case this writes what is already there.
  useEffect(() => {
    applyTheme(theme)
  }, [theme])

  const setTheme = useCallback((next: Theme) => {
    setThemeState(next)
    storeTheme(next)
    applyTheme(next)
  }, [])

  const value = useMemo(
    () => ({ theme, resolved: resolveTheme(theme, systemDark), setTheme }),
    [theme, systemDark, setTheme],
  )

  return <ThemeContext value={value}>{children}</ThemeContext>
}

export function useTheme(): ThemeContextValue {
  const ctx = use(ThemeContext)
  if (ctx === null) throw new Error('useTheme must be used within a ThemeProvider')
  return ctx
}
