import type { ReactNode } from 'react'

/**
 * A check's cadence label — Weekly, Monthly, Every 3 months.
 *
 * Not a status: it says how often the check is *meant* to happen, not whether it is due. It sits beside a
 * `<DueBadge>` in the same row and deliberately carries no tone, so the two cannot be read as one thing.
 * Faint, bordered, fixed minimum width so a column of them aligns.
 */
export function Cadence({ children }: { children: ReactNode }) {
  return <span className="cadence">{children}</span>
}
