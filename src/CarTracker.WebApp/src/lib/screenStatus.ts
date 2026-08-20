import type { VehicleSummary } from '../api/client'
import type { CenterBadge, StatusTone } from '../shell/scope'

/**
 * A screen's state: the tone drives the colour, the label is its accessible name, the count is how many
 * things are behind it.
 *
 * `count` is carried here rather than worked out again by the caller so that the badge and the tone cannot
 * disagree about what they are describing. Each function below sets both from the same branch.
 */
export interface ScreenStatus {
  tone: StatusTone
  label: string
  count: number
}

/**
 * A status as a centre-slot badge, or nothing when there is nothing to say.
 *
 * Green is nothing to say: a badge reading "0" on a car with nothing wrong is noise, and a green badge on a
 * car with nothing wrong is a decoration that trains people to ignore the position it sits in.
 */
export function badgeOf(status: ScreenStatus): CenterBadge | undefined {
  return status.tone === 'ok' || status.count === 0
    ? undefined
    : { count: status.count, tone: status.tone }
}

const plural = (n: number, one: string) => `${n} ${one}${n === 1 ? '' : 's'}`

/**
 * The regular-checks state as a status glyph — overdue is red, due-soon is amber, otherwise green.
 *
 * Never-logged is deliberately not an alert here: a check with no interval to be past is "counted, not assumed
 * done" (the fourth state the whole checks screen exists to carry), so it does not turn the tell-tale amber.
 */
export function checksStatus(checks: VehicleSummary['checks']): ScreenStatus {
  // Overdue and flagged are counted together rather than reported one at a time, because they are one pile of
  // work: the badge is what you act on by pressing, and pressing logs both.
  const pressing = checks.overdueCount + checks.attentionCount

  if (checks.overdueCount > 0) {
    return { tone: 'due', label: `${plural(checks.overdueCount, 'check')} overdue`, count: pressing }
  }
  // A flagged check (Attention/Failed on its last log) is on the due axis now, not merely displayed — it turns
  // the tell-tale red like an overdue one.
  if (checks.attentionCount > 0) {
    return { tone: 'due', label: `${plural(checks.attentionCount, 'check')} flagged`, count: pressing }
  }
  if (checks.dueSoonCount > 0) {
    return {
      tone: 'soon',
      label: `${plural(checks.dueSoonCount, 'check')} due soon`,
      count: checks.dueSoonCount,
    }
  }
  return { tone: 'ok', label: 'All checks up to date', count: 0 }
}

/**
 * The vehicle's worst state — the dashboard's tell-tale, and it must summarise the "Needs attention" panel
 * rather than contradict it. So it mirrors that panel's severity model exactly: a Red (due-<30 or expired)
 * statutory renewal — MOT, insurance or road tax — an overdue check, or a reading that contradicts the
 * odometer is red; an Amber statutory renewal or a due-soon check is amber; an open integrity flag alone is
 * the blue info axis; otherwise green.
 *
 * `nextServiceDate` is deliberately excluded from red/amber: an overdue service is a maintenance reminder on
 * the renewals panel, not one of the alerts the attention panel raises — counting it here would light the
 * glyph red while the panel says "nothing outstanding". Data-integrity flags are their own axis with a
 * dedicated dashboard panel, so they do not colour this green/amber/red tell-tale.
 */
export function overallStatus(summary: VehicleSummary): ScreenStatus {
  const { renewals, checks, mileage } = summary
  const statutory = [renewals.mot, renewals.insurance, renewals.roadTax]

  // Counted from the same three sources the branches below test, so the number and the tone are one decision.
  // These are the conditions the attention panel raises an alert for, which is what makes the badge and the
  // panel agree on a normal day; the panel splits expired from due-inside-30 into two alerts, so a car with an
  // expired MOT can show the panel one row and the badge one count of the same thing.
  const red =
    statutory.filter((r) => r.urgency === 'Red').length +
    checks.overdueCount +
    checks.attentionCount +
    (mileage.hasNonMonotonicHistory ? 1 : 0)

  if (red > 0) return { tone: 'due', label: 'Attention needed', count: red }

  const amber = statutory.filter((r) => r.urgency === 'Amber').length + checks.dueSoonCount
  if (amber > 0) return { tone: 'soon', label: 'Something due soon', count: amber }

  return { tone: 'ok', label: 'Nothing needs attention', count: 0 }
}
