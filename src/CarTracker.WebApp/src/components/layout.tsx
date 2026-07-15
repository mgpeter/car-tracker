import type { ReactNode } from 'react'

/** The page's horizontal rhythm — 1180px, gutters of 20. Every band uses it, so nothing drifts. */
export function Wrap({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={className === undefined ? 'wrap' : `wrap ${className}`}>{children}</div>
}

/**
 * A page section.
 *
 * `last` adds the bottom padding the final section needs. The design does this with a class rather than
 * `:last-child` because a section is not always last in the DOM — the toast and sheets follow it.
 */
export function Section({ children, last = false }: { children: ReactNode; last?: boolean }) {
  return <section className={last ? 'last' : undefined}>{children}</section>
}

/** The base surface. Everything on a screen sits in one. */
export function Panel({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={className === undefined ? 'panel' : `panel ${className}`}>{children}</div>
}

interface SectionHeadProps {
  title: string
  /**
   * The right-aligned note. `<i>` inside renders in `--accent` — structural emphasis, never status.
   *
   * The design puts *"red under 30 days · amber under 60"* here on the dashboard, which is its one
   * colour-only status statement, and `.rule i` renders those words in orange rather than red or amber, so
   * the prose does not even match its own rendering. That string is rewritten with its screen, not ported.
   */
  rule?: ReactNode
  /** A "see all →" style link. Optional; 8 of 17 screens have one. */
  link?: ReactNode
}

export function SectionHead({ title, rule, link }: SectionHeadProps) {
  return (
    <div className="sec-head">
      <h2>{title}</h2>
      {rule !== undefined && <div className="rule">{rule}</div>}
      {link}
    </div>
  )
}

/** Emphasis inside a `<SectionHead rule>` — accent-coloured, and structural by definition. */
export function RuleMark({ children }: { children: ReactNode }) {
  return <i>{children}</i>
}

/** The footnote under a panel: dashed rule, mono, muted. Where the design explains its own figures. */
export function CFoot({ children }: { children: ReactNode }) {
  return <div className="cfoot">{children}</div>
}

/**
 * Tabular figures.
 *
 * Wrap anything where digits must align in a column — the design marks these `.num`. Not decorative: without
 * it, proportional digits make a column of mileages ragged and much harder to compare.
 */
export function Num({ children, className }: { children: ReactNode; className?: string }) {
  return <span className={className === undefined ? 'num' : `num ${className}`}>{children}</span>
}
