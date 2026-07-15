import type { VehicleSummary } from '../../api/client'
import { Bars, type BarDatum } from '../../components/Bars'
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
 * The design's fourth bar is "Parking, tools, wash, parts" — a hardcoded lump for everything outside the first
 * three. It is derived here as the remainder, so a category nobody thought of still lands somewhere and the
 * bars always sum to the total. Naming it after four categories would go stale the moment a fifth is used.
 *
 * Budget is absent from this panel, unlike the design, and deliberately: its two budget figures need
 * `GetBudgetSummaryAsync`, which M1a made reachable but which this screen does not call. Rendering "Budget
 * used 43.2%" from nothing is exactly the class of thing this project exists to stop. It lands with the budget
 * screen in M2.
 */
export function SpendPanel({ summary }: { summary: VehicleSummary }) {
  const { spend, identity } = summary
  const reg = summary.registration

  const known = spend.fuelYtd + spend.serviceAndRepairsYtd + spend.statutoryYtd
  const other = Math.max(0, spend.totalYtd - known)

  const bars: BarDatum[] = [
    { name: 'Fuel', value: spend.fuelYtd, tone: 'g1' },
    { name: 'Service & repairs', value: spend.serviceAndRepairsYtd, tone: 'g2' },
    { name: 'Insurance, tax & MOT', value: spend.statutoryYtd, tone: 'g3' },
    // No tone: the design leaves the remainder bar untinted, and it is a remainder rather than a category.
    { name: 'Everything else', value: other },
  ]

  return (
    <Section>
      <Wrap>
        <SectionHead
          title="Spend & running cost"
          rule={<>since purchase · {shortDate(identity.purchaseDate)}</>}
          link={
            <AppLink className="sec-link" to="expenses" reg={reg}>
              Expenses →
            </AppLink>
          }
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

            <div className="bars-head">This year, by category</div>
            <Bars data={bars} total={spend.totalYtd} />

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
          </Panel>

          <FuelPanel summary={summary} />
        </div>
      </Wrap>
    </Section>
  )
}
