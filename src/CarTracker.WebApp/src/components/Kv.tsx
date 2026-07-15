import type { ReactNode } from 'react'

interface KvProps {
  /** The uppercase mono caption. */
  label: string
  /** The figure. Rendered mono and tabular so a row of them aligns. */
  value: ReactNode
  /** The qualifier under it — "263 mi · 4.7 below average". Where a derived figure explains itself. */
  note?: ReactNode
}

/**
 * A labelled figure: caption, value, optional note.
 *
 * The unit of every stats band in the app. `value` is a node rather than a string because a figure is often a
 * number plus a pill ("25.4" + "Unreliable") — and because a *null* derived figure must be able to say so out
 * loud rather than render blank, which is the defect class this project exists to eliminate.
 */
export function Kv({ label, value, note }: KvProps) {
  return (
    <div className="kv">
      <div className="k">{label}</div>
      <div className="v num">{value}</div>
      {note !== undefined && <div className="n">{note}</div>}
    </div>
  )
}

/**
 * A band of `<Kv>`s.
 *
 * `columns` is the design's `.stats` / `.stats.five` / `.stats.four`. It reflows to two columns below 680px —
 * six columns on a phone would be unreadable, and the design drops to a grid rather than scrolling, which is
 * the rule throughout: data reflows, it never scrolls sideways.
 */
export function Stats({ columns = 6, children }: { columns?: 4 | 5 | 6; children: ReactNode }) {
  const modifier = columns === 6 ? '' : columns === 5 ? ' five' : ' four'
  return <div className={`stats${modifier}`}>{children}</div>
}
