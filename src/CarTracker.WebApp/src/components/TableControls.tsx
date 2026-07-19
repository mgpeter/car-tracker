import type { TableView } from './useTableView'

/**
 * The shared filter/sort strip — chips, dropdowns, a sort control and a live count, driven entirely by a
 * <see cref="TableView"/>'s declared groups and sorts. The same component for fuel, expenses, tasks and
 * equipment; adding it to a fifth log is declaring predicates, not writing a fourth strip.
 *
 * Real controls, not decoration: chips are buttons with `aria-pressed`, dropdowns are labelled `<select>`s, and
 * the active sort is announced — the greyscale-and-screen-reader bar the rest of the app holds to.
 */
export function TableControls<T>({ view, noun }: { view: TableView<T>; noun: string }) {
  const { groups, sorts } = view.config

  return (
    <div className="tctl" role="group" aria-label={`Filter and sort the ${noun}`}>
      {groups.map((group) =>
        group.render === 'chips' ? (
          <div className="tctl-chips" key={group.id} role="group" aria-label={group.label}>
            <button
              type="button"
              className="chip"
              aria-pressed={(view.selected[group.id] ?? []).length === 0}
              onClick={() => view.clearGroup(group.id)}
            >
              All
            </button>
            {group.options.map((option) => (
              <button
                key={option.id}
                type="button"
                className="chip"
                aria-pressed={view.isSelected(group.id, option.id)}
                onClick={() => view.toggle(group.id, option.id)}
              >
                {option.label}
              </button>
            ))}
          </div>
        ) : (
          <label className="tctl-select" key={group.id}>
            <span className="tctl-label">{group.label}</span>
            <select
              value={(view.selected[group.id] ?? [])[0] ?? ''}
              onChange={(e) => view.select(group.id, e.target.value)}
            >
              <option value="">All</option>
              {group.options.map((option) => (
                <option key={option.id} value={option.id}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
        ),
      )}

      {sorts.length > 0 && (
        <div className="tctl-sort">
          <label className="tctl-select">
            <span className="tctl-label">Sort</span>
            <select value={view.sortId ?? ''} onChange={(e) => view.setSort(e.target.value)}>
              {sorts.map((sort) => (
                <option key={sort.id} value={sort.id}>
                  {sort.label}
                </option>
              ))}
            </select>
          </label>
          <button
            type="button"
            className="chip"
            onClick={view.toggleDir}
            aria-label={`Sort direction: ${view.sortDir === 'asc' ? 'ascending' : 'descending'}. Reverse.`}
          >
            {view.sortDir === 'asc' ? '↑' : '↓'}
          </button>
        </div>
      )}

      <span className="tctl-count" aria-live="polite">
        {view.filtered ? `${view.count} of ${view.total}` : `${view.total}`} {noun}
      </span>
    </div>
  )
}
