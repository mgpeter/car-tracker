import type { VehicleSummary } from '../../api/client'
import { IntegrityPill } from '../../components/Pill'

type Entry = VehicleSummary['fuel']['entries'][number]

const dayMonth = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })

const year = (iso: string) => new Date(`${iso}T00:00:00`).getFullYear()

/** Why an interval has no MPG. Mirrors `MpgUnreliableReason`, whose members are structural, not judgements. */
const NO_MPG: Record<string, string> = {
  NoPreviousFill: 'first fill · nothing to measure from',
  NonMonotonicMileage: 'odometer did not advance',
}

/**
 * The fills table.
 *
 * **This is the first of three tables** (fuel 9 columns, expenses 7, mileage 5) and it is deliberately not a
 * `<DataTable>`. The plan says extract at the third consumer: two examples cannot tell you which differences
 * are incidental, and a generic table guessed from one is a prop for every column.
 *
 * What the seam looks like from here, for whoever writes the third: the row is a CSS grid of named column
 * classes, not a `<table>`; `<i>` inside a cell is **not a unit slot** — `.c-date` renders `14 Jul` with the
 * year in it — it is a general "sub-line that reflows inline". The three breakpoints (760/820/860) look
 * arbitrary because they are: they are tuned to *this* column set, which is exactly why they should become
 * container queries rather than be copied.
 *
 * The design's ninth column is "Fill", Full or Partial, and it is the column that most needs explaining. Our
 * `FillLevel` is Full/Half/Quarter and it changes nothing: the fuel-basis spec made litres the sole basis of
 * MPG, so this column is a note about the tank and never a reason a figure is missing. The design uses it as
 * exactly that reason.
 */
export function FuelTable({ entries, bestMpg, worstMpg }: { entries: Entry[]; bestMpg: number | null; worstMpg: number | null }) {
  // Newest first. The domain returns them oldest-first because MPG is measured against the previous fill; a
  // log is read from the top.
  const rows = [...entries].reverse()

  return (
    /*
     * Real ARIA table semantics over a CSS grid. The design's markup is bare divs with no roles at all, so its
     * nine-column table is announced as nine unrelated runs of text — the column a number belongs to is carried
     * by position alone, which is to say by sight alone. A grid cannot use <table> without giving up the
     * reflow, so the roles carry it instead: they are the whole reason the layout choice is affordable.
     */
    <div className="panel ftable" role="table" aria-label="Fuel fills, newest first">
      <div className="fhead" role="row">
        <span className="c-date" role="columnheader">Date</span>
        <span className="c-odo" role="columnheader">Odometer</span>
        <span className="c-since" role="columnheader">Miles</span>
        <span className="c-ltr" role="columnheader">Litres</span>
        <span className="c-ppl" role="columnheader">£/L</span>
        <span className="c-tot" role="columnheader">Total</span>
        <span className="c-stn" role="columnheader">Station</span>
        <span className="c-lvl" role="columnheader">Fill</span>
        <span className="c-mpg" role="columnheader">MPG</span>
      </div>

      {rows.map((e) => (
        <div className="frow num" role="row" key={e.fuelEntryId}>
          <span role="cell" className="c c-date">
            {dayMonth(e.entryDate)}
            <i>{year(e.entryDate)}</i>
          </span>
          <span role="cell" className="c c-odo">{e.mileage.toLocaleString('en-GB')}</span>
          <span role="cell" className="c c-since">
            {e.milesSinceLast === null ? '—' : e.milesSinceLast.toLocaleString('en-GB')}
            {e.milesSinceLast !== null && <i>mi</i>}
          </span>
          <span role="cell" className="c c-ltr">
            {e.litres.toFixed(2)}
            <i>L</i>
          </span>
          <span role="cell" className="c c-ppl">{e.pricePerLitre.toFixed(3)}</span>
          <span role="cell" className="c c-tot">
            {e.totalCost.toLocaleString('en-GB', { style: 'currency', currency: 'GBP' })}
          </span>
          <span role="cell" className="c c-stn">
            {e.station ?? <span className="faint">not recorded</span>}
            {e.notes !== null && <i>{e.notes}</i>}
          </span>
          {/* Descriptive. Never the reason an MPG is absent — see the note above. */}
          <span role="cell" className="c c-lvl">{e.fillLevel ?? <span className="faint">—</span>}</span>
          <span role="cell" className="c c-mpg">
            {e.mpg === null ? (
              <>
                {/* The design prints 24.5 here with an "Estimate" pill. That figure is DEC-012: it rests on
                    "miles since last = 334" against a previous reading that exists nowhere, and it is what
                    drags the workbook's Worst MPG to 24.49 and makes its average a 13-value one. A caveated
                    number is still a number, and it still gets averaged. There is no interval, so there is no
                    figure. */}
                <span className="mpgv faint">—</span>
                <i>{NO_MPG[e.unreliableReason ?? ''] ?? 'no measurable interval'}</i>
              </>
            ) : (
              <>
                <span className="mpgv">{e.mpg.toFixed(1)}</span>
                {!e.isPlausible && <IntegrityPill>Implausible</IntegrityPill>}
                {e.isPlausible && e.mpg === bestMpg && <span className="pill ok">Best</span>}
                {e.isPlausible && e.mpg === worstMpg && bestMpg !== worstMpg && (
                  <span className="pill due">Worst</span>
                )}
              </>
            )}
          </span>
        </div>
      ))}
    </div>
  )
}
