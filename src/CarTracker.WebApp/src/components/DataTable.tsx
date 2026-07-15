import type { CSSProperties, ReactNode } from 'react'

/**
 * How readily a column is dropped when the table runs out of room.
 *
 * Dropping beats shrinking: at 13px mono there is nothing left to squeeze, and a table that overflows its
 * panel is worse than one that shows less. `essential` columns survive to the narrowest layout — for the three
 * tables here that is the date and the figure the log is scanned for.
 */
export type ColumnPriority = 'essential' | 'normal' | 'secondary'

export interface Column<T> {
  key: string
  label: string
  /** A grid track: `70px`, `1.2fr`. Widths are per-table, which is the whole reason they are a prop. */
  width: string
  align?: 'right'
  priority?: ColumnPriority
  render: (row: T) => ReactNode
}

interface DataTableProps<T> {
  columns: Column<T>[]
  rows: T[]
  rowKey: (row: T) => string | number
  /** The table's accessible name. Required — an unnamed table is a pile of text. */
  label: string
  rowClassName?: (row: T) => string | undefined
}

const tracks = <T,>(columns: Column<T>[], drop: ColumnPriority[]) =>
  columns
    .filter((c) => !drop.includes(c.priority ?? 'normal'))
    .map((c) => c.width)
    .join(' ')

/**
 * The three logs' table.
 *
 * **Extracted at the third consumer, not the first.** Fuel (9 columns), expenses (7) and mileage (5) were each
 * written concretely first, because two examples cannot tell you which differences are incidental — and the
 * differences turned out to be: column widths, which columns are worth keeping when space runs out, and
 * whether a cell has a sub-line. Everything else was the same three times, including the two mistakes below.
 *
 * **It is a grid, not a `<table>`**, so that a row can become a card on a phone rather than scroll sideways.
 * That costs the semantics a `<table>` gives free, so the ARIA roles pay for them explicitly. The design pays
 * nothing and gets nothing: its markup is bare divs, so a screen reader hears nine unrelated runs of text and
 * the column a number belongs to is carried by position — which is to say by sight.
 *
 * **Reflow is a container query, not a viewport one.** The design's three breakpoints (760/820/860) look
 * arbitrary because they are: they are tuned to the fuel log's nine columns and mean nothing to the mileage
 * log's five. A table cares how wide *it* is, not how wide the window is. The templates come from the column
 * defs as custom properties, because CSS cannot compute them and JS should not own the breakpoints.
 */
export function DataTable<T>({ columns, rows, rowKey, label, rowClassName }: DataTableProps<T>) {
  const style = {
    '--dt-full': tracks(columns, []),
    '--dt-mid': tracks(columns, ['secondary']),
    '--dt-min': tracks(columns, ['secondary', 'normal']),
  } as CSSProperties

  return (
    <div className="panel dt" role="table" aria-label={label} style={style}>
      <div className="dt-head dt-row" role="row">
        {columns.map((c) => (
          <span
            key={c.key}
            role="columnheader"
            className={`dt-c p-${c.priority ?? 'normal'}${c.align === 'right' ? ' r' : ''}`}
          >
            {c.label}
          </span>
        ))}
      </div>

      {rows.map((row) => (
        <div className={`dt-row num ${rowClassName?.(row) ?? ''}`} role="row" key={rowKey(row)}>
          {columns.map((c) => (
            <span
              key={c.key}
              role="cell"
              className={`dt-c p-${c.priority ?? 'normal'}${c.align === 'right' ? ' r' : ''}`}
              // The narrow layout drops the header, so each cell has to say what it is. Not a `<th>` scope
              // substitute — the roles above do that — but the visible label a card needs.
              data-label={c.label}
            >
              {c.render(row)}
            </span>
          ))}
        </div>
      ))}
    </div>
  )
}

/**
 * A cell's second line.
 *
 * **Not a unit slot**, though `47.03 L` makes it look like one. Across the three tables it carries the year
 * under a date, the note under a station, the origin under a reading, and the reason under a missing figure.
 * It is "a sub-line that reflows inline", and reading it as a unit is the first mistake a shared table makes.
 */
export function Sub({ children }: { children: ReactNode }) {
  return <i>{children}</i>
}

/** Muted text for an absent value — never an empty cell, which reads as a loading failure. */
export function Absent({ children = '—' }: { children?: ReactNode }) {
  return <span className="faint">{children}</span>
}
