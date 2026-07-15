import { useState, type ReactNode } from 'react'
import { Wrap } from '../components/layout'
import { BottomNav } from './BottomNav'
import type { ScreenId } from './nav'
import { NavMoreSheet } from './NavMoreSheet'
import type { CenterSlot, ShellScope } from './scope'
import { TopNav } from './TopNav'

/** The page footer. Prose differs entirely per screen; the chrome does not. */
export function Footer({ children }: { children: ReactNode }) {
  return (
    <footer>
      <Wrap>
        <p style={{ margin: 0 }}>{children}</p>
      </Wrap>
    </footer>
  )
}

interface AppShellProps {
  scope: ShellScope
  current: ScreenId
  /** The page's primary write action, in the bottom bar's centre slot. Null where a screen has none. */
  center?: CenterSlot | null
  footer?: ReactNode
  children: ReactNode
}

/**
 * The shared shell — extracted **once**, from 17 copy-pasted instances.
 *
 * This is the single biggest win in task 4. The design pastes the nav, footer, toast and the theme block into
 * every screen, which in a SPA means 17 competing writers to one global (`document.documentElement`) and 17
 * timers for one toast. Every difference between those copies turns out to be a prop: `current`, the centre
 * slot, and the footer's prose.
 *
 * The More sheet's open state lives here rather than in each screen. In the design it is `state.sheet ===
 * 'more'`, duplicated 17 times alongside each screen's *own* sheets — mixing a shell concern into page state.
 * A screen's own sheets stay its business; this one is the shell's.
 */
export function AppShell({ scope, current, center = null, footer, children }: AppShellProps) {
  const [moreOpen, setMoreOpen] = useState(false)

  return (
    <>
      <TopNav scope={scope} current={current} />

      <main>{children}</main>

      {footer !== undefined && <Footer>{footer}</Footer>}

      <BottomNav scope={scope} current={current} center={center} onOpenMore={() => setMoreOpen(true)} />

      <NavMoreSheet open={moreOpen} onClose={() => setMoreOpen(false)} scope={scope} current={current} />
    </>
  )
}
