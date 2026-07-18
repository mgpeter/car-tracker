import type { IconName } from '../components/Icon'
import type { ScreenId } from './nav'

/**
 * What the shell is scoped to.
 *
 * A discriminated union rather than an optional `reg`, because the garage's differences are **structural, not
 * incidental**: it is the screen you are on before choosing a vehicle, so it *cannot* render vehicle-scoped
 * links, a vehicle dashboard's bottom nav, or a page head with a plate. The design shows this as an outlier —
 * `garage.dc.html` alone has no More dropdown, no `.bnav` and no `.phead` — which reads as inconsistency
 * until you notice there is no vehicle to point any of it at.
 *
 * As a union, "garage with a reg" and "vehicle screen without one" are both unrepresentable.
 */
export type ShellScope =
  | {
      kind: 'garage'
      /** The design's second top-nav link: a shortcut back to a vehicle's dashboard. */
      shortcut?: { reg: string }
    }
  | {
      kind: 'vehicle'
      reg: string
    }

/**
 * The bottom nav's centre slot.
 *
 * Nullable and per-screen, not shell-wide: it is the *page's* primary write action, so it differs on every
 * screen (`Add fuel`, `Mark weekly checks done`, `Edit budgets`). Three screens — settings, vehicle-info and
 * data-integrity — have no write action at all and substitute a plain link, which the design does with a
 * hardcoded `style="width:68px"` to hold the grid. That becomes the `link` variant.
 */
export type CenterSlot =
  | { kind: 'action'; icon: IconName; label: string; onClick: () => void }
  | { kind: 'link'; screen: ScreenId }
  // A screen with no single write action fills the slot with its own status instead of an empty circle: the
  // dashboard shows the vehicle's worst state, checks shows the check state. A tell-tale, not a button.
  | { kind: 'status'; tone: StatusTone; label: string }

/** The four status tones a centre-slot glyph can carry — the app's existing semantic axis, no new colours. */
export type StatusTone = 'ok' | 'soon' | 'due' | 'info'
