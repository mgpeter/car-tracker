import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { Combobox } from '../components/Combobox'
import { ConfirmButton } from '../components/ConfirmButton'
import { Absent, DataTable, Sub, type Column } from '../components/DataTable'
import { TyreCorners } from '../components/TyreCorners'
import { Field, Sheet } from '../components/Sheet'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { todayIso } from '../lib/date'
import { fieldError, formError, reportApiError, type FieldErrors } from '../lib/formErrors'
import { recentValues } from '../lib/recentValues'
import { usePlate } from '../lib/usePlate'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'

interface TyreReading {
  id: number
  readingDate: string
  mileage: number | null
  psiFrontLeft: number | null
  psiFrontRight: number | null
  psiRearLeft: number | null
  psiRearRight: number | null
  psiSpare: number | null
  treadFrontLeft: number | null
  treadFrontRight: number | null
  treadRearLeft: number | null
  treadRearRight: number | null
  location: string | null
  tool: string | null
  notes: string | null
}

const dayMonth = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })

const year = (iso: string) => new Date(`${iso}T00:00:00`).getFullYear()

/** The MOT limit. Below this a tyre is illegal, not merely worn. */
const LEGAL_TREAD = 1.6

const CORNERS = [
  { psi: 'psiFrontLeft', tread: 'treadFrontLeft', label: 'Front left' },
  { psi: 'psiFrontRight', tread: 'treadFrontRight', label: 'Front right' },
  { psi: 'psiRearLeft', tread: 'treadRearLeft', label: 'Rear left' },
  { psi: 'psiRearRight', tread: 'treadRearRight', label: 'Rear right' },
] as const

/** A corner's figures, or nothing. Never a zero — not measured is not flat. */
const corner = (v: number | null, unit: string) =>
  v === null ? <Absent /> : (
    <>
      {v.toFixed(v % 1 === 0 ? 0 : 1)}
      <Sub>{unit}</Sub>
    </>
  )

/**
 * The tyre log.
 *
 * The design draws a "tyre diagram" that is four `border-radius` divs, not an SVG — so this is a table, which
 * is what four corners of numbers actually are. The diagram earns its place when tread depth has a history
 * worth seeing shaped like a car; with one reading it is decoration.
 *
 * **The spare is nullable and that is the point.** The workbook's eighteenth regular check is "Spare tyre
 * pressure", it has never been logged, and its Dashboard counts 17 of 18 as a result. Not measured is not zero.
 */
export function TyresPage() {
  const reg = useVehicleReg()
  const plate = usePlate()
  const [editing, setEditing] = useState<TyreReading | 'new' | null>(null)

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['vehicle', reg, 'tyres'] as const,
    queryFn: async () => {
      const result = await apiRequest<TyreReading[]>(`/api/vehicles/${encodeURIComponent(reg)}/tyres`)
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
  })

  const latest = data?.at(-1)
  const lowest = latest
    ? Math.min(...CORNERS.map((c) => latest[c.tread]).filter((t): t is number => t !== null), Infinity)
    : Infinity

  // History-derived suggestions for the free-text place fields — newest first (the log is oldest-first).
  const recent = [...(data ?? [])].reverse()
  const locationSuggestions = recentValues(recent, (r) => r.location).map((value) => ({ value }))
  const toolSuggestions = recentValues(recent, (r) => r.tool).map((value) => ({ value }))

  const columns: Column<TyreReading>[] = [
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
    ...CORNERS.map(
      (c): Column<TyreReading> => ({
        key: c.psi,
        label: c.label,
        width: '84px',
        align: 'right',
        render: (r) => (
          <>
            {corner(r[c.psi], 'psi')}
            {r[c.tread] !== null && <Sub>{r[c.tread]!.toFixed(1)} mm tread</Sub>}
          </>
        ),
      }),
    ),
    {
      key: 'spare',
      label: 'Spare',
      width: '74px',
      align: 'right',
      priority: 'secondary',
      // Never logged on BT53, and the reason its dashboard counts 17 of 18.
      render: (r) => (r.psiSpare === null ? <Absent>never</Absent> : corner(r.psiSpare, 'psi')),
    },
    {
      key: 'where',
      label: 'Where',
      width: '1fr',
      priority: 'secondary',
      render: (r) => (
        <>
          {r.location ?? <Absent>not recorded</Absent>}
          {r.tool !== null && <Sub>{r.tool}</Sub>}
        </>
      ),
    },
  ]

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="tyres"
      center={{ kind: 'action', icon: 'plus', label: 'Add reading', onClick: () => setEditing('new') }}
      footer={
        <>
          Pressures and tread by corner. Every figure is optional and none defaults to zero — <b>not measured
          is not flat</b>, and the spare in particular has never been checked on this car, which is exactly why
          the old dashboard counts 17 of its 18 checks. The MOT tread limit is {LEGAL_TREAD} mm.
        </>
      }
    >
      <PageHead
        eyebrow="Tyre log · pressures and tread"
        title="Tyres"
        plate={plate}
        pmeta={
          latest === undefined ? undefined : (
            <>
              Last checked <b>{dayMonth(latest.readingDate)}</b>
              <br />
              {lowest === Infinity
                ? 'no tread measured yet'
                : `lowest tread ${lowest.toFixed(1)} mm · MOT limit ${LEGAL_TREAD} mm`}
            </>
          )
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <h2 className="panel-title">The tyre log could not be loaded</h2>
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
          {latest !== undefined && (
            <Section>
              <Wrap>
                <SectionHead title="By corner" rule={<>the latest reading, laid out as the car</>} />
                <Panel>
                  <TyreCorners reading={latest} />
                </Panel>
              </Wrap>
            </Section>
          )}

          <Section last>
          <Wrap>
            <SectionHead
              title="Readings"
              rule={<>newest first</>}
              link={<Mark onClick={() => setEditing('new')}>Add reading</Mark>}
            />
            {data.length === 0 ? (
              <Panel>
                <p className="panel-empty">
                  No tyre readings yet. A reading records pressure and tread by corner — the spare included,
                  which is the one that never gets checked.
                </p>
              </Panel>
            ) : (
              <DataTable
                columns={columns}
                rows={[...data].reverse()}
                rowKey={(r) => r.id}
                label="Tyre readings, newest first"
                onRowClick={setEditing}
                rowLabel={(r) => `Edit the tyre reading on ${dayMonth(r.readingDate)} ${year(r.readingDate)}`}
              />
            )}
          </Wrap>
          </Section>
        </>
      )}

      <AddTyreSheet
        editing={editing}
        onClose={() => setEditing(null)}
        reg={reg}
        locationSuggestions={locationSuggestions}
        toolSuggestions={toolSuggestions}
      />
    </AppShell>
  )
}

const NUM_FIELDS = [
  'psiFrontLeft', 'psiFrontRight', 'psiRearLeft', 'psiRearRight', 'psiSpare',
  'treadFrontLeft', 'treadFrontRight', 'treadRearLeft', 'treadRearRight',
] as const

function AddTyreSheet({
  editing,
  onClose,
  reg,
  locationSuggestions,
  toolSuggestions,
}: {
  editing: TyreReading | 'new' | null
  onClose: () => void
  reg: string
  locationSuggestions: { value: string }[]
  toolSuggestions: { value: string }[]
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
    if (existing === null) {
      setV({ readingDate: todayIso() })
    } else {
      const seed: Record<string, string> = {
        readingDate: existing.readingDate,
        mileage: existing.mileage === null ? '' : String(existing.mileage),
        location: existing.location ?? '',
        tool: existing.tool ?? '',
        notes: existing.notes ?? '',
      }
      for (const f of NUM_FIELDS) {
        const val = existing[f]
        seed[f] = val === null ? '' : String(val)
      }
      setV(seed)
    }
    setErrors({})
  }

  // The one field the server can flag on a reading — anything else it returns falls to the footer banner.
  const FIELD_KEYS = ['readingdate'] as const

  const get = (k: string) => v[k] ?? ''
  const set = (k: string, value: string) => setV((p) => ({ ...p, [k]: value }))
  // Empty stays null. A blank pressure box is "I did not check that one", never 0 psi.
  const num = (k: string) => (get(k) === '' ? null : Number(get(k)))

  // A reading needs a date; every pressure and tread box is optional. Checked here so the answer is instant.
  const validate = (): FieldErrors => {
    const e: FieldErrors = {}
    if (get('readingDate') === '') e['readingdate'] = ['When were they checked?']
    return e
  }

  const submit = () => {
    const found = validate()
    setErrors(found)
    if (Object.keys(found).length === 0) mutation.mutate()
  }

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'tyres'] })
    await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'mileage'] })
    await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
  }

  const mutation = useMutation({
    mutationFn: async () => {
      const body = {
        readingDate: get('readingDate'),
        mileage: get('mileage') === '' ? null : Number(get('mileage').replace(/[\s,]/g, '')),
        psiFrontLeft: num('psiFrontLeft'),
        psiFrontRight: num('psiFrontRight'),
        psiRearLeft: num('psiRearLeft'),
        psiRearRight: num('psiRearRight'),
        psiSpare: num('psiSpare'),
        treadFrontLeft: num('treadFrontLeft'),
        treadFrontRight: num('treadFrontRight'),
        treadRearLeft: num('treadRearLeft'),
        treadRearRight: num('treadRearRight'),
        location: get('location') || null,
        tool: get('tool') || null,
        notes: get('notes') || null,
      }
      const result = await apiRequest<TyreReading>(
        existing === null
          ? `/api/vehicles/${encodeURIComponent(reg)}/tyres`
          : `/api/vehicles/${encodeURIComponent(reg)}/tyres/${existing.id}`,
        {
          method: existing === null ? 'POST' : 'PATCH',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body),
        },
      )
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async () => {
      await invalidate()
      toast(existing === null ? 'Tyre reading saved' : 'Tyre reading updated')
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
      const result = await apiRequest<null>(`/api/vehicles/${encodeURIComponent(reg)}/tyres/${existing.id}`, {
        method: 'DELETE',
      })
      if (!result.ok) throw new ApiFailure(result.error)
    },
    onSuccess: async () => {
      await invalidate()
      toast('Tyre reading deleted')
      setV({})
      setSeededFor(null)
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  return (
    <Sheet
      open={editing !== null}
      onClose={onClose}
      title={existing === null ? 'Add tyre reading' : 'Edit tyre reading'}
      subtitle="leave a box empty for anything you did not check"
      onSubmit={submit}
      footer={
        <>
          {existing !== null && (
            <ConfirmButton
              onConfirm={() => remove.mutate()}
              pending={remove.isPending}
              cascade={existing.mileage !== null ? 'with its reading' : undefined}
            />
          )}
          <Btn type="submit" onClick={() => {}}>
            {mutation.isPending ? 'Saving…' : existing === null ? 'Save reading' : 'Save changes'}
          </Btn>
        </>
      }
    >
      <Field label="Date" error={fieldError(errors, 'readingdate')}>
        {(p) => <input type="date" value={get('readingDate')} onChange={(e) => set('readingDate', e.target.value)} {...p} />}
      </Field>

      <Field label="Odometer" hint="optional — writes a mileage reading if given">
        {(p) => <input type="text" inputMode="numeric" placeholder="80,712" value={get('mileage')} onChange={(e) => set('mileage', e.target.value)} {...p} />}
      </Field>

      {CORNERS.map((c) => (
        <Field key={c.psi} label={`${c.label} psi`}>
          {(p) => <input type="text" inputMode="decimal" placeholder="35" value={get(c.psi)} onChange={(e) => set(c.psi, e.target.value)} {...p} />}
        </Field>
      ))}

      <Field label="Spare psi" hint="the one that never gets checked">
        {(p) => <input type="text" inputMode="decimal" placeholder="35" value={get('psiSpare')} onChange={(e) => set('psiSpare', e.target.value)} {...p} />}
      </Field>

      {CORNERS.map((c) => (
        <Field key={c.tread} label={`${c.label} tread mm`}>
          {(p) => <input type="text" inputMode="decimal" placeholder="6.0" value={get(c.tread)} onChange={(e) => set(c.tread, e.target.value)} {...p} />}
        </Field>
      ))}

      <Field label="Where">
        {(p) => <Combobox {...p} value={get('location')} onChange={(val) => set('location', val)} suggestions={locationSuggestions} placeholder="Home driveway" />}
      </Field>

      <Field label="Tool">
        {(p) => <Combobox {...p} value={get('tool')} onChange={(val) => set('tool', val)} suggestions={toolSuggestions} placeholder="Michelin digital gauge" />}
      </Field>

      <Field label="Notes" wide>
        {(p) => <input type="text" value={get('notes')} onChange={(e) => set('notes', e.target.value)} {...p} />}
      </Field>

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
