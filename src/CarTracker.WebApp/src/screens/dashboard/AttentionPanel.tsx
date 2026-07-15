import type { VehicleSummary } from '../../api/client'
import { Panel, Section, SectionHead, Wrap } from '../../components/layout'
import { AppLink } from '../../lib/link'
import { countdownText, type RenewalUrgency } from '../../lib/renewal'
import type { ScreenId } from '../../shell/nav'

interface Alert {
  key: string
  /** The uppercase mono kicker. Carries the state in text, before any colour does. */
  kicker: string
  headline: string
  body: string
  to: ScreenId
  cta: string
}

const plural = (n: number, one: string, many: string) => (n === 1 ? one : many)

/**
 * Needs attention — the one panel that decides what the page is *for* on any given day.
 *
 * The design's version is a single hardcoded story: the two K-series head-gasket checks, 19 days overdue, with
 * "Mark both done" wired to a `setState` that moves a counter and fires a toast describing a write that never
 * happens. The story is a good one and it is BT53's, not the product's — a Freelander's oil-filler-cap check is
 * a `CheckDefinition` row, and the panel cannot know it is special.
 *
 * So this derives its alerts, in severity order, and each one is a real fact with somewhere to go. An expired
 * renewal outranks an overdue check: one is a car that is illegal to drive, the other is a look under the
 * bonnet. When there is nothing wrong it says so — the design's `is-ok` state, which it only reaches by
 * clicking its own fake button.
 */
export function AttentionPanel({ summary }: { summary: VehicleSummary }) {
  const { checks, renewals, mileage } = summary
  const reg = summary.registration
  const alerts: Alert[] = []

  const named = [
    { r: renewals.mot, to: 'service' as const },
    { r: renewals.insurance, to: 'settings' as const },
    { r: renewals.roadTax, to: 'settings' as const },
  ]

  // 1. Expired. The negative days-remaining the domain deliberately keeps, put to work.
  for (const { r, to } of named) {
    if (r.daysRemaining !== null && r.daysRemaining < 0) {
      alerts.push({
        key: `expired-${r.name}`,
        kicker: `${r.name} · expired`,
        headline: `${r.name} expired ${countdownText(r.daysRemaining)}`,
        body: `Ran out on ${new Date(`${r.expiryDate}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' })}. This is not a reminder — it is a car that should not be on the road until it is sorted.`,
        to,
        cta: `Open ${r.name.toLowerCase()}`,
      })
    }
  }

  // 2. Due inside 30 days. Urgent, and emphatically not the same as expired.
  for (const { r, to } of named) {
    if (r.daysRemaining !== null && r.daysRemaining >= 0 && (r.urgency as RenewalUrgency | null) === 'Red') {
      alerts.push({
        key: `due-${r.name}`,
        kicker: `${r.name} · due`,
        headline: `${r.name} expires in ${countdownText(r.daysRemaining)}`,
        body: `Under the 30-day threshold. ${r.source ?? 'No source recorded'}.`,
        to,
        cta: `Open ${r.name.toLowerCase()}`,
      })
    }
  }

  // 3. The mileage flag. A reading above the latest one means a record disagrees with the odometer — spec §5.3
  // says flag it, never silently accept it. This is the 83,000 mi row, surfaced.
  if (mileage.hasNonMonotonicHistory) {
    alerts.push({
      key: 'mileage',
      kicker: 'Mileage · not monotonic',
      headline: 'A logged reading is above the current odometer',
      body:
        mileage.highestRecordedMileage !== null && mileage.currentMileage !== null
          ? `The highest reading on record is ${mileage.highestRecordedMileage.toLocaleString('en-GB')} mi, against a latest of ${mileage.currentMileage.toLocaleString('en-GB')} mi. A mileage cannot go down, so one of the two is a typo. Flagged, not corrected, and not silently accepted.`
          : 'A reading is above the latest one. A mileage cannot go down, so one of them is a typo.',
      to: 'mileage',
      cta: 'Open mileage log',
    })
  }

  // 4. Overdue checks, as one alert rather than seven.
  if (checks.overdueCount > 0) {
    alerts.push({
      key: 'checks',
      kicker: 'Regular checks · lapsed',
      headline: `${checks.overdueCount} ${plural(checks.overdueCount, 'check is', 'checks are')} past ${plural(checks.overdueCount, 'its', 'their')} interval`,
      body: `Each is past the cadence set for it. Status is computed from the last log and the interval, so logging one moves it out of this count immediately.`,
      to: 'checks',
      cta: 'Open regular checks',
    })
  }

  const rule =
    alerts.length === 0
      ? 'nothing outstanding'
      : `${alerts.length} ${plural(alerts.length, 'alert', 'alerts')} · ${checks.overdueCount + checks.dueSoonCount} checks outstanding`

  return (
    <Section>
      <Wrap>
        <SectionHead
          title="Needs attention"
          rule={<>{rule}</>}
          link={
            <AppLink className="sec-link" to="checks" reg={reg}>
              Regular checks →
            </AppLink>
          }
        />

        {alerts.length === 0 ? (
          <Panel className="attn is-ok">
            <div>
              <div className="attn-k">Nothing overdue</div>
              <h3>Nothing is overdue, expired, or flagged</h3>
              <p>
                No renewal is inside 30 days, no check is past its interval, and no reading contradicts the
                odometer. Computed at render — if that changes, this panel says so without anyone updating it.
                {/* Never-logged is not overdue, so it raises no alert — but it is not clear either, and the
                    domain is emphatic that it is "not an error, not a default, and emphatically not Ok". The
                    kicker says "Nothing overdue" rather than "All clear" for the same reason: this panel
                    should claim exactly what it checked. Reading a never-logged check as fine is how the
                    workbook came to count 17 definitions out of 18. */}
                {checks.neverLoggedCount > 0 && (
                  <>
                    {' '}
                    {checks.neverLoggedCount}{' '}
                    {plural(checks.neverLoggedCount, 'check has', 'checks have')} never been logged, so{' '}
                    {plural(checks.neverLoggedCount, 'it has', 'they have')} no interval to be past — counted,
                    not assumed done.
                  </>
                )}
              </p>
            </div>
            {checks.neverLoggedCount > 0 && (
              <div className="attn-act">
                <AppLink className="btn ghost" to="checks" reg={reg}>
                  Open regular checks
                </AppLink>
              </div>
            )}
          </Panel>
        ) : (
          alerts.map((a) => (
            <Panel key={a.key} className="attn">
              <div>
                <div className="attn-k">{a.kicker}</div>
                <h3>{a.headline}</h3>
                <p>{a.body}</p>
              </div>
              <div className="attn-act">
                <AppLink className="btn" to={a.to} reg={reg}>
                  {a.cta}
                </AppLink>
              </div>
            </Panel>
          ))
        )}
      </Wrap>
    </Section>
  )
}
