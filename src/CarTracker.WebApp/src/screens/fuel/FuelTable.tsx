import type { VehicleSummary } from '../../api/client'
import { Absent, DataTable, Sub, type Column } from '../../components/DataTable'
import { IntegrityPill } from '../../components/Pill'
import { entryEconomy, fmtEconomy, UNIT_LABEL, type FuelUnit } from '../../lib/fuelUnit'

type Entry = VehicleSummary['fuel']['entries'][number]

const dayMonth = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })

const year = (iso: string) => new Date(`${iso}T00:00:00`).getFullYear()

/** Why an interval has no MPG. Mirrors `MpgUnreliableReason`, whose members are structural, not judgements. */
const NO_MPG: Record<string, string> = {
  NoPreviousFill: 'first fill · nothing to measure from',
  NonMonotonicMileage: 'odometer did not advance',
  AwaitingFullTank: 'MPG pending · next full fill',
}

/**
 * The fills table — the richest of the three, and the one the shared `<DataTable>` was shaped around.
 *
 * The design's ninth column is "Fill", Full or Partial. Our `FillLevel` is Full/Half/Quarter, and it is now
 * load-bearing: Full or unrecorded closes the tank and measures MPG; Half/Quarter mark a partial whose figure
 * is deferred to the next full fill (the row then reads "MPG pending · next full fill"). A grouped figure — one
 * that closed a multi-fill segment — carries an "over N fills · M mi" sub-label so it does not read as a single
 * tank.
 */
const dayMonthYear = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

export function FuelTable({
  entries,
  bestMpg,
  worstMpg,
  unit,
  onEdit,
}: {
  entries: Entry[]
  bestMpg: number | null
  worstMpg: number | null
  unit: FuelUnit
  onEdit: (entry: Entry) => void
}) {
  const columns: Column<Entry>[] = [
    {
      key: 'date',
      label: 'Date',
      width: '70px',
      priority: 'essential',
      render: (e) => (
        <b>
          {dayMonth(e.entryDate)}
          <Sub>{year(e.entryDate)}</Sub>
        </b>
      ),
    },
    {
      key: 'odo',
      label: 'Odometer',
      width: '76px',
      align: 'right',
      render: (e) => e.mileage.toLocaleString('en-GB'),
    },
    {
      key: 'since',
      label: 'Miles',
      width: '64px',
      align: 'right',
      priority: 'secondary',
      render: (e) =>
        e.milesSinceLast === null ? (
          <Absent />
        ) : (
          <>
            {e.milesSinceLast.toLocaleString('en-GB')}
            <Sub>mi</Sub>
          </>
        ),
    },
    {
      key: 'litres',
      label: 'Litres',
      width: '66px',
      align: 'right',
      render: (e) => (
        <>
          {e.litres.toFixed(2)}
          <Sub>L</Sub>
        </>
      ),
    },
    {
      key: 'ppl',
      label: '£/L',
      width: '60px',
      align: 'right',
      priority: 'secondary',
      render: (e) => e.pricePerLitre.toFixed(3),
    },
    {
      key: 'total',
      label: 'Total',
      width: '72px',
      align: 'right',
      render: (e) => <b>{e.totalCost.toLocaleString('en-GB', { style: 'currency', currency: 'GBP' })}</b>,
    },
    {
      key: 'station',
      label: 'Station',
      width: '1.2fr',
      render: (e) => (
        <>
          {e.station ?? <Absent>not recorded</Absent>}
          {e.notes !== null && <Sub>{e.notes}</Sub>}
        </>
      ),
    },
    {
      key: 'level',
      label: 'Fill',
      width: '66px',
      priority: 'secondary',
      // Load-bearing: a partial (Half/Quarter) is the reason its own MPG is deferred — see the note above.
      render: (e) =>
        e.fillLevel === null ? (
          <Absent />
        ) : e.unreliableReason === 'AwaitingFullTank' ? (
          <>
            {e.fillLevel}
            <Sub>partial</Sub>
          </>
        ) : (
          e.fillLevel
        ),
    },
    {
      key: 'mpg',
      label: UNIT_LABEL[unit],
      width: '122px',
      align: 'right',
      priority: 'essential',
      render: (e) =>
        e.mpg === null ? (
          <>
            {/* The design prints 24.5 here with an "Estimate" pill. That figure is DEC-012: it rests on
                "miles since last = 334" against a previous reading that exists nowhere, and it is what drags
                the workbook's Worst MPG to 24.49 and makes its average a 13-value one. A caveated number is
                still a number, and it still gets averaged. There is no interval, so there is no figure. */}
            <Absent />
            <Sub>{NO_MPG[e.unreliableReason ?? ''] ?? 'no measurable interval'}</Sub>
          </>
        ) : (
          <span className="mpgcell">
            {/* The value flips with the unit; the Best/Worst/Implausible pills key off the MPG identity below,
                which is the same fill in either unit. */}
            <span className="mpgv">{fmtEconomy(entryEconomy(e, unit))}</span>
            {!e.isPlausible && <IntegrityPill>Implausible</IntegrityPill>}
            {e.isPlausible && e.mpg === bestMpg && <span className="pill ok">Best</span>}
            {e.isPlausible && e.mpg === worstMpg && bestMpg !== worstMpg && (
              <span className="pill due">Worst</span>
            )}
            {/* A grouped figure closed more than one fill, so it spans further than its own row's miles. */}
            {e.spannedFillCount > 1 && e.segmentMiles !== null && (
              <Sub>
                over {e.spannedFillCount} fills · {e.segmentMiles.toLocaleString('en-GB')} mi
              </Sub>
            )}
          </span>
        ),
    },
  ]

  return (
    <DataTable
      columns={columns}
      // Order is the caller's now — FuelLogPage sorts through useTableView, defaulting to date-descending so the
      // log still reads newest-first. Reversing here again would fight that.
      rows={entries}
      rowKey={(e) => e.fuelEntryId}
      label="Fuel fills, newest first"
      onRowClick={onEdit}
      rowLabel={(e) => `Edit the fill on ${dayMonthYear(e.entryDate)} at ${e.mileage.toLocaleString('en-GB')} miles`}
    />
  )
}
