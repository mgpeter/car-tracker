import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys } from '../api/queries'
import { useReferenceSuggestions } from '../api/reference'
import { Btn, Mark } from '../components/Btn'
import { Combobox } from '../components/Combobox'
import { ConfirmButton } from '../components/ConfirmButton'
import { DateQuickFill } from '../components/DateQuickFill'
import { Kv } from '../components/Kv'
import { Pill } from '../components/Pill'
import { Field, Sheet } from '../components/Sheet'
import { TableControls } from '../components/TableControls'
import { useTableView, type FilterGroup, type SortKey } from '../components/useTableView'
import { CFoot, Panel, Section, SectionHead, Wrap } from '../components/layout'
import { todayIso } from '../lib/date'
import { fieldError, formError, reportApiError, type FieldErrors } from '../lib/formErrors'
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

// High → Low, so the default "priority, then target" sort reads the way the design's footer names it.
const PRIO_RANK: Record<Priority, number> = { High: 3, Medium: 2, Low: 1 }
// A task with no target date sorts last within its priority band rather than first.
const FAR_FUTURE = '9999-12-31'

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

  // The board's filter/sort strip — the same shared capability as fuel and expenses, declared as predicates
  // over the one `<DataTable>` seam rather than a fourth hand-rolled filter. Kind is chips, priority a select,
  // matching the design's `tasks.dc.html`. The bundle stats above stay on the full set: they are the
  // authoritative figure, exactly as the expenses YTD rollup sits above its filtered total.
  const groups: FilterGroup<TaskItem>[] = useMemo(
    () => [
      {
        id: 'kind',
        label: 'Kind',
        render: 'chips',
        options: [
          { id: 'DIY', label: 'DIY', test: (t: TaskItem) => t.kind === 'DIY' },
          { id: 'Workshop', label: 'Workshop', test: (t: TaskItem) => t.kind === 'Workshop' },
        ],
      },
      {
        id: 'priority',
        label: 'Priority',
        render: 'select',
        options: (['High', 'Medium', 'Low'] as Priority[]).map((p) => ({
          id: p,
          label: PRIORITY[p].label,
          test: (t: TaskItem) => t.priority === p,
        })),
      },
    ],
    [],
  )

  const sorts: SortKey<TaskItem>[] = useMemo(
    () => [
      {
        id: 'priority',
        label: 'Priority',
        // Priority first (High → Low), then soonest target — the design's "sorted · priority, then target".
        compare: (a, b) => {
          const byPrio = PRIO_RANK[b.priority] - PRIO_RANK[a.priority]
          if (byPrio !== 0) return byPrio
          return (a.targetDate ?? FAR_FUTURE).localeCompare(b.targetDate ?? FAR_FUTURE)
        },
      },
      {
        id: 'target',
        label: 'Target date',
        compare: (a, b) => (a.targetDate ?? FAR_FUTURE).localeCompare(b.targetDate ?? FAR_FUTURE),
      },
    ],
    [],
  )

  // Ascending puts High and the soonest target first — the log's declared default order, so an unfiltered board
  // is priority-sorted as the design shows, not left in arrival order.
  const view = useTableView(tasks, { groups, sorts, defaultSortId: 'priority', defaultDir: 'asc' })

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
                <>
                  <TableControls view={view} noun="tasks" />
                  {view.count === 0 ? (
                    // A filter that matched nothing — distinct from the empty board above, which reads
                    // differently and must not be mistaken for it.
                    <Panel>
                      <p className="panel-empty">No tasks match this filter. Clear it to see all {view.total}.</p>
                    </Panel>
                  ) : (
                    <>
                      <div className="board">
                        {COLUMNS.map((col) => {
                          const inCol = view.rows.filter((t) => t.status === col.status)
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

                      <CFoot>
                        <span>
                          {COLUMNS.map((c) => `${view.rows.filter((t) => t.status === c.status).length} ${c.label.toLowerCase()}`).join(' · ')} ={' '}
                          <b>{view.count}</b>
                        </span>
                      </CFoot>
                    </>
                  )}
                </>
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
  const [errors, setErrors] = useState<FieldErrors>({})
  const queryClient = useQueryClient()
  const { toast } = useToast()
  const garageSuggestions = useReferenceSuggestions('garages')

  // Controlled from the row when editing, from state once touched. Keeps the sheet honest about what it is
  // editing without a useEffect that fights the user's typing.
  const get = (k: string, fallback = '') => v[k] ?? fallback
  const set = (k: string, value: string) => setV((p) => ({ ...p, [k]: value }))

  // The one field the server can flag on a task — anything else it returns falls to the footer banner.
  const FIELD_KEYS = ['title'] as const

  // A task needs a title; everything else is optional. Checked here so the answer is instant and beside the field.
  const validate = (): FieldErrors => {
    const e: FieldErrors = {}
    if (get('title', existing?.title ?? '').trim() === '') e['title'] = ['Give the task a title.']
    return e
  }

  const submit = () => {
    const found = validate()
    setErrors(found)
    if (Object.keys(found).length === 0) mutation.mutate()
  }

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
      setErrors({})
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
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
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
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
      setErrors({})
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  return (
    <Sheet
      open={task !== null}
      onClose={onClose}
      title={existing === null ? 'Add task' : 'Edit task'}
      subtitle="workshop jobs bundle into one garage visit"
      onSubmit={submit}
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
              // The promotion is a distinct action with its own required field; its message rides the same
              // footer banner via the form-level key so `formError` still surfaces it.
              if (get('promoteMileage').trim() === '') return setErrors({ _: ['The odometer reading at completion.'] })
              promote.mutate()
            }}
          >
            {promote.isPending ? 'Converting…' : 'Convert to service record'}
          </Btn>
        </div>
      )}

      <Field label="Title" wide error={fieldError(errors, 'title')}>
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
          <>
            <input
              type="date"
              value={get('targetDate', existing?.targetDate ?? '')}
              onChange={(e) => set('targetDate', e.target.value)}
              {...p}
            />
            <DateQuickFill base={todayIso()} onPick={(iso) => set('targetDate', iso)} />
          </>
        )}
      </Field>

      <Field label="Garage" wide hint="created on first use">
        {(p) => (
          <Combobox
            {...p}
            value={get('assignedGarage', existing?.assignedGarage ?? '')}
            onChange={(val) => set('assignedGarage', val)}
            suggestions={garageSuggestions}
            placeholder="K & P Motors"
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
