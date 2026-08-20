import { Icon } from '../components/Icon'
import { AppLink } from '../lib/link'
import { SCREENS, type CurrentScreen, type ScreenId } from './nav'
import type { CenterSlot, ShellScope } from './scope'

/** The four fixed outer slots. Identical on every screen in the design — Home, Fuel, [centre], Checks, More. */
const OUTER: readonly [ScreenId, ScreenId, ScreenId] = ['dashboard', 'fuel', 'checks']

interface BottomNavProps {
  scope: ShellScope
  current: CurrentScreen
  center: CenterSlot | null
  onOpenMore: () => void
}

/**
 * The mobile bottom bar. Appears at 900px, exactly where `<TopNav>`'s links vanish — the two are complements,
 * so there is never a viewport with both or neither.
 *
 * Not rendered at all without a vehicle: three of its five slots would have nowhere to point. The design
 * reaches the same conclusion by omitting `.bnav` from `garage.dc.html`; here it follows from the type, and
 * from the type it also covers the account screen for free.
 */
export function BottomNav({ scope, current, center, onOpenMore }: BottomNavProps) {
  if (scope.kind !== 'vehicle') return null
  const { reg } = scope

  return (
    <nav className="bnav" aria-label="Primary mobile">
      <div className="bnav-in">
        {OUTER.slice(0, 2).map((id) => (
          <AppLink key={id} to={id} reg={reg} current={current === id}>
            {SCREENS[id].bottom ?? SCREENS[id].label}
          </AppLink>
        ))}

        <CenterSlotView slot={center} reg={reg} current={current} />

        <AppLink to="checks" reg={reg} current={current === 'checks'}>
          {SCREENS.checks.bottom}
        </AppLink>

        <button className="bmore" type="button" onClick={onOpenMore}>
          More
        </button>
      </div>
    </nav>
  )
}

function CenterSlotView({ slot, reg, current }: { slot: CenterSlot | null; reg: string; current: CurrentScreen }) {
  if (slot === null) {
    // Keeps the five-column grid honest when a screen has neither an action nor a link.
    return <span className="bplus" aria-hidden="true" />
  }

  if (slot.kind === 'link') {
    // The design hardcodes style="width:68px" here to match .bplus and hold the grid. That is what .bnav-link
    // is for — the width belongs in the stylesheet, and an inline style attribute would be blocked by our CSP
    // anyway if it ever came from markup rather than React.
    return (
      <AppLink to={slot.screen} reg={reg} className="bnav-link" current={current === slot.screen}>
        {SCREENS[slot.screen].bottom ?? SCREENS[slot.screen].label}
      </AppLink>
    )
  }

  const badge = slot.badge !== undefined && slot.badge.count > 0 ? slot.badge : null

  return (
    <button
      className="bplus"
      type="button"
      // The count folded into the name rather than left as a loose number beside it, so a screen reader gets
      // one control saying one thing. Same choice VehicleCard makes for its stretched link.
      aria-label={badge === null ? slot.label : `${slot.label}, ${badge.count} needing attention`}
      onClick={slot.onClick}
    >
      {/* The <i> is the design's 44x44 circle — a styling hook, not an icon. The glyph inside it was ＋,
          absent from every font we ship; it is an SVG now (DEC-013). */}
      <i>
        <Icon name={slot.icon} />
      </i>
      {badge !== null && (
        // aria-hidden because the count is already in the button's name above. A visible number that is also
        // announced separately reads as two things.
        <span className={`bplus-badge tone-${badge.tone}`} aria-hidden="true">
          {badge.count}
        </span>
      )}
    </button>
  )
}
