import type { components } from '../api/generated/schema'

type BudgetGroupLine = components['schemas']['BudgetGroupLine']

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', minimumFractionDigits: 2 })

/**
 * The budget group bars — shared by the Budget page and the dashboard's spend card, so the two render variance
 * identically and cannot drift.
 *
 * Two rules carried from the original Budget page: a **null target** (a tracked group, or the uncategorised
 * "Everything else" line) shows its spend and no bar — it is not a group budgeted at zero; and an **over-budget**
 * bar caps its width at 100% while the real percentage stays in the text, because a bar drawn to 158% would spill
 * its track and a bar clamped to 100% with no figure would read "at the limit" when the truth is half again more.
 */
export function BudgetBars({ lines }: { lines: BudgetGroupLine[] }) {
  return (
    <ul className="bars-list">
      {lines.map((l) => {
        const pct = l.percentUsed ?? 0
        // Capped for the geometry only. The figure beside it is never the capped one.
        const width = Math.min(pct, 100)
        return (
          <li key={l.name}>
            <span className="bl-name">{l.name}</span>
            <span className="bl-val num">
              {money(l.actualSpend)}
              {l.annualBudget === null ? (
                // Not a bar at 100%, and not £0 budgeted. The absence is the fact.
                <em className="faint"> · {l.isUncategorised ? 'no group' : 'no target'}</em>
              ) : (
                <em className={l.isOverBudget ? 'over' : undefined}>
                  {' '}
                  {pct.toFixed(0)}% of {money(l.annualBudget)}
                  {l.isOverBudget && l.remaining !== null && ` · ${money(-l.remaining)} over`}
                </em>
              )}
            </span>
            {l.annualBudget !== null && (
              <span className="track">
                <i className={l.isOverBudget ? 'over' : undefined} style={{ width: `${width}%` }} />
              </span>
            )}
          </li>
        )
      })}
    </ul>
  )
}
