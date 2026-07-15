import type { Theme } from '../lib/theme'
import { useTheme } from './ThemeProvider'

const OPTIONS: ReadonlyArray<{ value: Theme; label: string }> = [
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
  { value: 'system', label: 'System' },
]

/**
 * The three-way control.
 *
 * Rendered as a radiogroup rather than the design's three `aria-pressed` buttons. That is a deliberate
 * divergence: aria-pressed says "toggle button", and three of them side by side announce as three
 * independent toggles that happen to look grouped. This is one choice among three — exactly what a radiogroup
 * is for — and it gets arrow-key navigation from the browser as a consequence rather than as extra code.
 *
 * The visual treatment is the design's, unchanged.
 */
export function ThemeToggle({ label = 'Theme' }: { label?: string }) {
  const { theme, setTheme } = useTheme()

  return (
    <div className="seg" role="radiogroup" aria-label={label}>
      {OPTIONS.map((option) => (
        <button
          key={option.value}
          type="button"
          role="radio"
          aria-checked={theme === option.value}
          // Roving tabindex: the group is one tab stop, and arrows move within it.
          tabIndex={theme === option.value ? 0 : -1}
          onClick={() => setTheme(option.value)}
          onKeyDown={(e) => {
            if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft' && e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return
            e.preventDefault()
            const step = e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : -1
            const i = OPTIONS.findIndex((o) => o.value === theme)
            const next = OPTIONS[(i + step + OPTIONS.length) % OPTIONS.length]!
            setTheme(next.value)
            const group = e.currentTarget.parentElement
            group?.querySelector<HTMLButtonElement>(`[data-theme-option="${next.value}"]`)?.focus()
          }}
          data-theme-option={option.value}
        >
          {option.label}
        </button>
      ))}
    </div>
  )
}
