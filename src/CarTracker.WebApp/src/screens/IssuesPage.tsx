import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../api/client'
import { ApiFailure } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { ConfirmButton } from '../components/ConfirmButton'
import { Kv } from '../components/Kv'
import { Pill } from '../components/Pill'
import { Field, Sheet } from '../components/Sheet'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { AppLink } from '../lib/link'
import { usePlate } from '../lib/usePlate'
import type { PillTone } from '../lib/status'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'

type Severity = 'Critical' | 'Medium' | 'Low'
type Status = 'Monitoring' | 'Resolved'

interface IssueItem {
  id: number
  title: string
  severity: Severity
  firstNoted: string
  lastChecked: string | null
  currentObservation: string | null
  actionIfWorsens: string | null
  estimatedFixCost: number | null
  status: Status
  resolvedDate: string | null
  notes: string | null
}

interface IssueLog {
  issues: IssueItem[]
  monitoringCount: number
  resolvedCount: number
  worstCaseCost: number
}

/**
 * How pressing an issue is.
 *
 * `Record<Severity, …>` off the wire enum, and note this is **not** `DueStatus`: an issue's severity is how bad
 * the thing is, not whether it is late. It reuses the same tones because a reader should not have to learn two
 * colour languages, but the labels are its own.
 */
const SEVERITY: Record<Severity, { label: string; tone: PillTone }> = {
  Critical: { label: 'Critical', tone: 'due' },
  Medium: { label: 'Medium', tone: 'soon' },
  Low: { label: 'Low', tone: 'plain' },
}

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', maximumFractionDigits: 0 })

const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

/** Whole months since a date, for "watched for 26 months". Derived, because it changes every month. */
const monthsSince = (iso: string, asOf: Date) => {
  const then = new Date(`${iso}T00:00:00`)
  return Math.max(0, (asOf.getFullYear() - then.getFullYear()) * 12 + (asOf.getMonth() - then.getMonth()))
}

/**
 * The issues watchlist.
 *
 * **An issue is not a task**, and the distinction is the reason this screen exists. A task is work someone
 * intends to do. An issue is an observation being watched — "brake pipe corrosion, advisory since 2024, not
 * failure yet" — where the decision has deliberately not been made. Collapsing them loses the thing a watchlist
 * is for: noticing that something has been getting slowly worse for two years.
 *
 * That is why the entity has `LastChecked`, `CurrentObservation` and `ActionIfWorsens` and a task has no
 * equivalent. The last of those is the most valuable field on the screen: it is the decision made calmly in
 * advance, so it is not made in a hurry at the roadside.
 */
export function IssuesPage() {
  const reg = useVehicleReg()
  const plate = usePlate()
  const [editing, setEditing] = useState<IssueItem | 'new' | null>(null)
  const [showResolved, setShowResolved] = useState(false)

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['vehicle', reg, 'issues'] as const,
    queryFn: async () => {
      const result = await apiRequest<IssueLog>(`/api/vehicles/${encodeURIComponent(reg)}/issues`)
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
  })

  const asOf = new Date()
  const monitoring = (data?.issues ?? []).filter((i) => i.status === 'Monitoring')
  const resolved = (data?.issues ?? []).filter((i) => i.status === 'Resolved')
  const shown = showResolved ? [...monitoring, ...resolved] : monitoring

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="issues"
      center={{ kind: 'action', icon: 'plus', label: 'Add issue', onClick: () => setEditing('new') }}
      footer={
        <>
          An issue is something you are <b>watching</b>, not work you have decided to do — that is a{' '}
          <b>task</b>. The difference is what a watchlist is for: noticing that an advisory has been getting
          slowly worse for two years. "If it worsens" is the decision made in advance, so it is not made in a
          hurry at the roadside.
        </>
      }
    >
      <PageHead
        eyebrow="Issues · watchlist"
        title="Issues"
        plate={plate}
        pmeta={
          data === undefined ? undefined : (
            <>
              <b>{data.monitoringCount} monitoring</b> · {data.resolvedCount} resolved
              <br />
              Worst case <b>{money(data.worstCaseCost)}</b> if every one needed fixing
            </>
          )
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <h2 className="panel-title">The watchlist could not be loaded</h2>
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
                title="Watching"
                rule={<>worst case is the sum of what is still monitored</>}
                link={
                  <AppLink className="sec-link" to="tasks" reg={reg}>
                    Tasks →
                  </AppLink>
                }
              />
              <Panel className="stats num">
                <Kv label="Monitoring" value={String(data.monitoringCount)} note="open observations" />
                <Kv
                  label="Critical"
                  value={String(monitoring.filter((i) => i.severity === 'Critical').length)}
                  note="open and serious"
                />
                <Kv label="Worst case" value={money(data.worstCaseCost)} note="if all of them needed fixing" />
                <Kv label="Resolved" value={String(data.resolvedCount)} note="kept, not deleted" />
              </Panel>
            </Wrap>
          </Section>

          <Section last>
            <Wrap>
              <SectionHead
                title={showResolved ? 'Every issue' : 'Monitoring'}
                rule={<>worst first</>}
                link={
                  <Mark onClick={() => setShowResolved((s) => !s)}>
                    {showResolved ? 'Monitoring only' : 'Show resolved'}
                  </Mark>
                }
              />
              {shown.length === 0 ? (
                <Panel>
                  <p className="panel-empty">
                    Nothing on the watchlist. An issue is an observation you want to keep an eye on —{' '}
                    <Mark onClick={() => setEditing('new')}>add one</Mark> when you notice something you are
                    not ready to act on.
                  </p>
                </Panel>
              ) : (
                <Panel>
                  <ul className="ilist">
                    {shown.map((i) => (
                      <li key={i.id} className={i.status === 'Resolved' ? 'is-resolved' : undefined}>
                        <div className="iw">
                          <Pill tone={i.status === 'Resolved' ? 'ok' : SEVERITY[i.severity].tone}>
                            {i.status === 'Resolved' ? 'Resolved' : SEVERITY[i.severity].label}
                          </Pill>
                          <span>{i.title}</span>
                        </div>

                        {i.currentObservation !== null && <div className="cmp">{i.currentObservation}</div>}

                        <p>
                          {/* Derived. "Advisory since 2024" is only useful if it says how long that has been,
                              and a stored "26 months" is wrong by next month. */}
                          First noted {shortDate(i.firstNoted)} · watched {monthsSince(i.firstNoted, asOf)} months
                          {i.lastChecked !== null && ` · last looked at ${shortDate(i.lastChecked)}`}
                          {i.resolvedDate !== null && ` · resolved ${shortDate(i.resolvedDate)}`}
                        </p>

                        {i.actionIfWorsens !== null && (
                          <p className="iaction">
                            <b>If it worsens:</b> {i.actionIfWorsens}
                          </p>
                        )}

                        <div className="ifoot">
                          <span className="imeta num">
                            {i.estimatedFixCost !== null ? `${money(i.estimatedFixCost)} to fix` : 'no estimate'}
                          </span>
                          <Mark onClick={() => setEditing(i)}>Edit</Mark>
                        </div>
                      </li>
                    ))}
                  </ul>
                </Panel>
              )}
            </Wrap>
          </Section>
        </>
      )}

      <IssueSheet issue={editing} onClose={() => setEditing(null)} reg={reg} />
    </AppShell>
  )
}

function IssueSheet({ issue, onClose, reg }: { issue: IssueItem | 'new' | null; onClose: () => void; reg: string }) {
  const existing = issue !== 'new' && issue !== null ? issue : null
  const [v, setV] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const get = (k: string, fallback = '') => v[k] ?? fallback
  const set = (k: string, value: string) => setV((p) => ({ ...p, [k]: value }))

  const mutation = useMutation({
    mutationFn: async () => {
      const cost = get('estimatedFixCost', existing?.estimatedFixCost?.toString() ?? '')
      const body = {
        title: get('title', existing?.title ?? ''),
        severity: get('severity', existing?.severity ?? 'Low'),
        status: get('status', existing?.status ?? 'Monitoring'),
        firstNoted: get('firstNoted', existing?.firstNoted ?? ''),
        lastChecked: get('lastChecked', existing?.lastChecked ?? '') || null,
        currentObservation: get('currentObservation', existing?.currentObservation ?? '') || null,
        actionIfWorsens: get('actionIfWorsens', existing?.actionIfWorsens ?? '') || null,
        estimatedFixCost: cost === '' ? null : Number(cost),
        notes: get('notes', existing?.notes ?? '') || null,
      }
      const result = await apiRequest<IssueItem>(
        existing === null
          ? `/api/vehicles/${encodeURIComponent(reg)}/issues`
          : `/api/vehicles/${encodeURIComponent(reg)}/issues/${existing.id}`,
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
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'issues'] })
      toast(existing === null ? 'Issue added to the watchlist' : 'Issue saved')
      setV({})
      setError(null)
      onClose()
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Could not save.'),
  })

  const remove = useMutation({
    mutationFn: async () => {
      if (existing === null) return
      const result = await apiRequest<null>(`/api/vehicles/${encodeURIComponent(reg)}/issues/${existing.id}`, {
        method: 'DELETE',
      })
      if (!result.ok) throw new ApiFailure(result.error)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'issues'] })
      toast('Issue removed from the watchlist')
      setV({})
      onClose()
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Could not delete.'),
  })

  return (
    <Sheet
      open={issue !== null}
      onClose={onClose}
      title={existing === null ? 'Add issue' : 'Edit issue'}
      subtitle="something to watch, not yet a job"
      onSubmit={() => mutation.mutate()}
      footer={
        <>
          {existing !== null && (
            <ConfirmButton onConfirm={() => remove.mutate()} pending={remove.isPending} />
          )}
          <Btn type="submit" onClick={() => {}}>
            {mutation.isPending ? 'Saving…' : 'Save issue'}
          </Btn>
        </>
      }
    >
      <Field label="Title" wide>
        {(p) => (
          <input
            type="text"
            placeholder="Brake pipe corrosion, both sides"
            value={get('title', existing?.title ?? '')}
            onChange={(e) => set('title', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Severity" hint="how bad it is, not how late">
        {(p) => (
          <select
            value={get('severity', existing?.severity ?? 'Low')}
            onChange={(e) => set('severity', e.target.value)}
            {...p}
          >
            <option value="Critical">Critical</option>
            <option value="Medium">Medium</option>
            <option value="Low">Low</option>
          </select>
        )}
      </Field>

      <Field label="Status">
        {(p) => (
          <select
            value={get('status', existing?.status ?? 'Monitoring')}
            onChange={(e) => set('status', e.target.value)}
            {...p}
          >
            <option value="Monitoring">Monitoring</option>
            <option value="Resolved">Resolved</option>
          </select>
        )}
      </Field>

      <Field label="First noted" hint="how long it has been watched derives from this">
        {(p) => (
          <input
            type="date"
            value={get('firstNoted', existing?.firstNoted ?? '')}
            onChange={(e) => set('firstNoted', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Last looked at">
        {(p) => (
          <input
            type="date"
            value={get('lastChecked', existing?.lastChecked ?? '')}
            onChange={(e) => set('lastChecked', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Current observation" wide hint="what it looks like now — the thing you compare next time">
        {(p) => (
          <input
            type="text"
            placeholder="surface rust, no flaking, advisory since 2024"
            value={get('currentObservation', existing?.currentObservation ?? '')}
            onChange={(e) => set('currentObservation', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="If it worsens" wide hint="the decision made calmly, in advance">
        {(p) => (
          <input
            type="text"
            placeholder="replace both pipes before the next MOT"
            value={get('actionIfWorsens', existing?.actionIfWorsens ?? '')}
            onChange={(e) => set('actionIfWorsens', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Estimated fix £" hint="the worst-case total sums these">
        {(p) => (
          <input
            type="text"
            inputMode="decimal"
            placeholder="150"
            value={get('estimatedFixCost', existing?.estimatedFixCost?.toString() ?? '')}
            onChange={(e) => set('estimatedFixCost', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Notes">
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
