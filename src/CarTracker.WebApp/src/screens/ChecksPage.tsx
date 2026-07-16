import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import type { VehicleSummary } from '../api/client'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { Cadence } from '../components/Cadence'
import { DueBadge } from '../components/Pill'
import { Field, Sheet } from '../components/Sheet'
import { StatTile, StatTiles } from '../components/StatTile'
import { CFoot, Panel, Section, SectionHead, Wrap } from '../components/layout'
import { AppLink } from '../lib/link'
import { usePlate } from '../lib/usePlate'
import type { DueStatus } from '../lib/status'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'

type Checks = VehicleSummary['checks']
type CheckState = Checks['checks'][number]

const dayMonth = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

/** Ordered by how much they want doing. NeverLogged sits after DueSoon: absence of data is not urgency. */
const RANK: Record<DueStatus, number> = { Overdue: 0, DueSoon: 1, NeverLogged: 2, Ok: 3 }

function dueText(c: CheckState): string {
  if (c.status === 'NeverLogged') return 'never logged'
  if (c.daysRemaining === null) return '—'
  if (c.daysRemaining < 0) return `${Math.abs(c.daysRemaining)} days over`
  if (c.daysRemaining === 0) return 'due today'
  return `in ${c.daysRemaining} days`
}

/**
 * Regular checks.
 *
 * **The four buckets must sum to the number of definitions.** The workbook's Dashboard counts 17 against 18
 * defined: "Spare tyre pressure" has never been logged and silently falls out of its OK/due/overdue buckets.
 * Never-logged is the fourth state — the domain's own comment calls it "not an error, not a default, and
 * emphatically not Ok" — and the sum is printed at the bottom so a recurrence would be visible rather than
 * silent.
 *
 * The design puts never-logged on its DETECTORS panel, as a *data-integrity* flag, while its checks screen
 * renders it as a due tile. The tile is right and the panel is wrong: whether a check has ever been done is a
 * fact about the car, not about the data.
 */
export function ChecksPage() {
  const reg = useVehicleReg()
  const plate = usePlate()
  const [logging, setLogging] = useState<CheckState[] | null>(null)

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['vehicle', reg, 'checks'] as const,
    queryFn: async () => {
      const result = await apiRequest<Checks>(`/api/vehicles/${encodeURIComponent(reg)}/checks`)
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
  })

  const ordered = [...(data?.checks ?? [])].sort((a, b) => {
    const r = RANK[a.status as DueStatus] - RANK[b.status as DueStatus]
    return r !== 0 ? r : (a.daysRemaining ?? 0) - (b.daysRemaining ?? 0)
  })

  const outstanding = ordered.filter((c) => c.status === 'Overdue' || c.status === 'DueSoon')

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="checks"
      center={
        outstanding.length > 0
          ? { kind: 'action', icon: 'check', label: 'Log due', onClick: () => setLogging(outstanding) }
          : null
      }
      footer={
        <>
          Status is computed from each check's last log and its interval — never stored. A check that has never
          been logged is <b>never logged</b>, not OK: the workbook's dashboard counts 17 of its 18 definitions
          because the eighteenth has no log and falls out of its three buckets.
        </>
      }
    >
      <PageHead
        eyebrow="Regular checks · computed live"
        title="Checks"
        plate={plate}
        pmeta={
          data === undefined ? undefined : (
            <>
              {data.totalCount} defined · <b>{data.overdueCount} overdue</b>
              <br />
              Status derives from the last log —<br />
              never logged is a state, not a gap
            </>
          )
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <h2 className="panel-title">The checks could not be loaded</h2>
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
      ) : data.totalCount === 0 ? (
        <Section last>
          <Wrap>
            <Panel>
              <p className="panel-empty">
                No checks defined for this vehicle, so there is nothing to be due — four zero tiles would say
                "all clear" about a question nobody has asked yet.{' '}
                <AppLink to="settings" reg={reg}>
                  Define them in settings
                </AppLink>
                , and status starts deriving from the first log.
              </p>
            </Panel>
          </Wrap>
        </Section>
      ) : (
        <>
          <Section>
            <Wrap>
              <SectionHead
                title="Status"
                rule={<>from each check's last log + its interval</>}
                link={
                  <AppLink className="sec-link" to="settings" reg={reg}>
                    Define checks →
                  </AppLink>
                }
              />
              <Panel>
                <StatTiles>
                  <StatTile due="Overdue" count={data.overdueCount} />
                  <StatTile due="DueSoon" count={data.dueSoonCount} />
                  <StatTile due="Ok" count={data.okCount} />
                  <StatTile due="NeverLogged" count={data.neverLoggedCount} />
                </StatTiles>
                <CFoot>
                  <span>
                    {data.okCount} + {data.dueSoonCount} + {data.overdueCount} + {data.neverLoggedCount} ={' '}
                    <b>{data.totalCount}</b> · every definition is in exactly one bucket
                  </span>
                </CFoot>
              </Panel>
            </Wrap>
          </Section>

          <Section last>
            <Wrap>
              <SectionHead
                title="All checks"
                rule={<>most pressing first</>}
                link={
                  outstanding.length > 0 ? (
                    <Mark onClick={() => setLogging(outstanding)}>Log {outstanding.length} due</Mark>
                  ) : undefined
                }
              />
              <Panel>
                <ul className="clist">
                  {ordered.map((c) => (
                    <li key={c.checkDefinitionId} className={c.status === 'DueSoon' ? 'is-soon' : undefined}>
                      <Cadence>{c.cadenceLabel}</Cadence>
                      <span className="cname">
                        {c.name}
                        <em>
                          {c.lastPerformedOn === null
                            ? `every ${c.intervalDays} days · no log yet`
                            : `last ${dayMonth(c.lastPerformedOn)} · every ${c.intervalDays} days`}
                        </em>
                      </span>
                      <span className="cdays num">{dueText(c)}</span>
                      <DueBadge due={c.status as DueStatus} />
                      <Mark onClick={() => setLogging([c])}>Log</Mark>
                    </li>
                  ))}
                </ul>
              </Panel>
            </Wrap>
          </Section>
        </>
      )}

      <LogChecksSheet checks={logging} onClose={() => setLogging(null)} reg={reg} />
    </AppShell>
  )
}

/**
 * Logging one check or a batch of them.
 *
 * The design's "Mark done" fires a toast and moves a counter; nothing is written. The batch matters as much as
 * the single: five weekly walk-around checks done in one go is one action, and making it five is how a log
 * stops getting kept.
 */
function LogChecksSheet({
  checks,
  onClose,
  reg,
}: {
  checks: CheckState[] | null
  onClose: () => void
  reg: string
}) {
  const [v, setV] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const get = (k: string) => v[k] ?? ''
  const set = (k: string, value: string) => setV((p) => ({ ...p, [k]: value }))

  const mutation = useMutation({
    mutationFn: async () => {
      const result = await apiRequest<unknown>(`/api/vehicles/${encodeURIComponent(reg)}/checks/logs`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          checkDefinitionIds: (checks ?? []).map((c) => c.checkDefinitionId),
          performedOn: get('performedOn'),
          result: get('result') || null,
          notes: get('notes') || null,
        }),
      })
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'checks'] })
      await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
      await queryClient.invalidateQueries({ queryKey: queryKeys.garage })
      const n = checks?.length ?? 0
      toast(
        n === 1
          ? `${checks![0]!.name} logged · its next due date recomputed`
          : `${n} checks logged · their next due dates recomputed`,
      )
      setV({})
      setError(null)
      onClose()
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Could not save.'),
  })

  const n = checks?.length ?? 0

  return (
    <Sheet
      open={checks !== null}
      onClose={onClose}
      title={n === 1 ? 'Log check' : `Log ${n} checks`}
      subtitle="next due recomputes from this date + the interval"
      onSubmit={() => mutation.mutate()}
      footer={
        <Btn type="submit" onClick={() => {}}>
          {mutation.isPending ? 'Saving…' : n === 1 ? 'Log it' : `Log all ${n}`}
        </Btn>
      }
    >
      <div className="field wide">
        <span className="hint">
          {(checks ?? []).map((c) => c.name).join(' · ')}
        </span>
      </div>

      <Field label="Performed on">
        {(p) => <input type="date" value={get('performedOn')} onChange={(e) => set('performedOn', e.target.value)} {...p} />}
      </Field>

      <Field label="Result" hint="typed, not prose — Attention is what the head-gasket watch depends on">
        {(p) => (
          <select value={get('result')} onChange={(e) => set('result', e.target.value)} {...p}>
            {/* Null is the ordinary "did it, all fine" the batch uses — distinct from an explicit OK. */}
            <option value="">Logged, no verdict</option>
            <option value="Ok">OK</option>
            <option value="Attention">Attention</option>
            <option value="Failed">Failed</option>
          </select>
        )}
      </Field>

      <Field label="Notes" wide>
        {(p) => <input type="text" placeholder="mayo under the filler cap" value={get('notes')} onChange={(e) => set('notes', e.target.value)} {...p} />}
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
