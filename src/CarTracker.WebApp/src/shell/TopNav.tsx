import { AppLink } from '../lib/link'
import { UserMenu } from '../auth/UserMenu'
import { Icon } from '../components/Icon'
import { ReminderBadge } from '../components/ReminderBadge'
import { useTheme } from '../theme/ThemeProvider'
import type { Theme } from '../lib/theme'
import { GROUP_LABELS, groupedScreens, SCREENS, TOP_LEVEL, type CurrentScreen } from './nav'
import type { ShellScope } from './scope'

const NEXT: Record<Theme, Theme> = { system: 'light', light: 'dark', dark: 'system' }
const THEME_LABEL: Record<Theme, string> = { system: 'System', light: 'Light', dark: 'Dark' }

/**
 * The top nav's theme control: a cycle button showing the current state.
 *
 * This is the design's SECOND theme control — distinct from the three-way `<ThemeToggle>` in Settings, which
 * is a radiogroup. Both exist in the design and they are not duplicates: one is a quick toggle, the other a
 * setting.
 *
 * The design's visible label is "Theme · System", which reads ambiguously to a screen reader — is "System"
 * the current state, or what pressing does? The visible text is kept and an explicit accessible name added,
 * naming both the state and the action.
 */
function ThemeCycleButton() {
  const { theme, setTheme } = useTheme()
  return (
    <button
      className="theme-btn"
      type="button"
      onClick={() => setTheme(NEXT[theme])}
      aria-label={`Theme: ${THEME_LABEL[theme]}. Change to ${THEME_LABEL[NEXT[theme]]}`}
    >
      {/* The prefix is split out so the phone bar can drop it and keep the state word, which is the part
          that changes. The accessible name above is unaffected either way — it names both the state and
          the action, and does so identically at every width. */}
      <span className="tb-word">Theme · </span>
      <span>{THEME_LABEL[theme]}</span>
    </button>
  )
}

/**
 * The desktop top bar. Hidden below 900px, where `<BottomNav>` takes over — the two are exact complements.
 *
 * Extracted once, from 17 copy-pasted instances.
 */
export function TopNav({ scope, current, onOpenChat }: { scope: ShellScope; current: CurrentScreen; onOpenChat?: () => void }) {
  return (
    <nav className="topnav" aria-label="Primary">
      <div className="wrap topnav-in">
        <AppLink to="garage" className="brand" current={current === 'garage'}>
          Cambelt
        </AppLink>

        {scope.kind === 'vehicle' ? (
          <div className="tn-links">
            {TOP_LEVEL.map((id) => (
              <AppLink key={id} to={id} reg={scope.reg} current={current === id}>
                {SCREENS[id].nav ?? SCREENS[id].label}
              </AppLink>
            ))}
            <MorePanel reg={scope.reg} current={current} />
          </div>
        ) : (
          // No vehicle to scope links to, so a different bar - not a bug, a consequence. Two scopes land
          // here: the garage, which is where you are *before* picking a car (and which the design gives a
          // shortcut back to the last one), and the account screen, which is about no car at all. Both offer
          // the way back and nothing that needs a registration.
          <div className="tn-links">
            <AppLink to="garage" current={current === 'garage'}>
              Garage
            </AppLink>
            {scope.kind === 'garage' && scope.shortcut !== undefined && (
              <AppLink to="dashboard" reg={scope.shortcut.reg}>
                {scope.shortcut.reg} · Dashboard
              </AppLink>
            )}
          </div>
        )}

        {onOpenChat !== undefined && (
          // The assistant's one entry point on a desktop. It opens the dock rather than navigating, because
          // the panel's whole point is being beside the screen you are asking about.
          <button className="chat-btn" type="button" onClick={onOpenChat} aria-label="Open the assistant">
            <Icon name="chat" />
            <span className="tb-word">Assistant</span>
          </button>
        )}

        {scope.kind === 'vehicle' && <ReminderBadge reg={scope.reg} />}
        <ThemeCycleButton />
        <UserMenu />
      </div>
    </nav>
  )
}

/**
 * The More dropdown.
 *
 * A native `<details>`/`<summary>`, as the design has it — CSS-only, keyboard-accessible for free, and no
 * open/close state to own. Its one gap is that it does not close on outside click; that is left as-is rather
 * than invented, because fixing it means adding a global listener whose behaviour the design never specified.
 */
function MorePanel({ reg, current }: { reg: string; current: CurrentScreen }) {
  // Only what the top bar does not already show. The mobile sheet passes false here, because it is the only
  // menu on that viewport and must list everything.
  const groups = groupedScreens({ excludeTopLevel: true })

  return (
    <details className="more">
      <summary>
        More <Icon name="caret-down" />
      </summary>
      <div className="more-panel">
        {groups.map(({ group, ids }) => (
          <div key={group}>
            <div className="mp-group">{GROUP_LABELS[group]}</div>
            {ids.map((id) => (
              <AppLink
                key={id}
                to={id}
                {...(SCREENS[id].scoped && { reg })}
                current={current === id}
              >
                {SCREENS[id].label}
              </AppLink>
            ))}
          </div>
        ))}
      </div>
    </details>
  )
}
