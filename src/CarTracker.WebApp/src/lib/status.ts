/**
 * The status axes.
 *
 * There are THREE, and keeping them apart is the point of this file. The design conflates two of them, and
 * `docs/product/decisions.md` (owner, 2026-07-15) says to fix that rather than port it:
 *
 *   1. **Due status** — ok / due soon / overdue / never logged. Green, amber, rust, neutral.
 *   2. **Data integrity** — a datum is unreliable, derived, or not stored. Blue (`--info`). A *different
 *      question* from "is it due", which is why it gets its own component and never a tone on this axis.
 *   3. **Priority** — a task's importance. Independent of both.
 *
 * `--accent` (orange) is a fourth thing again and is structural only — rules, eyebrows, section marks. See
 * the comment on it in tokens.css; it is the only thing holding that line.
 *
 * The greyscale property depends on this file. Every status here carries a TEXT label, and the components
 * derive that label from the union rather than accepting it as a prop — so a caller cannot render a status as
 * colour alone, because there is no way to pass an empty label.
 */

/**
 * Mirrors `CarTracker.Shared.Metrics.CheckStatus` — deliberately using the WIRE names, so that when task 5
 * generates types from OpenAPI this becomes an identity mapping and a domain change becomes a type error
 * rather than a silent drift. The domain's own comment on the fourth member: "Never performed. Not an error,
 * not a default, and emphatically not Ok."
 *
 * Renewals (MOT, tax, insurance) use the first three, derived from days-remaining against §3.1's thresholds —
 * red under 30 days, amber under 60. They can never be NeverLogged: a renewal always has a date.
 */
export type DueStatus = 'Ok' | 'DueSoon' | 'Overdue' | 'NeverLogged'

/**
 * Mirrors `CarTracker.Shared.Priority`.
 *
 * The design renders only `med` and `low`, and ships a `.prio.crit` rule that nothing uses — so **High, the
 * domain's most important priority, has no rendering in the design at all**. `crit` is that missing state
 * under a different name; it is restored here as `High` rather than dropped as dead.
 */
export type Priority = 'High' | 'Medium' | 'Low'

/**
 * The visual tones a `<Pill>` may take.
 *
 * `info` is absent BY DESIGN and this is load-bearing: it is the data-integrity axis, and a type that admits
 * it would let someone write `<Pill tone="info">Overdue</Pill>` — the exact conflation being fixed. Integrity
 * flags use `<IntegrityPill>`, which is a different component precisely so the two cannot be swapped.
 */
export type PillTone = 'ok' | 'soon' | 'due' | 'never' | 'plain'

interface StatusPresentation {
  /** The visible, uppercase, mono text. Colour is the third carrier of state, never the first. */
  label: string
  tone: PillTone
}

/**
 * `Record<DueStatus, …>` rather than a lookup function, deliberately: add a fifth member to the domain enum
 * and this object fails to type-check until it is given a label. The build breaks; nobody has to notice.
 */
export const DUE_STATUS: Record<DueStatus, StatusPresentation> = {
  Ok: { label: 'OK', tone: 'ok' },
  DueSoon: { label: 'Due soon', tone: 'soon' },
  Overdue: { label: 'Overdue', tone: 'due' },
  // Neutral, not blue and not a severity. Absence of data is not urgency — an unlogged check is not more
  // pressing than an overdue one — and blue is the integrity axis. Greyscale is unaffected either way,
  // because the label carries it.
  NeverLogged: { label: 'Never logged', tone: 'never' },
}

export const PRIORITY: Record<Priority, StatusPresentation> = {
  High: { label: 'High', tone: 'due' },
  Medium: { label: 'Medium', tone: 'soon' },
  Low: { label: 'Low', tone: 'plain' },
}

/**
 * Renewals (MOT, tax, insurance) are NOT modelled here, deliberately.
 *
 * A first attempt mapped §3.1's thresholds — red under 30 days, amber under 60 — onto `DueStatus`. That is
 * wrong, and wrong in this project's least forgivable way: it would label an MOT 23 days away "Overdue". It is
 * urgent; it has not expired. `DueStatus` is a *check's* state, where Overdue genuinely means past its
 * interval.
 *
 * The design gives no guidance either — all four renewals on its dashboard are OK in the frozen data, so it
 * never renders a non-OK renewal and there is no label to port. What a red renewal's pill should SAY is
 * therefore an open question, and belongs to the dashboard screen with the thresholds beside it, not to a
 * presentational component library inventing an answer.
 */
