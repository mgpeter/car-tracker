import { useState, type ReactNode } from 'react'
import { useChatAvailable, useMeta } from '../api/queries'
import { ChatDock } from '../chat/ChatDock'
import { Wrap } from '../components/layout'
import { BottomNav } from './BottomNav'
import type { CurrentScreen } from './nav'
import { NavMoreSheet } from './NavMoreSheet'
import type { CenterSlot, ShellScope } from './scope'
import { TopNav } from './TopNav'

/** The public feedback form. Owner-authored, unlike the model-generated links `Prose.tsx` sanitises. */
const FEEDBACK_URL = 'https://forms.cloud.microsoft/e/E25iu71Tb9'

/**
 * The page footer. Prose differs entirely per screen; the chrome does not.
 *
 * It names the build, which is the only place the app says which one it is. That question got sharper
 * with DEC-021: the dogfooding box follows `:edge` and Watchtower recreates it within five minutes of any
 * push, so "has this taken my change yet" is asked often and was previously answerable only by curling
 * `/api/meta` or reading the tag on the host. Beside it is the way out to the feedback form, which is the
 * app's only route for saying anything back - and it sits next to the build number on purpose, because
 * that is the one fact a bug report needs and the one the reporter cannot be expected to know.
 *
 * `LandingPage` imports this same component, so a signed-out visitor gets both - and on that page it
 * is the screen's only fetch. On a signed-in screen it costs nothing: AppShell below already calls useMeta()
 * for `chatConfigured`, and this is the same cache entry.
 */
export function Footer({ children }: { children: ReactNode }) {
  const version = useMeta().data?.version

  return (
    <footer>
      <Wrap>
        {/* The meta line is a SIBLING paragraph, never appended to this one: the prose is matched by exact
            text in shell.test.tsx and in screen tests, and merging them breaks those for a reason that
            reads as unrelated to a footer line. */}
        <p style={{ margin: 0 }}>{children}</p>
        {/* The link is always here; the build joins it once /api/meta answers - absent renders nothing
            rather than a placeholder or `v undefined`, the rule chatConfigured and vehicleLookupConfigured
            already follow. The version keeps its OWN element with the separator outside it, because two
            tests match `cambelt.app v…` by exact text and widening that node's text breaks them - and the
            hero eyebrow is found by an exact getByText('cambelt.app'), which a node carrying just the name
            would make ambiguous. */}
        <p className="fver">
          {version !== undefined && (
            <>
              <span>cambelt.app v{version}</span>
              {' · '}
            </>
          )}
          {/* target="_blank" here, unlike the landing page's two links: this one leaves a SPA that may have
              a half-filled sheet open, and Back would re-mount and refetch the screen. rel per Prose.tsx. */}
          <a href={FEEDBACK_URL} target="_blank" rel="noopener noreferrer">
            Send feedback
          </a>
        </p>
      </Wrap>
    </footer>
  )
}

interface AppShellProps {
  scope: ShellScope
  current: CurrentScreen
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
  const [chatOpen, setChatOpen] = useState(false)

  // Two facts, one hook: this deployment holds a model credential, and this account's plan includes the
  // assistant. See useChatAvailable for why both are tested `=== true`.
  const chat = useChatAvailable()

  return (
    <>
      <TopNav
        scope={scope}
        current={current}
        {...(chat && current !== 'assistant' && { onOpenChat: () => setChatOpen(true) })}
      />

      <main>{children}</main>

      {footer !== undefined && <Footer>{footer}</Footer>}

      <BottomNav scope={scope} current={current} center={center} onOpenMore={() => setMoreOpen(true)} />

      <NavMoreSheet open={moreOpen} onClose={() => setMoreOpen(false)} scope={scope} current={current} />

      {/* The dock is mounted only when opened, so a conversation is not held alive behind every screen — and
          only above 900px, because the button that opens it lives in the top bar, which is hidden below that.
          Below it the assistant is a route instead. */}
      {chatOpen && (
        <ChatDock
          reg={scope.kind === 'vehicle' ? scope.reg : null}
          onClose={() => setChatOpen(false)}
        />
      )}
    </>
  )
}
