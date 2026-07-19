export interface SelectableCheck {
  name: string
  cadenceLabel: string
}

/**
 * A toggle list of checks to include, shared by the add-vehicle sheet (generic set or copy-from-vehicle) and the
 * settings "add checks" sheet. State lives with the caller: it passes the set of *deselected* names and a toggle
 * handler, so "all on" is the default with no async initialisation.
 *
 * `locked` names are ones the vehicle already has — shown disabled with "already added" instead of a cadence, so
 * the whole set is visible but only the missing ones can be picked. The count is over the selectable (unlocked)
 * rows.
 */
export function CheckSelectList({
  checks,
  deselected,
  onToggle,
  locked,
  header = 'included',
}: {
  checks: SelectableCheck[]
  deselected: Set<string>
  onToggle: (name: string) => void
  locked?: Set<string>
  header?: string
}) {
  const isLocked = (name: string) => locked?.has(name) ?? false
  const selectable = checks.filter((c) => !isLocked(c.name))
  const keptCount = selectable.filter((c) => !deselected.has(c.name)).length

  return (
    <div className="checksel">
      <div className="checksel-head">
        <span>{header}</span>
        <span className="checksel-count num">
          {keptCount} of {selectable.length}
        </span>
      </div>
      <ul className="checksel-list" aria-label="Checks to include">
        {checks.map((c) => {
          const lockedRow = isLocked(c.name)
          return (
            <li key={c.name}>
              <label className={lockedRow ? 'checksel-row is-locked' : 'checksel-row'}>
                <input
                  type="checkbox"
                  checked={!lockedRow && !deselected.has(c.name)}
                  disabled={lockedRow}
                  onChange={() => onToggle(c.name)}
                />
                <span className="checksel-name">{c.name}</span>
                <span className="checksel-cadence">{lockedRow ? 'already added' : c.cadenceLabel}</span>
              </label>
            </li>
          )
        })}
      </ul>
    </div>
  )
}
