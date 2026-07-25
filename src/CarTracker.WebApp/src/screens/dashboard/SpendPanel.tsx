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
export function SpendPanel({ summary }: { summary: VehicleSummary }) {
  const { spend, identity, budget } = summary
  const reg = summary.registration

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
              total since purchase
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
                note={
                  spend.costPerMileExcludingPurchase === null
                    ? 'needs mileage since purchase'
                    : spend.costPerMile === null
                      ? 'running only'
                      : `running only · ${money(spend.costPerMile)} with purchase`
                }
              />
              <Kv
                label="Monthly average"
                value={spend.monthlyAverage === null ? '—' : money0(spend.monthlyAverage)}
                note={
                  spend.monthlyAverage === null
                    ? 'not enough history'
                    : `over ${(identity.daysOwned / 30.44).toFixed(1)} months · ex-purchase`
                }
              />
              <Kv label="This year" value={money(spend.totalYtd)} note="all categories" />
              <Kv
                label="Since purchase"
                value={money(spend.totalSincePurchaseExcludingPurchase)}
                note="running costs only"
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
