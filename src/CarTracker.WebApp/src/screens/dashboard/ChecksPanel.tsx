import type { VehicleSummary } from '../../api/client'
import { Cadence } from '../../components/Cadence'
import { StatTile, StatTiles } from '../../components/StatTile'
import { CFoot, Panel, Section, SectionHead, Wrap } from '../../components/layout'
import { AppLink } from '../../lib/link'
import type { DueStatus } from '../../lib/status'

type CheckState = VehicleSummary['checks']['checks'][number]

/** "19 days over" / "in 4 days" / "never logged" — the design's phrasing, derived. */
function dueText(check: CheckState): string {
  if (check.status === 'NeverLogged') return 'never logged'
  if (check.daysRemaining === null) return '—'
  if (check.daysRemaining < 0) return `${Math.abs(check.daysRemaining)} days over`
  if (check.daysRemaining === 0) return 'due today'
  return `in ${check.daysRemaining} days`
}

/**
 * Regular checks: the four buckets, and the most pressing few.
 *
 * The tiles are `<StatTile>`, whose labels derive from the domain's `CheckStatus` — so the fourth bucket is
 * "Never logged" on the *due* axis and neutral, not the design's blue `.tile.info`. Blue is the integrity
 * axis, and whether a check has ever been done is not a question about data quality.
 *
 * The list is ordered by urgency rather than the design's hardcoded five rows: overdue first and most overdue
 * at the top, then due soon, then never-logged. A dashboard is scanned, and the top of this list is the only
 * part most people will read.
 */
export function ChecksPanel({ summary }: { summary: VehicleSummary }) {
  const { checks } = summary
  const reg = summary.registration

  const rank: Record<DueStatus, number> = { Overdue: 0, DueSoon: 1, NeverLogged: 2, Ok: 3 }
  const pressing = [...checks.checks]
    .filter((c) => c.status !== 'Ok')
    .sort((a, b) => {
      const byStatus = rank[a.status as DueStatus] - rank[b.status as DueStatus]
      if (byStatus !== 0) return byStatus
      return (a.daysRemaining ?? 0) - (b.daysRemaining ?? 0)
    })
    .slice(0, 5)

  return (
    <Section last>
      <Wrap>
        <SectionHead
          title="Regular checks"
          rule={
            checks.totalCount === 0 ? (
              <>none defined yet</>
            ) : (
              <>
                {checks.totalCount} defined · status from last log + interval
              </>
            )
          }
          link={
            <AppLink className="sec-link" to="checks" reg={reg}>
              {checks.totalCount === 0 ? 'Define checks →' : `All ${checks.totalCount} checks →`}
            </AppLink>
          }
        />
        <Panel>
          {checks.totalCount === 0 ? (
            // A car with no definitions. The design cannot render it — it has eighteen frozen in — and it is
            // where BT53 itself was until the settings screen shipped. Four zero tiles would say "all clear".
            <p className="panel-empty">
              No checks defined for this vehicle, so there is nothing to be due.{' '}
              <AppLink to="settings" reg={reg}>
                Define them in settings
              </AppLink> and status starts deriving from the
              first log.
            </p>
          ) : (
            <>
              <StatTiles>
                <StatTile due="Overdue" count={checks.overdueCount} />
                <StatTile due="DueSoon" count={checks.dueSoonCount} />
                <StatTile due="Ok" count={checks.okCount} />
                <StatTile due="NeverLogged" count={checks.neverLoggedCount} />
              </StatTiles>

              {pressing.length > 0 && (
                <ul className="clist">
                  {pressing.map((c) => (
                    <li key={c.checkDefinitionId} className={c.status === 'DueSoon' ? 'is-soon' : undefined}>
                      <Cadence>{c.cadenceLabel}</Cadence>
                      <span className="cname">{c.name}</span>
                      <span className="cdays num">{dueText(c)}</span>
                    </li>
                  ))}
                </ul>
              )}

              <CFoot>
                {/* The tiles must sum to the definitions. The workbook's dashboard counts 17 of 18 because
                    "Spare tyre pressure" has never been logged and falls out of its three buckets — this
                    states the sum so a recurrence is visible rather than silent. */}
                <span>
                  {checks.okCount} + {checks.dueSoonCount} + {checks.overdueCount} +{' '}
                  {checks.neverLoggedCount} = <b>{checks.totalCount}</b> · never-logged is a state, not a gap
                </span>
                {pressing.length > 5 && <span>showing the 5 most pressing</span>}
              </CFoot>
            </>
          )}
        </Panel>
      </Wrap>
    </Section>
  )
}
