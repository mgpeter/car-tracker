import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
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

  return (
    <Sheet
      open={task !== null}
      onClose={onClose}
      title={existing === null ? 'Add task' : 'Edit task'}
      subtitle="workshop jobs bundle into one garage visit"
      onSubmit={() => mutation.mutate()}
      footer={
        <Btn type="submit" onClick={() => {}}>
          {mutation.isPending ? 'Saving…' : 'Save task'}
        </Btn>
      }
    >
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
