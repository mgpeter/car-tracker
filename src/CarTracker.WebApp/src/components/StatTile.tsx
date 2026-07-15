import type { ReactNode } from 'react'
import { DUE_STATUS, type DueStatus } from '../lib/status'

interface StatTileProps {
  due: DueStatus
  /** The count. Rendered in the display face at 30px — the tile's whole job is being scannable. */
  count: number
  /** Optional in-page target (the design's tiles jump to the matching group). Renders an <a> when present. */
  href?: string
}

/**
 * One bucket of a status strip: a big count over an uppercase mono label, with a severity stripe as the
 * left border.
 *
 * Like `<DueBadge>`, the label is **derived** from `due` and is not a prop — so the four tiles cannot drift
 * out of agreement with the badges elsewhere on the page, and none can be rendered as a bare number in a
 * colour. The design's `.tile.info` for "Never logged" becomes `.tile.never`: never-logged is a real fourth
 * *due* state (the domain's `CheckStatus.NeverLogged`, whose own comment says it is "emphatically not Ok"),
 * not a data-integrity flag. Blue stays on the integrity axis.
 */
export function StatTile({ due, count, href }: StatTileProps) {
  const { label, tone } = DUE_STATUS[due]
  const inner = (
    <>
      <span className="t-n num">{count}</span>
      <span className="t-l">{label}</span>
    </>
  )

  return href === undefined ? (
    <div className={`tile ${tone}`}>{inner}</div>
  ) : (
    <a className={`tile ${tone}`} href={href}>
      {inner}
    </a>
  )
}

/** The strip the tiles sit in. Four columns on desktop, and the design reflows it below 680px. */
export function StatTiles({ children }: { children: ReactNode }) {
  return <div className="tiles">{children}</div>
}
