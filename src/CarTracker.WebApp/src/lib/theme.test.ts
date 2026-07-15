import { beforeEach, describe, expect, it } from 'vitest'
import { applyTheme, readStoredTheme, resolveTheme, storeTheme, THEME_STORAGE_KEY } from './theme'

beforeEach(() => {
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
})

describe('resolution order: stored preference, then the OS, then light', () => {
  it('prefers an explicit stored choice over the OS', () => {
    storeTheme('light')
    expect(readStoredTheme()).toBe('light')
    // A stored 'light' stays light even when the OS asks for dark.
    expect(resolveTheme('light', true)).toBe('light')

    storeTheme('dark')
    expect(resolveTheme('dark', false)).toBe('dark')
  })

  it('falls back to the OS when the choice is system', () => {
    expect(resolveTheme('system', true)).toBe('dark')
    expect(resolveTheme('system', false)).toBe('light')
  })

  it('falls back to light when nothing is stored and the OS has no preference', () => {
    expect(readStoredTheme()).toBeNull()
    expect(resolveTheme(readStoredTheme() ?? 'system', false)).toBe('light')
  })

  it('ignores a stored value that is not a theme', () => {
    localStorage.setItem(THEME_STORAGE_KEY, 'chartreuse')
    expect(readStoredTheme()).toBeNull()
  })
})

describe('applyTheme writes the CSS contract', () => {
  it('sets data-theme for an explicit choice', () => {
    applyTheme('dark')
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    applyTheme('light')
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
  })

  // Not a detail. tokens.css resolves the OS preference itself, via
  // `@media (prefers-color-scheme: dark) :root:not([data-theme='light'])`. Writing a resolved value here
  // would freeze the theme at whatever the OS said when the page loaded, and stop it tracking a live change.
  it('removes data-theme for system, rather than writing a resolved value', () => {
    applyTheme('dark')
    applyTheme('system')
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false)
  })
})
