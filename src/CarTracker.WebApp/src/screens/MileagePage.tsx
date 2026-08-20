import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import type { VehicleSummary } from '../api/client'
import type { components } from '../api/generated/schema'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys } from '../api/queries'
import { anomalyKeys } from '../api/anomalies'
import { Btn, Mark } from '../components/Btn'
import { ConfirmButton } from '../components/ConfirmButton'
import { Absent, DataTable, Sub, type Column } from '../components/DataTable'
import { FixBanner } from '../components/FixBanner'
import { Kv } from '../components/Kv'
import { IntegrityPill } from '../components/Pill'
import { Field, Sheet } from '../components/Sheet'
import { TableControls } from '../components/TableControls'
import { TimeChart } from '../components/TimeChart'
import { useTableView, type SortKey, type TableSearch } from '../components/useTableView'
import { todayIso } from '../lib/date'
import { fieldError, formError, reportApiError, type FieldErrors } from '../lib/formErrors'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { AppLink } from '../lib/link'
import { useFlagFix, useOpenFixedRow } from '../lib/useFlagFix'
import { usePlate } from '../lib/usePlate'
import { useVehicleReg } from '../routes'
import type { ScreenId } from '../shell/nav'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'
import { useAddOnArrival } from '../lib/useAddOnArrival'

/** The wire enum, so a new member is a type error here rather than a raw string on screen. */
type Origin = components['schemas']['MileageOrigin']

interface Reading {
  id: number
  readingDate: string
  mileage: number
  origin: Origin
  notes: string | null
}

interface MileageLog {
  derived: VehicleSummary['mileage']
  readings: Reading[]
}

const dayMonth = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })

const year = (iso: string) => new Date(`${iso}T00:00:00`).getFullYear()

/**
 * Where a reading came from. Most are written by another log rather than typed, and saying so is what makes
 * the list legible — fourteen rows nobody entered would otherwise look like an import.
 *
 * `Record<Origin, string>` rather than a lookup with a fallback: the first version of this was hand-guessed
 * (`Expense`, `Mot`) and missed `Tyre`, `Wash` and `Purchase`, so BT53's founding reading rendered the raw
 * enum name. A fallback would have hidden that forever. Now a new member fails the build.
 */
const ORIGIN: Record<Origin, string> = {
  Manual: 'typed',
  Fuel: 'from a fill',
  Tyre: 'from a tyre log',
  Wash: 'from a wash',
  Service: 'from a service',
  // Distinct from Manual on purpose: "the odometer read 76,632 when I bought it" is a purchase record, and
  // miles-since-purchase rests on being able to tell it from an observation made later.
  Purchase: 'bought at',
}

/**
 * Where a mirrored reading is actually corrected.
 *
 * A reading written by another log is read-only here (`rowClickable` below), and until now the screen simply
 * did not say where to go — fine while nobody was *sent* to a specific row, and not fine now that the
 * integrity queue's "Fix this" lands on one. `MileageReading` carries no link back to the record that wrote
 * it, and matching by date and mileage would be a guess, so this maps the origin to the screen that owns that
 * kind of row and stops there: one honest hop, no invented pointer.
 *
 * `Record<Origin, …>` for the reason `ORIGIN` above is — a new member fails the build rather than falling
 * through to a link that goes nowhere.
 */
const CORRECTED_AT: Record<Origin, { screen: ScreenId; label: string; what: string } | null> = {
  Manual: null,
  Fuel: { screen: 'fuel', label: 'Fuel log', what: 'a fill' },
  Tyre: { screen: 'tyres', label: 'Tyre log', what: 'a tyre reading' },
  Wash: { screen: 'wash', label: 'Wash log', what: 'a wash' },
  Service: { screen: 'service', label: 'Service history', what: 'a service record' },
  // The purchase reading is the vehicle's own founding figure, not a log entry — it is edited on the profile.
  Purchase: { screen: 'vehicle-info', label: 'Vehicle info', what: "the vehicle's purchase mileage" },
}

/**
 * The mileage log — small, and the one that carries the project's sharpest rule.
 *
 * **Current mileage is the newest reading by DATE, not the largest.** The workbook has a service record dated
 * 27 Jun 2026 logging 83,000 mi against a current 80,712 — almost certainly 80,300 mistyped. `MAX(mileage)`
 * would make that typo the odometer forever, and no later reading could ever correct it. So the two figures
 * are shown side by side here: when they disagree, that is the flag, and the odometer does not move.
 */
export function MileagePage() {
  const reg = useVehicleReg()
  const plate = usePlate()
  const [editing, setEditing] = useState<Reading | 'new' | null>(null)

  // Arrived from the dashboard's quick add, which carries ?add=1 rather than mounting this sheet on the
  // dashboard. Opening it here is what makes that one press instead of two.
  useAddOnArrival(() => setEditing('new'))

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['vehicle', reg, 'mileage'] as const,
    queryFn: async () => {
      const result = await apiRequest<MileageLog>(`/api/vehicles/${encodeURIComponent(reg)}/mileage`)
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
  })

  const d = data?.derived
  const current = d?.currentMileage ?? null

  const readings = data?.readings ?? []

  // Arrived from the integrity queue's "Fix this". `MileageNonMonotonic` names a reading — but only a typed
  // one can be corrected here, so `useOpenFixedRow` is told which rows are editable and simply does not open a
  // sheet for the rest. The row is still highlighted, and `fixMirror` below says where its fix lives.
  const { flag, clear } = useFlagFix(reg, 'MileageReading')
  useOpenFixedRow(flag?.entityId, data?.readings, (r) => r.id, setEditing, (r) => r.origin === 'Manual')

  const fixRow = flag === null ? undefined : readings.find((r) => r.id === flag.entityId)
  const fixMirror = fixRow === undefined ? null : CORRECTED_AT[fixRow.origin]

  const sorts: SortKey<Reading>[] = useMemo(
    () => [
      { id: 'date', label: 'Date', compare: (a, b) => a.readingDate.localeCompare(b.readingDate) },
      { id: 'mileage', label: 'Odometer', compare: (a, b) => a.mileage - b.mileage },
    ],
    [],
  )
  // The origin LABEL, not the enum name: the column renders "from a fill", so that is what someone types.
  // Searching the raw member would make the box disagree with the words beside it.
  const search: TableSearch<Reading> = useMemo(
    () => ({ label: 'Search', fields: (r) => [r.notes, ORIGIN[r.origin]] }),
    [],
  )

  // Default date-descending reproduces the log's newest-first order — and replaces the old raw `.reverse()`,
  // which rendered oldest-first under a "newest first" caption (the API already returns newest-first).
  const view = useTableView(readings, { sorts, search, defaultSortId: 'date', defaultDir: 'desc' })

  // Odometer over time — every reading by its date. A reading above the current odometer (a mistyped 83,000) is
  // plotted too, not hidden: the page's thesis is that the disagreement IS the flag. No good/bad axis — a rising
  // odometer is neither good nor bad, so TimeChart just marks the latest point.
  const mileagePoints = readings.map((r) => ({ date: r.readingDate, value: r.mileage }))
  const mileageLabel =
    mileagePoints.length === 0
      ? 'No readings yet.'
      : `Mileage across ${mileagePoints.length} reading${mileagePoints.length === 1 ? '' : 's'}, from ` +
        `${Math.min(...mileagePoints.map((p) => p.value)).toLocaleString('en-GB')} to ` +
        `${Math.max(...mileagePoints.map((p) => p.value)).toLocaleString('en-GB')} mi. ` +
        `Latest ${current === null ? '—' : `${current.toLocaleString('en-GB')} mi`}.`

  const columns: Column<Reading>[] = [
    {
      key: 'date',
      label: 'Date',
      width: '70px',
      priority: 'essential',
      render: (r) => (
        <b>
          {dayMonth(r.readingDate)}
          <Sub>{year(r.readingDate)}</Sub>
        </b>
      ),
    },
    {
      key: 'mileage',
      label: 'Odometer',
      // Wide enough for the pill, not just the number — the same measurement the expenses Source column
      // records. `Above current` is 114.6px at the pill's metrics (10px mono, 700, 0.12em tracking, 9px
      // padding, 1.5px border) and the track was 90px; a pill is `white-space: nowrap`, so it neither wrapped
      // nor shrank, it painted straight over the Source column beside it. The cell wraps the pill onto its own
      // line (see `.dt-c:has(.pill)`), so this only has to hold the wider of the two, not both side by side.
      width: '124px',
      align: 'right',
      priority: 'essential',
      render: (r) => (
        <>
          <b>{r.mileage.toLocaleString('en-GB')}</b>
          {/* The flag, on the row that causes it. Never a correction: spec §5.3 says a reading above the
              current odometer is surfaced, not silently accepted and not silently dropped. */}
          {current !== null && r.mileage > current && <IntegrityPill>Above current</IntegrityPill>}
        </>
      ),
    },
    {
      key: 'origin',
      label: 'Source',
      width: '110px',
      render: (r) => ORIGIN[r.origin],
    },
    {
      key: 'notes',
      label: 'Notes',
      width: '1fr',
      priority: 'secondary',
      render: (r) => r.notes ?? <Absent />,
    },
  ]

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="mileage"
      center={{ kind: 'action', icon: 'plus', label: 'Add reading', onClick: () => setEditing('new') }}
      footer={
        <>
          Current mileage is the <b>newest reading by date</b>, never the largest. A mistyped 83,000 would
          otherwise become the odometer permanently, and nothing later could correct it. Most readings here are
          written by another log — a fill, a service, an expense — rather than typed.
        </>
      }
    >
      <PageHead
        eyebrow="Mileage · computed live"
        title="Mileage"
        plate={plate}
        pmeta={
          current === null ? undefined : (
            <>
              Current <b>{current.toLocaleString('en-GB')} mi</b>
              <br />
              The newest reading by date —<br />
              not the highest number on record
            </>
          )
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <h2 className="panel-title">The mileage log could not be loaded</h2>
              <p className="panel-empty">{error instanceof Error ? error.message : 'The request failed.'}</p>
              <button className="btn" type="button" onClick={() => void refetch()}>
                Try again
              </button>
            </Panel>
          </Wrap>
        </Section>
      ) : isPending || data === undefined || d === undefined ? (
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
                title="Derived"
                rule={<>from the readings below</>}
                link={
                  <AppLink className="sec-link" to="dashboard" reg={reg}>
                    Dashboard →
                  </AppLink>
                }
              />
              <Panel className={`stats four num${d.hasNonMonotonicHistory ? ' has-flag' : ''}`}>
                <Kv
                  label="Current"
                  value={current === null ? '—' : current.toLocaleString('en-GB')}
                  note={d.asOfDate === null ? 'no readings' : `newest by date · ${dayMonth(d.asOfDate)}`}
                />
                <Kv
                  label="Highest recorded"
                  value={d.highestRecordedMileage === null ? '—' : d.highestRecordedMileage.toLocaleString('en-GB')}
                  // The two figures side by side, because their disagreement IS the flag. The workbook has no
                  // equivalent: it has one number, and a typo in it is permanent.
                  note={
                    d.hasNonMonotonicHistory
                      ? 'above the current reading — see the flag'
                      : 'agrees with the current reading'
                  }
                />
                <Kv
                  label="Since purchase"
                  value={d.milesSincePurchase === null ? '—' : `${d.milesSincePurchase.toLocaleString('en-GB')} mi`}
                  note="from the purchase odometer"
                />
                <Kv label="Readings" value={String(data.readings.length)} note="most written by another log" />
              </Panel>

              {d.hasNonMonotonicHistory && (
                <Panel className="attn attn-info">
                  <div>
                    <div className="attn-k">Mileage · not monotonic</div>
                    <h3>A reading is above the current odometer</h3>
                    <p>
                      {d.highestRecordedMileage?.toLocaleString('en-GB')} mi is on record against a latest
                      reading of {current?.toLocaleString('en-GB')} mi. A mileage cannot go down, so one of the
                      two is a typo. It is flagged and kept — the odometer does not move, and the reading is
                      not deleted, because which one is wrong is not ours to guess.
                    </p>
                  </div>
                </Panel>
              )}
            </Wrap>
          </Section>

          {readings.length >= 2 && (
            <Section>
              <Wrap>
                <SectionHead title="Over time" rule={<>every reading, by date</>} />
                <Panel className="pad">
                  <h3 className="chart-title">Odometer over time</h3>
                  <TimeChart
                    series={[{ id: 'mileage', label: 'Miles', points: mileagePoints }]}
                    unit="mi"
                    format={(v) => v.toLocaleString('en-GB')}
                    label={mileageLabel}
                    emptyMessage="Two readings are needed to plot a line."
                  />
                </Panel>
              </Wrap>
            </Section>
          )}

          <Section last>
            <Wrap>
              <SectionHead
                title="Readings"
                rule={<>sortable — click a typed reading to edit</>}
                link={<Mark onClick={() => setEditing('new')}>Add reading</Mark>}
              />

              {/* Below the head, not above it: the banner is about the table, and floating it after the chart
                  left it attached to nothing. `IntegrityPanel` places its `.attn` the same way.

                  The mirrored case rides the same banner rather than a second box beneath it. A reading
                  written by another log cannot be corrected on this screen, so the default "correct the row
                  below" line would be false — the note and the action say where it can be corrected instead,
                  which is the whole fix path for the workbook's 83,000 mi row. */}
              {flag !== null && (
                <FixBanner
                  flag={flag}
                  reg={reg}
                  onDismiss={clear}
                  {...(fixMirror !== null &&
                    fixRow !== undefined && {
                      note: (
                        <>
                          This reading was written by {fixMirror.what} and is read-only here — a mirrored
                          reading is corrected at its source, or the two would disagree. Correct it there and
                          the flag clears itself.
                        </>
                      ),
                      action: { screen: fixMirror.screen, label: fixMirror.label },
                    })}
                />
              )}
              {data.readings.length === 0 ? (
                <Panel>
                  <p className="panel-empty">
                    No readings yet. Logging a fill, a service or an expense with an odometer writes one here
                    automatically.
                  </p>
                </Panel>
              ) : (
                <>
                  <TableControls view={view} noun="readings" />
                  <DataTable
                    columns={columns}
                    rows={view.rows}
                    rowKey={(r) => r.id}
                    label="Mileage readings"
                    // Both can land on the same row — the reading above the odometer IS the one the queue
                    // sends you to — so they compose rather than replace, and `.is-fix` adds an outline on
                    // top of the stripe so the two are still distinguishable in greyscale.
                    rowClassName={(r) =>
                      [
                        current !== null && r.mileage > current ? 'is-flagged' : '',
                        r.id === flag?.entityId ? 'is-fix' : '',
                      ]
                        .filter(Boolean)
                        .join(' ') || undefined
                    }
                    scrollTo={(r) => r.id === flag?.entityId}
                    onRowClick={setEditing}
                    // Only a typed reading is editable. The rest are shadows of another log — a fill, a service —
                    // and are corrected there, so they stay read-only here.
                    rowClickable={(r) => r.origin === 'Manual'}
                    rowLabel={(r) => `Edit the reading on ${dayMonth(r.readingDate)}, ${r.mileage.toLocaleString('en-GB')} miles`}
                  />
                </>
              )}
            </Wrap>
          </Section>
        </>
      )}

      <AddReadingSheet editing={editing} onClose={() => setEditing(null)} reg={reg} current={current} />
    </AppShell>
  )
}

interface AnomalyFlag {
  id: number
  message: string
}

function AddReadingSheet({
  editing,
  onClose,
  reg,
  current,
}: {
  editing: Reading | 'new' | null
  onClose: () => void
  reg: string
  current: number | null
}) {
  const existing = editing !== 'new' && editing !== null ? editing : null
  const [v, setV] = useState<Record<string, string>>({})
  const [errors, setErrors] = useState<FieldErrors>({})
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const [seededFor, setSeededFor] = useState<number | 'new' | null>(null)
  const key = existing?.id ?? (editing === 'new' ? ('new' as const) : null)
  if (key !== null && key !== seededFor) {
    setSeededFor(key)
    setV(
      existing === null
        ? { readingDate: todayIso() }
        : { readingDate: existing.readingDate, mileage: String(existing.mileage), notes: existing.notes ?? '' },
    )
    setErrors({})
  }

  const get = (k: string) => v[k] ?? ''
  const set = (k: string, value: string) => setV((p) => ({ ...p, [k]: value }))

  // The fields the server can flag on a reading — anything else it returns falls to the footer banner.
  const FIELD_KEYS = ['mileage'] as const

  // Checked here so the answer is instant and beside the field; the server validates independently.
  const validate = (): FieldErrors => {
    const e: FieldErrors = {}
    const mileage = Number(get('mileage').replace(/[\s,]/g, ''))
    if (!Number.isFinite(mileage) || mileage <= 0) e['mileage'] = ['An odometer reading greater than zero.']
    return e
  }

  const submit = () => {
    const found = validate()
    setErrors(found)
    if (Object.keys(found).length === 0) mutation.mutate()
  }

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'mileage'] })
    // A reading is what `MileageNonMonotonic` is about, and the scanner raises or retracts it inside this
    // same write — so correcting a mistyped odometer must take its flag off the queue with it.
    await queryClient.invalidateQueries({ queryKey: anomalyKeys.all(reg) })
    await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
    await queryClient.invalidateQueries({ queryKey: queryKeys.garage })
  }

  const mutation = useMutation({
    mutationFn: async () => {
      const body = {
        readingDate: get('readingDate'),
        mileage: Number(get('mileage')),
        notes: get('notes') || null,
      }
      const result = await apiRequest<{ id: number; flags?: AnomalyFlag[] }>(
        existing === null
          ? `/api/vehicles/${encodeURIComponent(reg)}/mileage`
          : `/api/vehicles/${encodeURIComponent(reg)}/mileage/${existing.id}`,
        {
          method: existing === null ? 'POST' : 'PATCH',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body),
        },
      )
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async (res) => {
      await invalidate()
      // A flag never blocks the save. Saying so is the difference between the app accepting something it does
      // not believe and the app telling you what it noticed.
      const flags = res.flags ?? []
      toast(
        flags.length > 0
          ? `Reading saved · ${flags[0]!.message} · flagged, not refused`
          : existing === null
            ? 'Reading saved · the odometer recomputed'
            : 'Reading updated · the odometer recomputed',
      )
      setV({})
      setSeededFor(null)
      setErrors({})
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  const remove = useMutation({
    mutationFn: async () => {
      if (existing === null) return
      const result = await apiRequest<null>(`/api/vehicles/${encodeURIComponent(reg)}/mileage/${existing.id}`, {
        method: 'DELETE',
      })
      if (!result.ok) throw new ApiFailure(result.error)
    },
    onSuccess: async () => {
      await invalidate()
      toast('Reading deleted · the odometer re-derived from the newest remaining reading')
      setV({})
      setSeededFor(null)
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  const entered = get('mileage') === '' ? null : Number(get('mileage'))
  const wouldFlag = entered !== null && current !== null && entered < current

  return (
    <Sheet
      open={editing !== null}
      onClose={onClose}
      title={existing === null ? 'Add reading' : 'Edit reading'}
      subtitle="the odometer is derived from these"
      onSubmit={submit}
      footer={
        <>
          {existing !== null && (
            <ConfirmButton
              onConfirm={() => remove.mutate()}
              pending={remove.isPending}
              cascade="odometer re-derives"
            />
          )}
          <Btn type="submit" onClick={() => {}}>
            {mutation.isPending ? 'Saving…' : existing === null ? 'Save reading' : 'Save changes'}
          </Btn>
        </>
      }
    >
      <Field label="Date" hint="the newest date wins, not the highest number">
        {(p) => <input type="date" value={get('readingDate')} onChange={(e) => set('readingDate', e.target.value)} {...p} />}
      </Field>

      <Field
        label="Odometer"
        error={fieldError(errors, 'mileage')}
        hint={current === null ? 'the first reading' : `current is ${current.toLocaleString('en-GB')} mi`}
      >
        {(p) => <input type="text" inputMode="numeric" placeholder="80,712" value={get('mileage')} onChange={(e) => set('mileage', e.target.value)} {...p} />}
      </Field>

      <Field label="Notes" wide>
        {(p) => <input type="text" value={get('notes')} onChange={(e) => set('notes', e.target.value)} {...p} />}
      </Field>

      {wouldFlag && (
        <div className="field wide">
          {/* Not a validation error, and not a block. §5.3: flag, never refuse. A reading below the current
              odometer is often perfectly correct — a backdated entry — which is exactly why the app must not
              decide it is wrong. */}
          <span className="hint hint-info">
            Below the current {current?.toLocaleString('en-GB')} mi. If this is a backdated reading that is
            fine; if the date is today it will be flagged for review. Either way it saves.
          </span>
        </div>
      )}

      {formError(errors) !== undefined && (
        <div className="field wide">
          <span className="hint err" role="alert">
            {formError(errors)}
          </span>
        </div>
      )}
    </Sheet>
  )
}
