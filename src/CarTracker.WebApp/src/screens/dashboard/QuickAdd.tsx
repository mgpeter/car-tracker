import { Btn } from '../../components/Btn'
import { Wrap } from '../../components/layout'
import { AppLink } from '../../lib/link'

/**
 * The quick-add band.
 *
 * README calls quick-add a core requirement, and the design's version is five hardcoded buttons above a
 * Settings list with **drag grips that do not drag**. The grips are cut rather than ported: an affordance that
 * does nothing is worse than its absence, and reordering is a real feature for M2, when Settings has somewhere
 * to store the order. What is left is the part that was always the point — one row, every button wired to a
 * sheet that actually writes.
 *
 * It arrives now rather than with the dashboard because until M1f only one of its buttons had a sheet behind
 * it. A band with one live button and four dead links is worse than no band.
 *
 * Fuel opens the sheet in place; the rest navigate, because their sheets live on their own screens and
 * mounting four sheets on the dashboard to save one click is how a dashboard becomes an app.
 */
export function QuickAdd({ reg, onAddFuel }: { reg: string; onAddFuel: () => void }) {
  return (
    <div className="qa">
      <Wrap className="qa-in">
        <span className="qa-label">Quick add</span>
        <Btn onClick={onAddFuel}>+ Fuel</Btn>
        <AppLink className="btn ghost" to="expenses" reg={reg}>
          Expense
        </AppLink>
        <AppLink className="btn ghost" to="mileage" reg={reg}>
          Mileage
        </AppLink>
        <AppLink className="btn ghost" to="checks" reg={reg}>
          Log a check
        </AppLink>
      </Wrap>
    </div>
  )
}
