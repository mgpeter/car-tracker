import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { ConfirmButton } from '../components/ConfirmButton'
import { Absent, DataTable, Sub, type Column } from '../components/DataTable'
import { Kv } from '../components/Kv'
import { IntegrityPill, Pill } from '../components/Pill'
import { DateQuickFill } from '../components/DateQuickFill'
import { Field, Sheet } from '../components/Sheet'
import { useReferenceSuggestions } from '../api/reference'
import { Combobox } from '../components/Combobox'
import { addMonths, todayIso } from '../lib/date'
import { fieldError, formError, reportApiError, type FieldErrors } from '../lib/formErrors'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { AppLink } from '../lib/link'
import { usePlate } from '../lib/usePlate'
import { countdownText, renewalPresentation, type RenewalUrgency } from '../lib/renewal'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'

interface ServiceRecordItem {
  id: number
  serviceDate: string
  type: string
  mileage: number
  garage: string | null
  workDone: string | null
  partsReplaced: string | null
  cost: number | null
  nextDueDate: string | null
  nextDueMileage: number | null
  notes: string | null
}

interface Renewal {
  name: string
  expiryDate: string | null
  daysRemaining: number | null
  urgency: string | null
  source: string | null
}

interface ServiceLog {
  records: ServiceRecordItem[]
  mot: Renewal
  nextServiceDate: Renewal
  nextServiceMiles: number | null
}

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', minimumFractionDigits: 2 })

const dayMonth = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })

const year = (iso: string) => new Date(`${iso}T00:00:00`).getFullYear()

const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

/** The type the MOT expiry derives from, matched exactly. See the note on the sheet's type field. */
const MOT = 'MOT'

/** Common types, offered as choices. `Type` is free text, so these are a convenience, not a constraint. */
const TYPES = ['MOT', 'Service', 'Repair', 'Inspection', 'Recall', 'Tyres', 'Bodywork']

/**
 * Service-interval templates: how far ahead a type's next-due usually falls. Used only to pre-fill the add
 * sheet as an overridable suggestion — the stored record holds whatever is saved, never the template. Constants,
 * not a per-vehicle editable schedule (that would be its own spec). Types with no recurring cadence — Repair,
 * Recall, Tyres, Bodywork — carry no template and suggest nothing. Keyed on the exact `TYPES` strings, so a
 * suggestion and the MOT derivation agree on what a type is called.
 */
const SERVICE_INTERVALS: Record<string, { months?: number; miles?: number }> = {
  MOT: { months: 12 },
  Service: { months: 12, miles: 12_000 },
  Inspection: { months: 12 },
}

/**
 * The next-due a template suggests from the chosen type and the entered service date/mileage. Empty strings
 * where nothing can be suggested (unknown type, or the base date/mileage not yet entered) — a suggestion, never
 * an assertion.
 */
function suggestNextDue(type: string, serviceDate: string, mileageStr: string): { nextDueDate: string; nextDueMileage: string } {
  const template = SERVICE_INTERVALS[type]
  if (template === undefined) return { nextDueDate: '', nextDueMileage: '' }

  const nextDueDate = template.months !== undefined && serviceDate !== '' ? addMonths(serviceDate, template.months) : ''
  const miles = Number(mileageStr.replace(/[\s,]/g, ''))
  const nextDueMileage =
    template.miles !== undefined && mileageStr.trim() !== '' && Number.isFinite(miles) ? String(miles + template.miles) : ''
  return { nextDueDate, nextDueMileage }
}

/**
 * Service history — the screen two of the five defects were waiting for.
 *
 * The MOT expiry derives from the latest `Type = "MOT"` record's next-due date, so until this existed a
 * vehicle's MOT could only read "not set" no matter how many tests it had passed. And the workbook's 27 Jun
 * 2026 row logging 83,000 mi against a current 80,712 lives on this sheet: it is the row that trips the mileage
 * detector, and there was nowhere to type it.
 */
export function ServiceHistoryPage() {
  const reg = useVehicleReg()
  const plate = usePlate()
  const [editing, setEditing] = useState<ServiceRecordItem | 'new' | null>(null)

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['vehicle', reg, 'service'] as const,
    queryFn: async () => {
      const result = await apiRequest<ServiceLog>(`/api/vehicles/${encodeURIComponent(reg)}/service`)
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
  })

  const mot = data?.mot
  const motPresentation =
    mot === undefined ? null : renewalPresentation(mot.urgency as RenewalUrgency | null, mot.daysRemaining)

  // The record the countdown actually derives from. Naming it is the difference between "trust me" and "here".
  const motRecord = [...(data?.records ?? [])]
    .filter((r) => r.type === MOT && r.nextDueDate !== null)
    .sort((a, b) => (a.nextDueDate! < b.nextDueDate! ? 1 : -1))[0]

  const columns: Column<ServiceRecordItem>[] = [
    {
      key: 'date',
      label: 'Date',
      width: '70px',
      priority: 'essential',
      render: (r) => (
        <b>
          {dayMonth(r.serviceDate)}
          <Sub>{year(r.serviceDate)}</Sub>
        </b>
      ),
    },
    {
      key: 'type',
      label: 'Type',
      width: '90px',
      render: (r) => (
        <>
          {r.type}
          {/* The MOT row is the source of a dashboard figure, and saying so here is what makes the derivation
              legible rather than magic. */}
          {r.id === motRecord?.id && <Sub>derives the MOT expiry</Sub>}
        </>
      ),
    },
    {
      key: 'mileage',
      label: 'Odometer',
      width: '86px',
      align: 'right',
      render: (r) => r.mileage.toLocaleString('en-GB'),
    },
    {
      key: 'work',
      label: 'Work done',
      width: '1.4fr',
      priority: 'secondary',
      render: (r) => (
        <>
          {r.workDone ?? <Absent>not recorded</Absent>}
          {r.partsReplaced !== null && <Sub>{r.partsReplaced}</Sub>}
        </>
      ),
    },
    {
      key: 'garage',
      label: 'Garage',
      width: '1fr',
      priority: 'secondary',
      render: (r) => r.garage ?? <Absent>DIY</Absent>,
    },
    {
      key: 'next',
      label: 'Next due',
      width: '92px',
      priority: 'secondary',
      render: (r) =>
        r.nextDueDate === null && r.nextDueMileage === null ? (
          <Absent />
        ) : (
          <>
            {r.nextDueDate !== null ? dayMonth(r.nextDueDate) : '—'}
            {r.nextDueMileage !== null && <Sub>{r.nextDueMileage.toLocaleString('en-GB')} mi</Sub>}
          </>
        ),
    },
    {
      key: 'cost',
      label: 'Cost',
      width: '84px',
      align: 'right',
      priority: 'essential',
      render: (r) => (r.cost === null ? <Absent /> : <b>{money(r.cost)}</b>),
    },
  ]

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="service"
      center={{ kind: 'action', icon: 'plus', label: 'Add record', onClick: () => setEditing('new') }}
      footer={
        <>
          The MOT countdown on the dashboard is <b>derived from the latest MOT record here</b> — it is not a
          date anyone types. That is the whole reason this screen exists: the old spreadsheet stored its MOT
          expiry and showed a red 23-day countdown for a test that had already passed. Each record also writes
          an odometer reading and, when it cost something, mirrors into expenses.
        </>
      }
    >
      <PageHead
        eyebrow="Service history · computed live"
        title="Service"
        plate={plate}
        pmeta={
          motPresentation === null || mot === undefined ? undefined : (
            <>
              MOT <b>{mot.expiryDate === null ? 'no record yet' : shortDate(mot.expiryDate)}</b>
              <br />
              {mot.daysRemaining === null
                ? 'add an MOT record and this fills itself in'
                : `${countdownText(mot.daysRemaining)} · derived from the pass below`}
            </>
          )
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <h2 className="panel-title">The service history could not be loaded</h2>
              <p className="panel-empty">{error instanceof Error ? error.message : 'The request failed.'}</p>
              <button className="btn" type="button" onClick={() => void refetch()}>
                Try again
              </button>
            </Panel>
          </Wrap>
        </Section>
      ) : isPending || data === undefined || mot === undefined || motPresentation === null ? (
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
                title="Derived from these records"
                rule={<>nothing here is stored</>}
                link={
                  <AppLink className="sec-link" to="dashboard" reg={reg}>
                    Dashboard →
                  </AppLink>
                }
              />
              <Panel className="stats num">
                <Kv
                  label="MOT expires"
                  value={mot.expiryDate === null ? '—' : shortDate(mot.expiryDate)}
                  note={mot.source ?? 'no MOT record yet'}
                />
                <Kv
                  label="MOT countdown"
                  value={countdownText(mot.daysRemaining)}
                  note={motPresentation.label}
                />
                <Kv
                  label="Next service"
                  value={data.nextServiceDate.expiryDate === null ? '—' : shortDate(data.nextServiceDate.expiryDate)}
                  note={
                    data.nextServiceMiles !== null
                      ? `or in ${data.nextServiceMiles.toLocaleString('en-GB')} mi`
                      : 'whichever comes first'
                  }
                />
                <Kv label="Records" value={String(data.records.length)} note="each writes a mileage reading" />
              </Panel>

              {mot.expiryDate !== null && (
                <Panel className="attn attn-info">
                  <div>
                    <div className="attn-k">
                      <IntegrityPill>Derived</IntegrityPill>
                    </div>
                    <h3>The MOT expiry is computed, not stored</h3>
                    <p>
                      {shortDate(mot.expiryDate)} comes from the {MOT} record
                      {motRecord !== undefined && ` dated ${shortDate(motRecord.serviceDate)} at ${motRecord.mileage.toLocaleString('en-GB')} mi`}
                      . The workbook stored this figure instead and drifted: it read 6 Aug 2026, 23 days, red —
                      a countdown for a test that had already passed. Log a newer pass and this moves on its
                      own.
                    </p>
                  </div>
                </Panel>
              )}
            </Wrap>
          </Section>

          <Section last>
            <Wrap>
              <SectionHead
                title="Records"
                rule={<>newest first</>}
                link={<Mark onClick={() => setEditing('new')}>Add record</Mark>}
              />
              {data.records.length === 0 ? (
                <Panel>
                  <p className="panel-empty">
                    No service records yet. The MOT countdown on the dashboard derives from an {MOT} record
                    here, so until one exists it reads "Not set" — which is honest, and is why there is a seed
                    in settings for the meantime.
                  </p>
                </Panel>
              ) : (
                <DataTable
                  columns={columns}
                  rows={[...data.records].reverse()}
                  rowKey={(r) => r.id}
                  label="Service records, newest first"
                  onRowClick={setEditing}
                  rowLabel={(r) => `Edit the ${r.type} record on ${shortDate(r.serviceDate)}`}
                />
              )}
            </Wrap>
          </Section>
        </>
      )}

      <AddServiceSheet editing={editing} onClose={() => setEditing(null)} reg={reg} />
    </AppShell>
  )
}

interface AnomalyFlag {
  id: number
  message: string
}

function AddServiceSheet({
  editing,
  onClose,
  reg,
}: {
  editing: ServiceRecordItem | 'new' | null
  onClose: () => void
  reg: string
}) {
  const existing = editing !== 'new' && editing !== null ? editing : null
  const [v, setV] = useState<Record<string, string>>({})
  const [errors, setErrors] = useState<FieldErrors>({})
  // Once the owner edits next-due, the template stops suggesting — their value is theirs. An edit of an existing
  // record starts touched, so its stored next-due is never overwritten by a template.
  const [nextDueTouched, setNextDueTouched] = useState(false)
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const [seededFor, setSeededFor] = useState<number | 'new' | null>(null)
  const key = existing?.id ?? (editing === 'new' ? ('new' as const) : null)
  if (key !== null && key !== seededFor) {
    setSeededFor(key)
    setNextDueTouched(existing !== null)
    setV(
      existing === null
        ? { serviceDate: todayIso() }
        : {
            serviceDate: existing.serviceDate,
            type: existing.type,
            mileage: String(existing.mileage),
            garage: existing.garage ?? '',
            workDone: existing.workDone ?? '',
            partsReplaced: existing.partsReplaced ?? '',
            cost: existing.cost === null ? '' : String(existing.cost),
            nextDueDate: existing.nextDueDate ?? '',
            nextDueMileage: existing.nextDueMileage === null ? '' : String(existing.nextDueMileage),
            notes: existing.notes ?? '',
          },
    )
    setErrors({})
  }

  const get = (k: string) => v[k] ?? ''
  const set = (k: string, value: string) =>
    setV((p) => {
      const next = { ...p, [k]: value }
      // Pre-fill next-due from the template when the type or the base date/mileage changes — a suggestion the
      // owner can accept or overwrite, never an automatic write to the stored record.
      if ((k === 'type' || k === 'serviceDate' || k === 'mileage') && !nextDueTouched) {
        const s = suggestNextDue(next.type ?? '', next.serviceDate ?? '', next.mileage ?? '')
        next.nextDueDate = s.nextDueDate
        next.nextDueMileage = s.nextDueMileage
      }
      return next
    })
  // The owner touched next-due directly, so stop suggesting and keep what they typed.
  const setNextDue = (k: string, value: string) => {
    setNextDueTouched(true)
    setV((p) => ({ ...p, [k]: value }))
  }
  const isMot = get('type') === MOT
  const garageSuggestions = useReferenceSuggestions('garages')

  // The fields the server can flag on a service record — anything else it returns falls to the footer banner.
  const FIELD_KEYS = ['type', 'mileage'] as const

  // Checked here so the answer is instant and beside the field; the server validates independently.
  const validate = (): FieldErrors => {
    const e: FieldErrors = {}
    if (get('type').trim() === '') e['type'] = ['Which service?']
    const mileage = Number(get('mileage').replace(/[\s,]/g, ''))
    if (!Number.isFinite(mileage) || mileage <= 0) e['mileage'] = ['The odometer at this service.']
    return e
  }

  const submit = () => {
    const found = validate()
    setErrors(found)
    if (Object.keys(found).length === 0) mutation.mutate()
  }

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'service'] })
    await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'mileage'] })
    await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'expenses'] })
    await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'anomalies'] })
    await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
    await queryClient.invalidateQueries({ queryKey: queryKeys.garage })
  }

  const mutation = useMutation({
    mutationFn: async () => {
      const body = {
        serviceDate: get('serviceDate'),
        type: get('type'),
        mileage: Number(get('mileage').replace(/[\s,]/g, '')),
        garage: get('garage') || null,
        workDone: get('workDone') || null,
        partsReplaced: get('partsReplaced') || null,
        cost: get('cost') === '' ? null : Number(get('cost')),
        nextDueDate: get('nextDueDate') || null,
        nextDueMileage: get('nextDueMileage') === '' ? null : Number(get('nextDueMileage').replace(/[\s,]/g, '')),
        notes: get('notes') || null,
      }
      const result = await apiRequest<{ id: number; flags?: AnomalyFlag[] }>(
        existing === null
          ? `/api/vehicles/${encodeURIComponent(reg)}/service`
          : `/api/vehicles/${encodeURIComponent(reg)}/service/${existing.id}`,
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
      const flags = res.flags ?? []
      toast(
        flags.length > 0
          ? `Record saved · ${flags[0]!.message} · flagged, not refused`
          : existing === null
            ? 'Record saved · the odometer and any countdowns recomputed'
            : 'Record updated · the odometer, countdowns and expense mirror recomputed',
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
      const result = await apiRequest<null>(`/api/vehicles/${encodeURIComponent(reg)}/service/${existing.id}`, {
        method: 'DELETE',
      })
      if (!result.ok) throw new ApiFailure(result.error)
    },
    onSuccess: async () => {
      await invalidate()
      toast('Record deleted · its mileage reading and any mirrored expense went with it')
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
      title={existing === null ? 'Add service record' : 'Edit service record'}
      subtitle="writes a mileage reading; an MOT drives the countdown"
      onSubmit={submit}
      footer={
        <>
          {existing !== null && (
            <ConfirmButton
              onConfirm={() => remove.mutate()}
              pending={remove.isPending}
              cascade={existing.cost !== null ? 'with its reading & expense' : 'with its reading'}
            />
          )}
          <Btn type="submit" onClick={() => {}}>
            {mutation.isPending ? 'Saving…' : existing === null ? 'Save record' : 'Save changes'}
          </Btn>
        </>
      }
    >
      <Field label="Date">
        {(p) => <input type="date" value={get('serviceDate')} onChange={(e) => set('serviceDate', e.target.value)} {...p} />}
      </Field>

      <Field label="Type" error={fieldError(errors, 'type')} hint='"MOT" is matched exactly — it is what the expiry derives from'>
        {(p) => (
          // A choice, not a text box. `ServiceRecord.Type` is free text by design (it holds the workbook's
          // varied descriptions), and the MOT derivation matches "MOT" exactly — so a record typed "MOT test"
          // or "mot" derives no expiry at all and fails silently. Offering the string removes that trap.
          <select value={get('type')} onChange={(e) => set('type', e.target.value)} {...p}>
            <option value="">Choose…</option>
            {TYPES.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </select>
        )}
      </Field>

      <Field label="Odometer" error={fieldError(errors, 'mileage')} hint="writes a mileage reading, like every other log">
        {(p) => <input type="text" inputMode="numeric" placeholder="80,705" value={get('mileage')} onChange={(e) => set('mileage', e.target.value)} {...p} />}
      </Field>

      <Field label="Garage" hint="leave empty for DIY">
        {(p) => (
          <Combobox
            {...p}
            value={get('garage')}
            onChange={(val) => set('garage', val)}
            suggestions={garageSuggestions}
            placeholder="K & P Motors"
          />
        )}
      </Field>

      <Field
        label={isMot ? 'MOT expires' : 'Next due'}
        hint={
          isMot
            ? 'this is the dashboard countdown'
            : SERVICE_INTERVALS[get('type')]?.months !== undefined && !nextDueTouched
              ? 'suggested from the type — change it if the interval differs'
              : 'optional'
        }
      >
        {(p) => (
          <>
            <input type="date" value={get('nextDueDate')} onChange={(e) => setNextDue('nextDueDate', e.target.value)} {...p} />
            {!isMot && <DateQuickFill base={get('serviceDate')} onPick={(iso) => setNextDue('nextDueDate', iso)} />}
          </>
        )}
      </Field>

      <Field label="Next due at" hint="miles — whichever comes first">
        {(p) => <input type="text" inputMode="numeric" placeholder="87,500" value={get('nextDueMileage')} onChange={(e) => setNextDue('nextDueMileage', e.target.value)} {...p} />}
      </Field>

      <Field label="Cost £" hint="mirrors into expenses; leave empty for none">
        {(p) => <input type="text" inputMode="decimal" placeholder="603.99" value={get('cost')} onChange={(e) => set('cost', e.target.value)} {...p} />}
      </Field>

      <Field label="Work done" wide>
        {(p) => <input type="text" placeholder="Cambelt and water pump" value={get('workDone')} onChange={(e) => set('workDone', e.target.value)} {...p} />}
      </Field>

      <Field label="Parts replaced" wide>
        {(p) => <input type="text" value={get('partsReplaced')} onChange={(e) => set('partsReplaced', e.target.value)} {...p} />}
      </Field>

      <Field label="Notes" wide>
        {(p) => <input type="text" placeholder="advisories: headlamp lens, rear tyres" value={get('notes')} onChange={(e) => set('notes', e.target.value)} {...p} />}
      </Field>

      {isMot && (
        <div className="field wide">
          <span className="hint hint-info">
            <Pill tone="ok">Derived</Pill> The date above becomes the dashboard's MOT countdown, computed at
            render. It is never stored as a countdown, which is what the old spreadsheet did.
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
