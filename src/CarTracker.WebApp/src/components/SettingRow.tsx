import type { ReactNode } from 'react'

interface SettingRowProps {
  label: ReactNode
  /** Rendered as the row's value. A row with nothing to say does not render at all - see below. */
  value: ReactNode
  /** The smaller second line under the value: a countdown, a capacity, a caveat. */
  note?: ReactNode
  /** An `<Mark>`, usually. Omit it and the row is read-only, which is the whole statement. */
  action?: ReactNode
  /**
   * Render even when `value` is empty.
   *
   * Off by default: an empty spec row is worse than an absent one, because it implies the manual said nothing.
   * On for an *editable* row, where the absence is the thing you came to fix and a row you cannot see is a
   * field you cannot fill in.
   */
  keepEmpty?: boolean
}

/**
 * The key/value row that both halves of this screen are built from.
 *
 * It existed twice: `.setrow` with an action in the settings panels, and a local `Row` without one on the
 * vehicle reference card - the same markup, in two files, because the two halves were two screens. They are
 * one screen now.
 *
 * **The action slot is the read-only statement.** A row with no control is not styled differently and needs no
 * badge; `components.css:1064-1071` records why the absent control is the strongest available way to say a
 * value cannot be typed. For a value that is *derived* rather than merely fixed, use {@link DerivedRow}, which
 * says so out loud - the distinction matters, because "you cannot change this here" and "nothing can change
 * this, it is computed" are different promises.
 */
export function SettingRow({ label, value, note, action, keepEmpty = false }: SettingRowProps) {
  const empty = value === null || value === undefined || value === ''
  if (empty && !keepEmpty) return null

  return (
    <div className="setrow num">
      <span className="sk">{label}</span>
      <span className="sv">
        {empty ? 'not recorded' : value}
        {note !== undefined && <i>{note}</i>}
      </span>
      {action}
    </div>
  )
}

/**
 * A row whose value is computed, with the badge and the explanation that says so.
 *
 * Structurally `.setrow.ro`: the label carries an `<IntegrityPill>`, and a full-width `.ro-note` explains
 * where the figure came from. It takes no action slot at all - not an optional one - because a derived value
 * having an edit control is the defect this whole project exists to prevent.
 */
export function DerivedRow({
  label,
  badge,
  value,
  source,
}: {
  label: ReactNode
  badge: ReactNode
  value: ReactNode
  source: ReactNode
}) {
  return (
    <div className="setrow ro num">
      <span className="sk">
        {label} {badge}
      </span>
      <span className="sv">{value}</span>
      <span className="ro-note">{source}</span>
    </div>
  )
}
