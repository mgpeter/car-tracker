import type { ReactNode } from 'react'
import { DUE_STATUS, PRIORITY, type DueStatus, type PillTone, type Priority } from '../lib/status'

interface PillProps {
  tone: PillTone
  /**
   * Required, and that is the whole point. A pill with no text would be a colour swatch, and colour is the
   * third carrier of state here — never the first. There is no icon-only mode for the same reason.
   */
  children: ReactNode
}

/**
 * A generic tone-carrying label. NOT a status component.
 *
 * The design proves the generality: `pill ok` carries "Owned", "On", "Best", "Healthy", "Active", "Resolved";
 * `pill soon` carries "Monitoring", "On order" and — genuinely — "Read-write", a token scope. Only some uses
 * are due-status. So this stays a primitive, and `<DueBadge>` is the one that means something.
 *
 * `tone` cannot be `info`: that is the data-integrity axis, and admitting it here would allow
 * `<Pill tone="info">Overdue</Pill>` — the exact conflation DEC (2026-07-15) says to fix. Use
 * `<IntegrityPill>`, which is a separate component so the two cannot be confused at a call site.
 */
export function Pill({ tone, children }: PillProps) {
  return <span className={`pill ${tone}`}>{children}</span>
}

/**
 * A data-integrity flag: this datum is unreliable, derived, estimated, or not stored.
 *
 * A different question from "is it due", which is why it is a different component rather than a tone. Its
 * labels in the design: Unreliable, Partial, Estimate, Recomputed, Stale, Double-count, Check mileage,
 * Mileage above odometer.
 */
export function IntegrityPill({ children }: { children: ReactNode }) {
  return <span className="pill info">{children}</span>
}

/**
 * A check's due status.
 *
 * There is **no label prop**. The label is derived from the union, so a caller cannot pass `""` and cannot
 * render the state as colour alone — the greyscale property is structural here rather than a review comment.
 * Add a fifth member to the domain's `CheckStatus` and `DUE_STATUS` fails to type-check until it has a label.
 */
export function DueBadge({ due }: { due: DueStatus }) {
  const { label, tone } = DUE_STATUS[due]
  return <Pill tone={tone}>{label}</Pill>
}

/**
 * A task's priority.
 *
 * `High` renders through the design's orphaned `.prio.crit` rule, restored as `.prio.high`: the domain has
 * `Priority.High`, the design renders only `med` and `low`, and shipped a rule for the third that nothing
 * ever used. It was the missing state under a different name, not dead code.
 */
export function PrioTag({ priority }: { priority: Priority }) {
  const { label, tone } = PRIORITY[priority]
  return <span className={`prio ${tone}`}>{label}</span>
}
