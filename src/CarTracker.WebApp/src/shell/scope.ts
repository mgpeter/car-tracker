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
 *
 * **`account` is a third kind rather than a reuse of `garage`** (2026-08-15). Every rendering treats the two
 * identically - no vehicle, so no vehicle links, no reminder badge, no bottom nav, no chat dock - and that is
 * exactly the argument for spelling it out: a page that is not the garage passing `kind: 'garage'` would be a
 * lie the type system had been asked to tell, in the one file whose whole purpose is making misrepresented
 * states unrepresentable. It also cost nothing to add: every branch that needed revisiting was a compile error.
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
  | {
      /** The account screen: signed-in, and deliberately about no car at all. */
      kind: 'account'
    }

/**
 * The bottom nav's centre slot.
 *
 * Nullable and per-screen, not shell-wide: it is the *page's* primary write action, so it differs on every
 * screen (`Add fuel`, `Mark weekly checks done`, `Edit budgets`). Two screens - vehicle-info and
 * data-integrity - have no *single* write action and substitute a plain link, which the design does with a
 * hardcoded `style="width:68px"` to hold the grid. That becomes the `link` variant. (It was three until the
 * settings screen was absorbed into vehicle-info; that screen was the variant's only production user, and
 * vehicle-info inherited the slot along with its editors.)
 */
export type CenterSlot =
  | { kind: 'action'; icon: IconName; label: string; onClick: () => void; badge?: CenterBadge }
  | { kind: 'link'; screen: ScreenId }

/**
 * A count riding on the centre action, toned by severity. Absent when there is nothing to say.
 *
 * **This replaced a `status` variant that rendered a warning triangle you could not tap.** The dashboard and
 * the checks screen used it: a `<span>` with `cursor: default`, no handler and no focus, sitting in the one
 * control a thumb reaches for. Worse on the dashboard, where the desktop quick-add band is hidden below 900px
 * on the explicit grounds that "the bottom bar's + is the mobile quick-add" - so the phone had no way to add
 * anything at all from the screen you land on.
 *
 * The alarm was worth keeping and the inert control was not, so the alarm became this. The centre is now
 * always something you can press, and the count says whether pressing something else matters more.
 */
export type CenterBadge = { count: number; tone: StatusTone }

/** The four status tones a centre-slot badge can carry - the app's existing semantic axis, no new colours. */
export type StatusTone = 'ok' | 'soon' | 'due' | 'info'
