import type { VehicleSummary } from '../../api/client'
import { BudgetBars } from '../../components/BudgetBars'
import { Kv } from '../../components/Kv'
import { Panel, Section, SectionHead, Wrap } from '../../components/layout'
import { AppLink } from '../../lib/link'
import { FuelPanel } from './FuelPanel'

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', minimumFractionDigits: 2 })

const money0 = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', maximumFractionDigits: 0 })

const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

/**
 * Spend and running cost, beside the fuel panel.
 *
 * The "This year, by category" bars now render the vehicle's **budget groups** (actual vs target), read straight
 * off `summary.budget` — the same calendar-year `BudgetSummary` the Budget page computes, so the two cannot
 * disagree. A group with no target set shows its spend and no bar; spend in no group folds into "Everything
 * else". Setting the numbers is the Budget screen's job (linked at the panel foot).
 */
/**
 * Cost per mile divides spend that runs to *today* by miles that run only to the last odometer reading, so an
 * odometer nobody has read for a while inflates it — the numerator keeps growing and the denominator does not.
 * Say so rather than letting the figure quietly drift. Only past this many days, because a day or two of lag is
 * every car every week and a caveat that always fires is one nobody reads.
 */
const STALE_ODOMETER_DAYS = 14

const daysBetween = (fromIso: string, toIso: string) =>
  Math.round(
    (new Date(`${toIso}T00:00:00`).getTime() - new Date(`${fromIso}T00:00:00`).getTime()) / 86_400_000,
  )

export function SpendPanel({ summary }: { summary: VehicleSummary }) {
  const { spend, identity, budget } = summary
  const reg = summary.registration

  const odometerDate = summary.mileage.asOfDate
  const staleOdometer =
    odometerDate !== null && daysBetween(odometerDate, summary.asOfDate) > STALE_ODOMETER_DAYS
      ? ` · odometer last read ${shortDate(odometerDate)}`
      : ''

  // ?? null: optional in the contract (it carries a default, so the addition stayed additive).
  const monthlyRunning = spend.monthlyAverageExcludingPurchase ?? null

  const costPerMileNote =
    spend.costPerMileExcludingPurchase === null
      ? 'needs mileage since purchase'
      : spend.costPerMile === null
        ? `running only${staleOdometer}`
        : `running only · ${money(spend.costPerMile)} with purchase${staleOdometer}`

  return (
    <Section>
      <Wrap>
        <SectionHead
          title="Spend & running cost"
          rule={<>since purchase · {shortDate(identity.purchaseDate)}</>}
        />
        <div className="twoup">
          <Panel className="pad">
            <div className="big num">{money(spend.totalSincePurchase)}</div>
            <div className="big-sub">
              {/* "Total outlay", not "total since purchase". The Kv below is *also* a since-purchase figure and
                  is the ex-purchase one, so two different numbers carried the same words forty pixels apart.
                  Outlay = everything including the car; running cost = everything except it. One vocabulary,
                  every screen. */}
              total outlay since purchase
              {/* The purchase itself is the single largest line and distorts every ratio built on it. The
                  design says "including the £1,700 car itself" as prose; this derives the figure. */}
              {spend.totalSincePurchase > spend.totalSincePurchaseExcludingPurchase && (
                <>
                  , including the{' '}
                  {money0(spend.totalSincePurchase - spend.totalSincePurchaseExcludingPurchase)} car itself
                </>
              )}
            </div>

            <div className="bars-head">This year, against budget</div>
            {budget.lines.length === 0 ? (
              <p className="panel-empty">No spend or budgets yet this year.</p>
            ) : (
              <BudgetBars lines={budget.lines} />
            )}

            <div className="split">
              <Kv
                label="Cost per mile"
                value={spend.costPerMileExcludingPurchase === null ? '—' : money(spend.costPerMileExcludingPurchase)}
                note={costPerMileNote}
              />
              <Kv
                label="Monthly average"
                // The ex-purchase figure, which is what this tile has always *said* it was. It showed
                // spend.monthlyAverage — the purchase-inclusive one — under the note "ex-purchase", because
                // until now no ex-purchase monthly average existed to show.
                value={monthlyRunning === null ? '—' : money0(monthlyRunning)}
                note={
                  monthlyRunning === null
                    ? 'not enough history'
                    : `over ${(identity.daysOwned / 30.44).toFixed(1)} months · ex-purchase`
                }
              />
              <Kv label="This year" value={money(spend.totalYtd)} note="all categories" />
              <Kv
                label="Running cost, since purchase"
                value={money(spend.totalSincePurchaseExcludingPurchase)}
                note="the outlay above, less the car"
              />
            </div>

            <div className="panel-foot">
              <AppLink className="sec-link" to="expenses" reg={reg}>
                Full expenses log →
              </AppLink>
            </div>
          </Panel>

          <FuelPanel summary={summary} />
        </div>
      </Wrap>
    </Section>
  )
}
