/**
 * The two bits of date arithmetic the forms need, in one place.
 *
 * Display formatters stay per-screen (each says what *that* screen needs); this is only the input side — the
 * value the sheets seed and the quick-fill links compute. `addMonths` was lifted out of `ServiceHistoryPage`,
 * where it was the only copy.
 */

/** Today as a local `YYYY-MM-DD` — what an `<input type="date">` wants, in the user's own timezone. */
export function todayIso(): string {
  const d = new Date()
  const y = d.getFullYear()
  const m = String(d.getMonth() + 1).padStart(2, '0')
  const day = String(d.getDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

/**
 * Add `months` to an ISO date, clamping the day to the target month (31 Jan + 1 month → 28/29 Feb, never a
 * rolled-over 2/3 Mar). UTC throughout so it never shifts a day across a DST boundary.
 */
export function addMonths(iso: string, months: number): string {
  const parts = iso.split('-').map(Number)
  const y = parts[0] ?? 0
  const m = parts[1] ?? 1
  const d = parts[2] ?? 1
  const target = new Date(Date.UTC(y, m - 1 + months, 1))
  const daysInTargetMonth = new Date(Date.UTC(target.getUTCFullYear(), target.getUTCMonth() + 1, 0)).getUTCDate()
  target.setUTCDate(Math.min(d, daysInTargetMonth))
  return target.toISOString().slice(0, 10)
}

/** Add whole years — `addMonths` under the hood, so the leap-day clamp is shared. */
export function addYears(iso: string, years: number): string {
  return addMonths(iso, years * 12)
}
