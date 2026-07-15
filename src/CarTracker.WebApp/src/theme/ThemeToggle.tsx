import { Seg } from '../components/Seg'
import type { Theme } from '../lib/theme'
import { useTheme } from './ThemeProvider'

const OPTIONS: ReadonlyArray<{ value: Theme; label: string }> = [
  { value: 'light', label: 'Light' },
  { value: 'dark', label: 'Dark' },
  { value: 'system', label: 'System' },
]

/**
 * The three-way theme control, as it appears in Settings.
 *
 * Now a thin binding over the generic `<Seg>` — the radiogroup semantics and roving tabindex it used to own
 * were never theme-specific. The design has a *second*, different theme control in the top nav (a cycle
 * button showing the current state); that one lands with the shell in stage 5.
 */
export function ThemeToggle({ label = 'Theme' }: { label?: string }) {
  const { theme, setTheme } = useTheme()
  return <Seg label={label} options={OPTIONS} value={theme} onChange={setTheme} />
}
