import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { ConfirmButton } from '../components/ConfirmButton'
import { Kv } from '../components/Kv'
import { Pill } from '../components/Pill'
import { Field, Sheet } from '../components/Sheet'
import { CFoot, Panel, Section, SectionHead, Wrap } from '../components/layout'
import { AppLink } from '../lib/link'
import { usePlate } from '../lib/usePlate'
import { PRIORITY, type Priority } from '../lib/status'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'

type Kind = 'DIY' | 'Workshop'
type Status = 'Open' | 'InProgress' | 'Scheduled' | 'Done'

interface TaskItem {
  id: number
  kind: Kind
  priority: Priority
  title: string
  description: string | null
  estimatedCost: number | null
  status: Status
  targetDate: string | null
  targetService: string | null
  completedDate: string | null
  assignedGarage: string | null
  serviceRecordId: number | null
  notes: string | null
}

interface TaskLog {
  tasks: TaskItem[]
  bundleCost: number
  bundleCount: number
  openEstimateTotal: number
}

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', maximumFractionDigits: 0 })

const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })

/**
 * The columns. `Record<Status, …>` off the wire enum, so a fifth status fails the build here rather than
 * dropping its tasks off the board silently.
 */
const COLUMNS: { status: Status; label: string; help: string }[] = [
  { status: 'Open', label: 'Open', help: 'not started' },
  { status: 'InProgress', label: 'In progress', help: 'started' },
  { status: 'Scheduled', label: 'Scheduled', help: 'booked in' },
  { status: 'Done', label: 'Done', help: 'finished' },
]

/**
 * Tasks — the workbook's DIY To-Do and Workshop To-Do sheets, which are one list.
 *
 * Two sheets is why the same job ends up on whichever one someone opened. `Kind` is a column rather than a
 * table because "do it myself or pay someone" is a property of a task, and tasks move between them once you
 * price the job.
 *
 * **The bundle total is derived.** The design shows "Bundle for next garage visit → £150 · 1 job" as a
 * hardcoded string; it is the sum of the open Workshop estimates, and its whole value is that it moves when you
 * add a task — the question it answers is "is it worth booking the visit yet".
 */
export function TasksPage() {
  const reg = useVehicleReg()
  const plate = usePlate()
  const [editing, setEditing] = useState<TaskItem | 'new' | null>(null)

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['vehicle', reg, 'tasks'] as const,
    queryFn: async () => {
      const result = await apiRequest<TaskLog>(`/api/vehicles/${encodeURIComponent(reg)}/tasks`)
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
  })

  const tasks = data?.tasks ?? []
  const open = tasks.filter((t) => t.status !== 'Done')

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="tasks"
      center={{ kind: 'action', icon: 'plus', label: 'Add task', onClick: () => setEditing('new') }}
      footer={
        <>
          DIY and Workshop are one list with a <b>kind</b>, not two sheets. The workbook keeps them apart, which
          is how the same job ends up on whichever one you happened to open — and tasks move between them the
          moment you price the work. The bundle total is computed from the open Workshop estimates.
        </>
      }
    >
      <PageHead
        eyebrow="Tasks · DIY and workshop"
        title="Tasks"
        plate={plate}
        pmeta={
          data === undefined ? undefined : (
            <>
              <b>{open.length} open</b> · {data.bundleCount} for the garage
              <br />
              Bundle <b>{money(data.bundleCost)}</b> · worst case {money(data.openEstimateTotal)}
            </>
          )
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <h2 className="panel-title">The tasks could not be loaded</h2>
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
                title="Bundle"
                rule={<>what the next garage visit is worth</>}
                link={
                  <AppLink className="sec-link" to="service" reg={reg}>
                    Service history →
                  </AppLink>
                }
              />
              <Panel className="stats num">
                <Kv
                  label="Bundle"
                  value={money(data.bundleCost)}
                  note={`${data.bundleCount} workshop job${data.bundleCount === 1 ? '' : 's'} waiting`}
                />
                <Kv label="Worst case" value={money(data.openEstimateTotal)} note="every open task, DIY included" />
                <Kv label="Open" value={String(open.length)} note="not done" />
                <Kv
                  label="High priority"
                  value={String(open.filter((t) => t.priority === 'High').length)}
                  // The design renders only Medium and Low, and ships a `.prio.crit` rule nothing uses — so
                  // High, the domain's most important priority, has no rendering in it at all.
                  note="the design has no rendering for these"
                />
              </Panel>
            </Wrap>
          </Section>

          <Section last>
            <Wrap>
              <SectionHead
                title="Board"
                rule={<>grouped by status</>}
                link={<Mark onClick={() => setEditing('new')}>Add task</Mark>}
              />
              {tasks.length === 0 ? (
                <Panel>
                  <p className="panel-empty">
                    No tasks yet. A task is work you intend to do — an{' '}
                    <AppLink to="issues" reg={reg}>
                      issue
                    </AppLink>{' '}
                    is something you are watching and have not decided about.
                  </p>
                </Panel>
              ) : (
                <div className="board">
                  {COLUMNS.map((col) => {
                    const inCol = tasks.filter((t) => t.status === col.status)
                    return (
                      <Panel key={col.status} className="bcol">
                        <div className="bhead">
                          <span className="bname">{col.label}</span>
                          <span className="bcount num">{inCol.length}</span>
                        </div>
                        {inCol.length === 0 ? (
                          <p className="bempty">nothing {col.help}</p>
                        ) : (
                          <ul className="blist">
                            {inCol.map((t) => (
                              <li key={t.id}>
                                <button className="bcard" type="button" onClick={() => setEditing(t)}>
                                  <span className="bcard-top">
                                    <Pill tone={PRIORITY[t.priority].tone}>{PRIORITY[t.priority].label}</Pill>
                                    <span className="bkind">{t.kind}</span>
                                  </span>
                                  <span className="btitle">{t.title}</span>
                                  <span className="bmeta num">
                                    {t.estimatedCost !== null && <b>{money(t.estimatedCost)}</b>}
                                    {t.targetDate !== null && <> · target {shortDate(t.targetDate)}</>}
                                    {t.assignedGarage !== null && <> · {t.assignedGarage}</>}
                                  </span>
                                </button>
                              </li>
                            ))}
                          </ul>
                        )}
                      </Panel>
                    )
                  })}
                </div>
              )}

              {tasks.length > 0 && (
                <CFoot>
                  <span>
                    {COLUMNS.map((c) => `${tasks.filter((t) => t.status === c.status).length} ${c.label.toLowerCase()}`).join(' · ')} ={' '}
                    <b>{tasks.length}</b>
                  </span>
                </CFoot>
              )}
            </Wrap>
          </Section>
        </>
      )}

      <TaskSheet task={editing} onClose={() => setEditing(null)} reg={reg} />
    </AppShell>
  )
}

function TaskSheet({ task, onClose, reg }: { task: TaskItem | 'new' | null; onClose: () => void; reg: string }) {
  const existing = task !== 'new' && task !== null ? task : null
  const [v, setV] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const { toast } = useToast()

  // Controlled from the row when editing, from state once touched. Keeps the sheet honest about what it is
  // editing without a useEffect that fights the user's typing.
  const get = (k: string, fallback = '') => v[k] ?? fallback
  const set = (k: string, value: string) => setV((p) => ({ ...p, [k]: value }))

  const mutation = useMutation({
    mutationFn: async () => {
      const body = {
        title: get('title', existing?.title ?? ''),
        kind: get('kind', existing?.kind ?? 'DIY'),
        priority: get('priority', existing?.priority ?? 'Medium'),
        status: get('status', existing?.status ?? 'Open'),
        description: get('description', existing?.description ?? '') || null,
        estimatedCost: get('estimatedCost', existing?.estimatedCost?.toString() ?? '') === ''
          ? null
          : Number(get('estimatedCost', existing?.estimatedCost?.toString() ?? '')),
        targetDate: get('targetDate', existing?.targetDate ?? '') || null,
        assignedGarage: get('assignedGarage', existing?.assignedGarage ?? '') || null,
        notes: get('notes', existing?.notes ?? '') || null,
      }
      const result = await apiRequest<TaskItem>(
        existing === null
          ? `/api/vehicles/${encodeURIComponent(reg)}/tasks`
          : `/api/vehicles/${encodeURIComponent(reg)}/tasks/${existing.id}`,
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
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'tasks'] })
      await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
      toast(existing === null ? 'Task added · the bundle recomputed' : 'Task saved · the bundle recomputed')
      setV({})
      setError(null)
      onClose()
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Could not save.'),
  })

  const remove = useMutation({
    mutationFn: async () => {
      if (existing === null) return
      const result = await apiRequest<null>(`/api/vehicles/${encodeURIComponent(reg)}/tasks/${existing.id}`, {
        method: 'DELETE',
      })
      if (!result.ok) throw new ApiFailure(result.error)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'tasks'] })
      await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
      toast('Task deleted · the bundle recomputed')
      setV({})
      onClose()
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Could not delete.'),
  })

  // README §3.3: a done Workshop job becomes a service record. Offered only where it can succeed — Workshop,
  // Done, and not already promoted — because a button you can see but never use is worse than one that is absent.
  const promotable = existing !== null && existing.kind === 'Workshop' && existing.status === 'Done' && existing.serviceRecordId === null
  const promoted = existing !== null && existing.serviceRecordId !== null

  const promote = useMutation({
    mutationFn: async () => {
      if (existing === null) return
      const body = {
        mileage: Number(get('promoteMileage').replace(/[\s,]/g, '')),
        type: get('promoteType', 'Service'),
        cost: get('promoteCost', existing.estimatedCost?.toString() ?? '') === ''
          ? null
          : Number(get('promoteCost', existing.estimatedCost?.toString() ?? '')),
      }
      const result = await apiRequest<{ serviceRecordId: number }>(
        `/api/vehicles/${encodeURIComponent(reg)}/tasks/${existing.id}/promote`,
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) },
      )
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async () => {
      // The record, its mileage reading and its mirrored expense all landed — recompute everything they touch.
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'tasks'] })
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'service'] })
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'expenses'] })
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'mileage'] })
      await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
      toast('Service record created — date, mileage, garage and cost carried over. Review in Service history.')
      setV({})
      setError(null)
      onClose()
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Could not convert.'),
  })

  return (
    <Sheet
      open={task !== null}
      onClose={onClose}
      title={existing === null ? 'Add task' : 'Edit task'}
      subtitle="workshop jobs bundle into one garage visit"
      onSubmit={() => mutation.mutate()}
      footer={
        <>
          {existing !== null && (
            <ConfirmButton onConfirm={() => remove.mutate()} pending={remove.isPending} />
          )}
          <Btn type="submit" onClick={() => {}}>
            {mutation.isPending ? 'Saving…' : 'Save task'}
          </Btn>
        </>
      }
    >
      {promoted && (
        <div className="field wide">
          <span className="hint hint-info">
            <Pill tone="ok">Converted</Pill> This job became a service record.{' '}
            <AppLink to="service" reg={reg}>
              Open in service history →
            </AppLink>
          </span>
        </div>
      )}

      {promotable && (
        // The done Workshop job, one click from a service record. Mileage is asked because a task carries no
        // reading; cost defaults to the estimate but is editable, because an estimate is not a receipt.
        <div className="field wide" style={{ borderBottom: '1px dashed var(--line-strong)', paddingBottom: 12, marginBottom: 4 }}>
          <span className="hint" style={{ marginBottom: 8, display: 'block' }}>
            <b>This job is done.</b> Convert it to a service record — its date, garage and cost carry over.
          </span>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
            <label className="minifield">
              <span>Odometer</span>
              <input type="text" inputMode="numeric" placeholder="80,712" value={get('promoteMileage')} onChange={(e) => set('promoteMileage', e.target.value)} />
            </label>
            <label className="minifield">
              <span>Type</span>
              <select value={get('promoteType', 'Service')} onChange={(e) => set('promoteType', e.target.value)}>
                <option value="Service">Service</option>
                <option value="Repair">Repair</option>
                <option value="MOT">MOT</option>
                <option value="Inspection">Inspection</option>
              </select>
            </label>
            <label className="minifield">
              <span>Cost £</span>
              <input type="text" inputMode="decimal" placeholder="603.99" value={get('promoteCost', existing?.estimatedCost?.toString() ?? '')} onChange={(e) => set('promoteCost', e.target.value)} />
            </label>
          </div>
          <Btn
            variant="ghost"
            onClick={() => {
              if (get('promoteMileage').trim() === '') return setError('The odometer reading at completion.')
              promote.mutate()
            }}
          >
            {promote.isPending ? 'Converting…' : 'Convert to service record'}
          </Btn>
        </div>
      )}

      <Field label="Title" wide>
        {(p) => (
          <input
            type="text"
            placeholder="Replace gear shift knob and gaiter"
            value={get('title', existing?.title ?? '')}
            onChange={(e) => set('title', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Kind" hint="workshop jobs bundle; DIY ones do not">
        {(p) => (
          <select value={get('kind', existing?.kind ?? 'DIY')} onChange={(e) => set('kind', e.target.value)} {...p}>
            <option value="DIY">DIY</option>
            <option value="Workshop">Workshop</option>
          </select>
        )}
      </Field>

      <Field label="Priority">
        {(p) => (
          <select
            value={get('priority', existing?.priority ?? 'Medium')}
            onChange={(e) => set('priority', e.target.value)}
            {...p}
          >
            {/* High is here even though the design never renders it — it ships a `.prio.crit` rule nothing
                uses, so its most important priority has no representation at all. */}
            <option value="High">High</option>
            <option value="Medium">Medium</option>
            <option value="Low">Low</option>
          </select>
        )}
      </Field>

      <Field label="Status">
        {(p) => (
          <select
            value={get('status', existing?.status ?? 'Open')}
            onChange={(e) => set('status', e.target.value)}
            {...p}
          >
            {COLUMNS.map((c) => (
              <option key={c.status} value={c.status}>
                {c.label}
              </option>
            ))}
          </select>
        )}
      </Field>

      <Field label="Estimated cost £" hint="what the bundle sums">
        {(p) => (
          <input
            type="text"
            inputMode="decimal"
            placeholder="120"
            value={get('estimatedCost', existing?.estimatedCost?.toString() ?? '')}
            onChange={(e) => set('estimatedCost', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Target date">
        {(p) => (
          <input
            type="date"
            value={get('targetDate', existing?.targetDate ?? '')}
            onChange={(e) => set('targetDate', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Garage" wide hint="created on first use">
        {(p) => (
          <input
            type="text"
            placeholder="K & P Motors"
            value={get('assignedGarage', existing?.assignedGarage ?? '')}
            onChange={(e) => set('assignedGarage', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Notes" wide>
        {(p) => (
          <input
            type="text"
            value={get('notes', existing?.notes ?? '')}
            onChange={(e) => set('notes', e.target.value)}
            {...p}
          />
        )}
      </Field>

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
