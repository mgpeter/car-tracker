/**
 * Distinct recent values pulled from existing records — the suggestion source for place fields that have no
 * reference table (station, vendor, tool, …). Rows are taken in the order given, so pass them most-recent-first
 * (the logs already are) and the newest distinct values come out first.
 *
 * Dedupe is case-insensitive but the first-seen casing is what's kept and shown; blanks are skipped.
 */
export function recentValues<T>(
  rows: readonly T[],
  selector: (row: T) => string | null | undefined,
  limit = 6,
): string[] {
  const seen = new Set<string>()
  const out: string[] = []
  for (const row of rows) {
    const value = selector(row)?.trim()
    if (!value) continue
    const key = value.toLowerCase()
    if (seen.has(key)) continue
    seen.add(key)
    out.push(value)
    if (out.length >= limit) break
  }
  return out
}
