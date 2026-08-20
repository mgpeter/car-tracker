import { Btn } from '../../components/Btn'
import { Wrap } from '../../components/layout'
import { AppLink } from '../../lib/link'
import type { ScreenId } from '../../shell/nav'

/**
 * One quick-add destination: a screen, and the label the band and the mobile sheet both render.
 *
 * Exported because the bottom bar's + opens the same list in a sheet (`QuickAddSheet`), and two hand-written
 * copies of "the things you can add" is how one of them comes to be missing wash for six months.
 */
export interface QuickAddAction {
  screen: ScreenId
  label: string
}

/**
 * Everything you can log, in the order you log it.
 *
 * Fuel is first and is the only one with a solid button, because it is the thing you do weekly; the rest are
 * ordered by how often they come up rather than alphabetically or by screen order. Fuel is absent from this
 * list because it is not a destination - it opens in place, see below.
 */
export const QUICK_ADD_ACTIONS: readonly QuickAddAction[] = [
  { screen: 'service', label: 'Service' },
  { screen: 'wash', label: 'Wash' },
  { screen: 'equipment', label: 'Equipment' },
  { screen: 'expenses', label: 'Expense' },
  { screen: 'mileage', label: 'Mileage' },
  { screen: 'checks', label: 'Log a check' },
]

/** The search param the band adds, so the target screen opens its sheet on arrival. */
export const ADD_QUERY = { add: '1' } as const

/**
 * The quick-add band.
 *
 * README calls quick-add a core requirement, and the design's version is five hardcoded buttons above a
 * Settings list with **drag grips that do not drag**. The grips are cut rather than ported: an affordance that
 * does nothing is worse than its absence, and reordering is a real feature for M2, when Settings has somewhere
 * to store the order. What is left is the part that was always the point - one row, every button wired to a
 * sheet that actually writes.
 *
 * **Fuel opens in place; the rest navigate carrying `?add=1`, and the target screen opens its own sheet on
 * arrival.** The original band navigated and left you to press the screen's own + when you got there, which
 * made "quick add" two clicks for six of the seven things you can add. Mounting all six sheets here instead
 * would be the other extreme, and is what this file has always argued against: the sheets live where their
 * data does, and three of them need rows the dashboard summary does not carry (an expense's vendor
 * suggestions, an equipment item's sources, the checks screen's whole definition list). The param costs
 * nothing and moves the sheet, not the data. `lib/useAddOnArrival.ts` is the receiving half.
 */
export function QuickAdd({ reg, onAddFuel }: { reg: string; onAddFuel: () => void }) {
  return (
    <div className="qa">
      <Wrap className="qa-in">
        <span className="qa-label">Quick add</span>
        <Btn onClick={onAddFuel}>+ Fuel</Btn>
        {QUICK_ADD_ACTIONS.map((action) => (
          <AppLink
            key={action.screen}
            className="btn ghost"
            to={action.screen}
            reg={reg}
            query={ADD_QUERY}
          >
            {action.label}
          </AppLink>
        ))}
      </Wrap>
    </div>
  )
}
