import type { VehicleSummary } from '../../api/client'
import { Absent, DataTable, Sub, type Column } from '../../components/DataTable'
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
 * The fills table — the richest of the three, and the one the shared `<DataTable>` was shaped around.
 *
 * The design's ninth column is "Fill", Full or Partial, and it is the column that most needs explaining. Our
 * `FillLevel` is Full/Half/Quarter and it changes nothing: the fuel-basis spec made litres the sole basis of
 * MPG, so this is a note about the tank and never a reason a figure is missing. The design uses it as exactly
 * that reason.
 */
const dayMonthYear = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

export function FuelTable({
  entries,
  bestMpg,
  worstMpg,
  onEdit,
}: {
  entries: Entry[]
  bestMpg: number | null
  worstMpg: number | null
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
      // Descriptive. Never the reason an MPG is absent — see the note above.
      render: (e) => e.fillLevel ?? <Absent />,
    },
    {
      key: 'mpg',
      label: 'MPG',
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
            <span className="mpgv">{e.mpg.toFixed(1)}</span>
            {!e.isPlausible && <IntegrityPill>Implausible</IntegrityPill>}
            {e.isPlausible && e.mpg === bestMpg && <span className="pill ok">Best</span>}
            {e.isPlausible && e.mpg === worstMpg && bestMpg !== worstMpg && (
              <span className="pill due">Worst</span>
            )}
          </span>
        ),
    },
  ]

  return (
    <DataTable
      columns={columns}
      // Newest first. The domain returns them oldest-first because MPG is measured against the previous fill;
      // a log is read from the top.
      rows={[...entries].reverse()}
      rowKey={(e) => e.fuelEntryId}
      label="Fuel fills, newest first"
      onRowClick={onEdit}
      rowLabel={(e) => `Edit the fill on ${dayMonthYear(e.entryDate)} at ${e.mileage.toLocaleString('en-GB')} miles`}
    />
  )
}
