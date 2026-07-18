import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import type { VehicleSummary } from '../api/client'
import type { components } from '../api/generated/schema'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { ConfirmButton } from '../components/ConfirmButton'
import { Absent, DataTable, Sub, type Column } from '../components/DataTable'
import { Kv } from '../components/Kv'
import { IntegrityPill } from '../components/Pill'
import { Field, Sheet } from '../components/Sheet'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { AppLink } from '../lib/link'
import { usePlate } from '../lib/usePlate'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'

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
      width: '90px',
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
              <Panel className={`stats num${d.hasNonMonotonicHistory ? ' has-flag' : ''}`}>
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

          <Section last>
            <Wrap>
              <SectionHead
                title="Readings"
                rule={<>newest first</>}
                link={<Mark onClick={() => setEditing('new')}>Add reading</Mark>}
              />
              {data.readings.length === 0 ? (
                <Panel>
                  <p className="panel-empty">
                    No readings yet. Logging a fill, a service or an expense with an odometer writes one here
                    automatically.
                  </p>
                </Panel>
              ) : (
                <DataTable
                  columns={columns}
                  rows={[...data.readings].reverse()}
                  rowKey={(r) => r.id}
                  label="Mileage readings, newest first"
                  rowClassName={(r) => (current !== null && r.mileage > current ? 'is-flagged' : undefined)}
                  onRowClick={setEditing}
                  // Only a typed reading is editable. The rest are shadows of another log — a fill, a service —
                  // and are corrected there, so they stay read-only here.
                  rowClickable={(r) => r.origin === 'Manual'}
                  rowLabel={(r) => `Edit the reading on ${dayMonth(r.readingDate)}, ${r.mileage.toLocaleString('en-GB')} miles`}
                />
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
  const [error, setError] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const [seededFor, setSeededFor] = useState<number | 'new' | null>(null)
  const key = existing?.id ?? (editing === 'new' ? ('new' as const) : null)
  if (key !== null && key !== seededFor) {
    setSeededFor(key)
    setV(
      existing === null
        ? {}
        : { readingDate: existing.readingDate, mileage: String(existing.mileage), notes: existing.notes ?? '' },
    )
    setError(null)
  }

  const get = (k: string) => v[k] ?? ''
  const set = (k: string, value: string) => setV((p) => ({ ...p, [k]: value }))

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'mileage'] })
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
      setError(null)
      onClose()
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Could not save.'),
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
    onError: (e) => setError(e instanceof Error ? e.message : 'Could not delete.'),
  })

  const entered = get('mileage') === '' ? null : Number(get('mileage'))
  const wouldFlag = entered !== null && current !== null && entered < current

  return (
    <Sheet
      open={editing !== null}
      onClose={onClose}
      title={existing === null ? 'Add reading' : 'Edit reading'}
      subtitle="the odometer is derived from these"
      onSubmit={() => mutation.mutate()}
      footer={
        <>
          {existing !== null && (
            <ConfirmButton
              onConfirm={() => remove.mutate()}
              pending={remove.isPending}
              cascade="the odometer re-derives from the rest"
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

      {error !== null && (
        <div className="field wide">
          <span className="hint" style={{ color: 'var(--due)' }} role="alert">
            {error}
          </span>
        </div>
      )}
    </Sheet>
  )
}
