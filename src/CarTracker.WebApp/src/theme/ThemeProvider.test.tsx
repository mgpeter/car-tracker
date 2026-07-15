import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { THEME_STORAGE_KEY } from '../lib/theme'
import { ThemeProvider, useTheme } from './ThemeProvider'

/** jsdom has no matchMedia. This is a controllable one, so "System tracks the OS" is actually testable. */
function mockMatchMedia(matches: boolean) {
  const listeners = new Set<(e: MediaQueryListEvent) => void>()
  const mql = {
    matches,
    media: '(prefers-color-scheme: dark)',
    addEventListener: (_: string, l: (e: MediaQueryListEvent) => void) => listeners.add(l),
    removeEventListener: (_: string, l: (e: MediaQueryListEvent) => void) => listeners.delete(l),
    dispatch(next: boolean) {
      mql.matches = next
      for (const l of listeners) l({ matches: next } as MediaQueryListEvent)
    },
  }
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => mql),
  )
  return mql
}

function Probe() {
  const { theme, resolved, setTheme } = useTheme()
  return (
    <div>
      <span data-testid="theme">{theme}</span>
      <span data-testid="resolved">{resolved}</span>
      <button type="button" onClick={() => setTheme('dark')}>
        Dark
      </button>
      <button type="button" onClick={() => setTheme('light')}>
        Light
      </button>
      <button type="button" onClick={() => setTheme('system')}>
        System
      </button>
    </div>
  )
}

beforeEach(() => {
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('ThemeProvider', () => {
  it('defaults to system and resolves against the OS', () => {
    mockMatchMedia(true)
    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    )
    expect(screen.getByTestId('theme')).toHaveTextContent('system')
    expect(screen.getByTestId('resolved')).toHaveTextContent('dark')
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false)
  })

  it('lands data-theme on <html> and persists the choice', async () => {
    mockMatchMedia(false)
    const user = userEvent.setup()
    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    )

    await user.click(screen.getByRole('button', { name: 'Dark' }))

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('dark')
    expect(screen.getByTestId('resolved')).toHaveTextContent('dark')
  })

  it('persists across a remount', async () => {
    mockMatchMedia(false)
    const user = userEvent.setup()
    const { unmount } = render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    )
    await user.click(screen.getByRole('button', { name: 'Dark' }))
    unmount()
    document.documentElement.removeAttribute('data-theme')

    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    )
    expect(screen.getByTestId('theme')).toHaveTextContent('dark')
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
  })

  it('System tracks a live OS change', async () => {
    const mql = mockMatchMedia(false)
    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    )
    expect(screen.getByTestId('resolved')).toHaveTextContent('light')

    await vi.waitFor(() => {
      mql.dispatch(true)
      expect(screen.getByTestId('resolved')).toHaveTextContent('dark')
    })
    // Still no attribute: system is the absence of one, and CSS did the work.
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false)
  })

  it('an explicit choice does not follow the OS', async () => {
    const mql = mockMatchMedia(false)
    const user = userEvent.setup()
    render(
      <ThemeProvider>
        <Probe />
      </ThemeProvider>,
    )
    await user.click(screen.getByRole('button', { name: 'Light' }))

    mql.dispatch(true)

    expect(screen.getByTestId('resolved')).toHaveTextContent('light')
    expect(document.documentElement.getAttribute('data-theme')).toBe('light')
  })
})
