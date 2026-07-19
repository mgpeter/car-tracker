import { useQuery } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import type { VehicleSummary } from '../api/client'
import { apiRequest } from '../api/client'
import { ApiFailure, useVehicleSummary } from '../api/queries'
import { Mark } from '../components/Btn'
import { Kv } from '../components/Kv'
import { Seg } from '../components/Seg'
import { TableControls } from '../components/TableControls'
import { TimeChart } from '../components/TimeChart'
import { useTableView, type FilterGroup, type SortKey } from '../components/useTableView'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { economy, entryEconomy, fmtEconomy, lowerIsBetter, setFuelUnit, UNIT_LABEL, useFuelUnit, type FuelUnit } from '../lib/fuelUnit'
import { AppLink } from '../lib/link'
import { recentValues } from '../lib/recentValues'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { AddFillSheet } from './fuel/AddFillSheet'
import { FuelTable } from './fuel/FuelTable'

type Fuel = VehicleSummary['fuel']

const UNIT_OPTIONS: ReadonlyArray<{ value: FuelUnit; label: string }> = [
  { value: 'mpg', label: 'MPG' },
  { value: 'l100', label: 'L/100 km' },
]

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', minimumFractionDigits: 2 })

const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

const dayMonth = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })

/**
 * The fuel log.
 *
 * Reads `GET /api/vehicles/{reg}/fuel`, which returns the same `FuelEconomySummary` the dashboard's panel
 * renders — computed once, through `IDerivedMetricsService`. A raw `FuelEntries` query would hand back rows
 * with no MPG and invite this screen to work it out again, which is how two surfaces start disagreeing about
 * a number.
 */
type FuelEntry = Fuel['entries'][number]

export function FuelLogPage() {
  const reg = useVehicleReg()
  const [editing, setEditing] = useState<FuelEntry | 'new' | null>(null)
  const unit = useFuelUnit()

  const { data: summary } = useVehicleSummary(reg)
  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['vehicle', reg, 'fuel'] as const,
    queryFn: async () => {
      const result = await apiRequest<Fuel>(`/api/vehicles/${encodeURIComponent(reg)}/fuel`)
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
  })

  const last = data?.entries.at(-1)
  // The fill the preview measures against. Entries are oldest-first: adding a fill measures from the newest;
  // editing an existing fill must measure from the one immediately BEFORE it, not the newest — otherwise
  // editing the latest fill measures against itself (miles ≈ 0) and editing an older one measures against the
  // wrong predecessor, either way disagreeing with the server's chronological walk.
  const editingEntry = editing !== 'new' && editing !== null ? editing : null
  const editingIndex = editingEntry
    ? (data?.entries.findIndex((e) => e.fuelEntryId === editingEntry.fuelEntryId) ?? -1)
    : -1
  const predecessor = editingEntry ? (editingIndex > 0 ? data?.entries[editingIndex - 1] : undefined) : last
  const measured = data?.entries.filter((e) => e.mpg !== null && e.isPlausible) ?? []
  const best = measured.find((e) => e.mpg === data?.bestMpg)
  const worst = measured.find((e) => e.mpg === data?.worstMpg)
  const prices = data?.entries.map((e) => e.pricePerLitre) ?? []

  // Distinct stations from the loaded rows — never a second hardcoded list, so the filter can only offer a
  // station some fill actually has.
  const stations = useMemo(
    () => [...new Set((data?.entries ?? []).map((e) => e.station).filter((s): s is string => s !== null))].sort(),
    [data?.entries],
  )

  // Recent stations for the add-fill combobox. Entries are oldest-first, so reverse to offer the newest first;
  // never a hardcoded list — a suggestion can only be a station some fill actually has.
  const stationSuggestions = useMemo(
    () => recentValues([...(data?.entries ?? [])].reverse(), (e) => e.station).map((value) => ({ value })),
    [data?.entries],
  )

  const groups: FilterGroup<FuelEntry>[] = useMemo(() => {
    const cutoff = new Date()
    cutoff.setDate(cutoff.getDate() - 30)
    const cutoffIso = cutoff.toISOString().slice(0, 10)
    return [
      {
        id: 'when',
        label: 'Fills',
        render: 'chips',
        options: [
          { id: 'recent', label: 'Last 30 days', test: (e) => e.entryDate >= cutoffIso },
          // Flagged: no clean plausible MPG — the DEC-012 first-fill interval and the implausible fills.
          { id: 'flagged', label: 'Flagged only', test: (e) => !(e.mpg !== null && e.isPlausible) },
        ],
      },
      {
        id: 'station',
        label: 'Station',
        render: 'select',
        options: stations.map((s) => ({ id: s, label: s, test: (e: FuelEntry) => e.station === s })),
      },
    ]
  }, [stations])

  const sorts: SortKey<FuelEntry>[] = useMemo(
    () => [
      { id: 'date', label: 'Date', compare: (a, b) => a.entryDate.localeCompare(b.entryDate) },
      // Nulls sort below any real figure, so a flagged fill never floats to the top of a best-MPG sort.
      { id: 'mpg', label: 'MPG', compare: (a, b) => (a.mpg ?? -Infinity) - (b.mpg ?? -Infinity) },
    ],
    [],
  )

  // Default date-descending reproduces the log's current newest-first order, so no-filter behaviour is unchanged.
  const view = useTableView(data?.entries ?? [], { groups, sorts, defaultSortId: 'date', defaultDir: 'desc' })

  // Trend series — derived, never stored. MPG is the plausible measured intervals (the identical filter the
  // headline applies), so a 272-mpg splash stays off the line as it stays off the average. Price is every fill.
  const mpgPoints = (data?.entries ?? [])
    .filter((e) => e.mpg !== null && e.isPlausible)
    .map((e) => ({ date: e.entryDate, value: entryEconomy(e, unit) as number }))
  const pricePoints = (data?.entries ?? []).map((e) => ({ date: e.entryDate, value: e.pricePerLitre }))
  const mpgLabel =
    mpgPoints.length === 0
      ? 'No measured intervals yet.'
      : `Fuel economy across ${mpgPoints.length} measured interval${mpgPoints.length === 1 ? '' : 's'}, ranging ` +
        `${Math.min(...mpgPoints.map((p) => p.value)).toFixed(1)} to ${Math.max(...mpgPoints.map((p) => p.value)).toFixed(1)} ${UNIT_LABEL[unit]}. ` +
        `Latest ${mpgPoints[mpgPoints.length - 1]!.value.toFixed(1)}.`
  const priceLabel =
    pricePoints.length === 0
      ? 'No fills yet.'
      : `Fuel price across ${pricePoints.length} fill${pricePoints.length === 1 ? '' : 's'}, ranging ` +
        `£${Math.min(...pricePoints.map((p) => p.value)).toFixed(3)} to £${Math.max(...pricePoints.map((p) => p.value)).toFixed(3)} per litre. ` +
        `Latest £${pricePoints[pricePoints.length - 1]!.value.toFixed(3)}.`

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="fuel"
      center={{ kind: 'action', icon: 'plus', label: 'Add fill', onClick: () => setEditing('new') }}
      footer={
        <>
          MPG is computed per fill from litres and the distance since the previous fill — never stored, and
          never withheld on a partial tank. A figure outside 10–70 MPG is kept and marked, because the entry is
          real even when the figure is not; it is excluded from the averages above, not deleted.
        </>
      }
    >
      <PageHead
        eyebrow={`Fuel log · computed live${data !== undefined && data.lastFillDate !== null ? ` · last fill ${shortDate(data.lastFillDate)}` : ''}`}
        title="Fuel"
        plate={summary?.registration ?? reg}
        pmeta={
          <>
            {summary?.mileage.currentMileage !== null && summary?.mileage.currentMileage !== undefined && (
              <>
                Odometer <b>{summary.mileage.currentMileage.toLocaleString('en-GB')} mi</b>
                <br />
              </>
            )}
            {/* Litres is the basis; fill level does the grouping. A full (or unrecorded) fill closes the tank
                and measures the span since the last full fill; a partial defers its MPG to the next full fill
                and its litres are counted there — nothing is discarded, a correct figure just arrives later. */}
            MPG is measured tank to tank from litres —<br />
            a partial fill defers to your next full fill
          </>
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <h2 className="panel-title">The fuel log could not be loaded</h2>
              <p className="panel-empty">{error instanceof Error ? error.message : 'The request failed.'}</p>
              <button className="btn" type="button" onClick={() => void refetch()}>
                Try again
              </button>
            </Panel>
          </Wrap>
        </Section>
      ) : isPending || data === undefined ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <p className="panel-empty">Loading…</p>
            </Panel>
          </Wrap>
        </Section>
      ) : (
        <>
          <Section>
            <Wrap>
              <SectionHead
                title="Fleet stats"
                rule={
                  data.fillCount === 0 ? (
                    <>no fills yet</>
                  ) : (
                    <>
                      {data.fillCount} fill{data.fillCount === 1 ? '' : 's'}
                      {/* The count that separates a fill from a measurable interval. On BT53's real history
                          these differ by one, and that one is DEC-012's phantom. */}
                      {data.measuredIntervalCount !== data.fillCount && (
                        <> · {data.measuredIntervalCount} measurable</>
                      )}
                      {/* Kept off the averages but on the log. The design has no concept of this at all: its
                          only non-figure is the "Estimate" pill it puts on the interval that never happened. */}
                      {data.implausibleCount > 0 && <> · {data.implausibleCount} implausible</>}
                    </>
                  )
                }
                link={
                  <>
                    {/* The same unit choice as Settings → Appearance, here where the figures are — flips every
                        fuel surface live (stats, charts, table) via the shared store. */}
                    <Seg
                      className="seg-sm"
                      label="Fuel economy units"
                      options={UNIT_OPTIONS}
                      value={unit}
                      onChange={setFuelUnit}
                    />
                    <AppLink className="sec-link" to="dashboard" reg={reg}>
                      Dashboard →
                    </AppLink>
                  </>
                }
              />
              <Panel className="stats num">
                <Kv
                  label="Average"
                  value={fmtEconomy(economy(data.averageMpg, unit))}
                  note={
                    data.averageMpg === null
                      ? 'needs two fills'
                      : `${UNIT_LABEL[unit]} · ${data.measuredIntervalCount} interval${data.measuredIntervalCount === 1 ? '' : 's'}`
                  }
                />
                <Kv
                  label="Best"
                  value={fmtEconomy(economy(data.bestMpg, unit))}
                  note={best !== undefined ? `${dayMonth(best.entryDate)} · ${best.milesSinceLast?.toLocaleString('en-GB')} mi` : 'no interval'}
                />
                <Kv
                  label="Worst"
                  value={fmtEconomy(economy(data.worstMpg, unit))}
                  note={worst !== undefined ? `${dayMonth(worst.entryDate)} · ${worst.milesSinceLast?.toLocaleString('en-GB')} mi` : 'no interval'}
                />
                <Kv
                  label="Total litres"
                  value={data.totalLitres.toFixed(2)}
                  note={data.totalCost > 0 ? `${money(data.totalCost)} pumped` : 'nothing pumped'}
                />
                <Kv
                  label="Avg price/L"
                  value={data.averagePricePerLitre === null ? '—' : `£${data.averagePricePerLitre.toFixed(3)}`}
                  // Volume-weighted (DEC-011). The workbook's plain mean of the price column gives 1.5949
                  // against this 1.5973 — not a defect, a different question answered correctly, which is why
                  // it sits outside the count of five.
                  note={
                    prices.length > 0
                      ? `${Math.min(...prices).toFixed(3)} – ${Math.max(...prices).toFixed(3)} · volume-weighted`
                      : 'no fills'
                  }
                />
                <Kv
                  label="Last fill"
                  value={data.lastFillDate === null ? '—' : dayMonth(data.lastFillDate)}
                  note={last?.station ?? (data.lastFillDate === null ? 'no fills' : 'station not recorded')}
                />
              </Panel>
            </Wrap>
          </Section>

          <Section>
            <Wrap>
              <SectionHead title="Trends" rule={<>MPG and price over time — the real chart the sparkline stood in for</>} />
              <div className="trend-grid">
                <Panel className="pad">
                  <h3 className="chart-title">{UNIT_LABEL[unit]} over time</h3>
                  <TimeChart
                    series={[{ id: 'mpg', label: 'MPG', points: mpgPoints }]}
                    unit={UNIT_LABEL[unit]}
                    label={mpgLabel}
                    good={lowerIsBetter(unit) ? 'lower' : 'higher'}
                    emptyMessage="Economy needs two fills — the first has nothing to measure from."
                  />
                </Panel>
                <Panel className="pad">
                  <h3 className="chart-title">Fuel price over time</h3>
                  <TimeChart
                    series={[{ id: 'price', label: '£/L', points: pricePoints }]}
                    unit="£/L"
                    format={(v) => `£${v.toFixed(2)}`}
                    label={priceLabel}
                    good="lower"
                    emptyMessage="No fills logged yet."
                  />
                </Panel>
              </div>
            </Wrap>
          </Section>

          <Section last>
            <Wrap>
              <SectionHead
                title="Fills"
                rule={<>each fill mirrors into expenses automatically</>}
                link={<Mark onClick={() => setEditing('new')}>Add fill</Mark>}
              />
              {data.entries.length === 0 ? (
                <Panel>
                  <p className="panel-empty">
                    No fills logged yet. The first one records an odometer reading and mirrors into expenses;
                    MPG starts at the second, because the first has nothing to measure from.
                  </p>
                </Panel>
              ) : (
                <>
                  <TableControls view={view} noun="fills" />
                  {view.count === 0 ? (
                    // An empty result is a filter that matched nothing — not the empty log, which reads
                    // differently and must not be mistaken for a load failure.
                    <Panel>
                      <p className="panel-empty">No fills match this filter. Clear it to see all {view.total}.</p>
                    </Panel>
                  ) : (
                    <FuelTable
                      entries={view.rows}
                      bestMpg={data.bestMpg}
                      worstMpg={data.worstMpg}
                      unit={unit}
                      onEdit={setEditing}
                    />
                  )}
                </>
              )}
            </Wrap>
          </Section>
        </>
      )}

      <AddFillSheet
        editing={editing}
        onClose={() => setEditing(null)}
        reg={reg}
        // The predecessor FILL's mileage — the newest fill when adding, the fill just before this one when
        // editing. Never the current odometer reading: MPG measures fuel burned between two fills, so a reading
        // that is not a fill has no litres attached and cannot bound an interval. With that fallback the first
        // fill previewed 66.4 MPG — measured from the purchase reading — while the server correctly returned no
        // MPG at all. A preview that contradicts the server is worse than no preview.
        lastMileage={predecessor?.mileage ?? null}
        averageMpg={data?.averageMpg ?? null}
        today={summary?.asOfDate ?? new Date().toISOString().slice(0, 10)}
        stationSuggestions={stationSuggestions}
      />
    </AppShell>
  )
}
