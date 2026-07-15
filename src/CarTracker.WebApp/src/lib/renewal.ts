import type { PillTone } from './status'

/**
 * Mirrors `CarTracker.Shared.Metrics.RenewalUrgency` — the wire names, as everywhere else.
 *
 * The domain's own comment insists it is urgency and not colour — that naming the paint is the UI's job, and a
 * domain service that knows hex codes has the layering wrong. That is right, and the enum is nonetheless named
 * after three colours. This file is where that gets repaired: nothing below maps a member to the paint of the
 * same name, and `Red` in particular does not mean red.
 */
export type RenewalUrgency = 'Ok' | 'Amber' | 'Red'

interface RenewalPresentation {
  label: string
  tone: PillTone
}

/**
 * What a renewal's pill says — the question `status.ts` refused to answer, resolved here with the thresholds
 * beside it, as it asked.
 *
 * **`Urgency` alone cannot label a renewal, and this is the whole reason the function takes two arguments.**
 * `RenewalCalculator.UrgencyOf` is `< 30 => Red`, which is a bucket with no floor: an MOT expiring in 23 days
 * and one that expired 12 days ago are both `Red`. They are not the same fact. One is a reminder, the other is
 * a car that is illegal to drive, and a single "Red" label would flatten that distinction at exactly the
 * moment it matters most. The domain keeps the negative deliberately — its comment: "expired 12 days ago is
 * actionable in a way that a clamped zero is not" — so the sign is the information, and reading `Urgency`
 * without it throws that away.
 *
 * Note what is NOT here: `Overdue`. That word belongs to a *check*, where it means past its interval and the
 * remedy is to go and look under the bonnet. A renewal does not lapse quietly — it expires, on a date, with
 * legal consequences. `DueStatus` was kept away from renewals for this reason and stays away.
 */
export function renewalPresentation(
  urgency: RenewalUrgency | null,
  daysRemaining: number | null,
): RenewalPresentation {
  // No date to count to. Not OK — OK is a claim, and we have nothing to base it on. The design never rendered
  // this because its four renewals were all populated; a real vehicle added this morning has none of them.
  if (urgency === null || daysRemaining === null) return { label: 'Not set', tone: 'never' }

  // Expired. Read the sign, not the bucket.
  if (daysRemaining < 0) return { label: 'Expired', tone: 'due' }

  switch (urgency) {
    // Under 30 days and not yet expired. "Due" — imperative, and distinct from both "Due soon" and "Expired".
    case 'Red':
      return { label: 'Due', tone: 'due' }
    case 'Amber':
      return { label: 'Due soon', tone: 'soon' }
    case 'Ok':
      return { label: 'OK', tone: 'ok' }
  }
}

/**
 * The countdown, phrased so it reads as English at every sign.
 *
 * "-12 days" is not a thing anyone says, and it is what a naive `${days} days` renders the moment a renewal
 * lapses — the one case where the reader most needs the sentence to be plain.
 */
export function countdownText(daysRemaining: number | null): string {
  if (daysRemaining === null) return 'no date'
  if (daysRemaining < 0) return `${Math.abs(daysRemaining).toLocaleString('en-GB')} days ago`
  if (daysRemaining === 0) return 'today'
  if (daysRemaining === 1) return 'tomorrow'
  return `${daysRemaining.toLocaleString('en-GB')} days`
}

/** The word before the countdown: "expired 12 days ago" vs "expires in 243 days". */
export function countdownVerb(daysRemaining: number | null): string {
  if (daysRemaining === null) return 'no renewal date recorded'
  if (daysRemaining < 0) return 'expired'
  if (daysRemaining <= 1) return 'expires'
  return 'expires in'
}
