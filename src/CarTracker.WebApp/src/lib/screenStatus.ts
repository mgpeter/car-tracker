import type { VehicleSummary } from '../api/client'
import type { StatusTone } from '../shell/scope'

/** A centre-slot status: the tone drives the glyph and colour, the label is its accessible name. */
export interface ScreenStatus {
  tone: StatusTone
  label: string
}

const plural = (n: number, one: string) => `${n} ${one}${n === 1 ? '' : 's'}`

/**
 * The regular-checks state as a status glyph — overdue is red, due-soon is amber, otherwise green.
 *
 * Never-logged is deliberately not an alert here: a check with no interval to be past is "counted, not assumed
 * done" (the fourth state the whole checks screen exists to carry), so it does not turn the tell-tale amber.
 */
export function checksStatus(checks: VehicleSummary['checks']): ScreenStatus {
  if (checks.overdueCount > 0) return { tone: 'due', label: `${plural(checks.overdueCount, 'check')} overdue` }
  if (checks.dueSoonCount > 0) return { tone: 'soon', label: `${plural(checks.dueSoonCount, 'check')} due soon` }
  return { tone: 'ok', label: 'All checks up to date' }
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

  if (statutory.some((r) => r.urgency === 'Red') || checks.overdueCount > 0 || mileage.hasNonMonotonicHistory) {
    return { tone: 'due', label: 'Attention needed' }
  }
  if (statutory.some((r) => r.urgency === 'Amber') || checks.dueSoonCount > 0) {
    return { tone: 'soon', label: 'Something due soon' }
  }
  return { tone: 'ok', label: 'Nothing needs attention' }
}
