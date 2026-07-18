import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { Kv } from '../components/Kv'
import { Field, Sheet } from '../components/Sheet'
import { CFoot, Panel, Section, SectionHead, Wrap } from '../components/layout'
import { AppLink } from '../lib/link'
import { usePlate } from '../lib/usePlate'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'

interface BudgetLine {
  category: string
  annualBudget: number | null
  actualSpend: number
  remaining: number | null
  percentUsed: number | null
  isOverBudget: boolean
}

interface BudgetSummary {
  totalBudget: number
  totalActual: number
  lines: BudgetLine[]
}

// These strings bind to the backend `BudgetPeriod` enum by name, so they must match its members exactly —
// `Rolling12Months`, not `RollingTwelveMonths`. The mismatch failed enum binding (a 400 the query never
// recovered from), which is why "Last 12 months" hung and rendered nothing.
type Period = 'CalendarYear' | 'SincePurchase' | 'Rolling12Months'

const PERIODS: { value: Period; label: string }[] = [
  { value: 'CalendarYear', label: 'This year' },
  { value: 'Rolling12Months', label: 'Last 12 months' },
  { value: 'SincePurchase', label: 'Since purchase' },
]

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', minimumFractionDigits: 2 })

/**
 * The budget.
 *
 * `GetBudgetSummaryAsync` has existed since Phase 1 and had no HTTP caller until M1a, and no screen until now —
 * so the variance it computes has never been looked at.
 *
 * **A category with no target is not a category at £0.** The dashboard deliberately left its budget figures out
 * rather than show "43.2% used" derived from nothing, and the same rule holds here: an absent target renders as
 * "no target", never as a bar at 100%. `AnnualBudget` is nullable for exactly this reason.
 *
 * **Over-budget bars cap at 100% and the real figure is in the text.** A bar at 158% would draw outside its
 * track; a bar clamped to 100% with no figure beside it says "at the limit" when the truth is half as much
 * again. The percentage is the carrier, the bar is the illustration.
 */
export function BudgetPage() {
  const reg = useVehicleReg()
  const plate = usePlate()
  const [period, setPeriod] = useState<Period>('CalendarYear')
  const [editing, setEditing] = useState(false)

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['vehicle', reg, 'budget', period] as const,
    queryFn: async () => {
      const result = await apiRequest<BudgetSummary>(
        `/api/vehicles/${encodeURIComponent(reg)}/budget?period=${period}`,
      )
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
  })

  const lines = data?.lines ?? []
  const budgeted = lines.filter((l) => l.annualBudget !== null)
  const over = budgeted.filter((l) => l.isOverBudget)
  const untargeted = lines.filter((l) => l.annualBudget === null && l.actualSpend > 0)

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="budget"
      center={{ kind: 'action', icon: 'gear', label: 'Set targets', onClick: () => setEditing(true) }}
      footer={
        <>
          Targets are the only stored numbers here; every other figure is computed from the expense rows at
          render. A category with <b>no target</b> shows its spend and no bar — it is not a category budgeted at
          zero, and pretending otherwise is how a budget starts lying about what was planned.
        </>
      }
    >
      <PageHead
        eyebrow="Budget · variance computed live"
        title="Budget"
        plate={plate}
        pmeta={
          data === undefined ? undefined : (
            <>
              <b>{money(data.totalActual)}</b> of {money(data.totalBudget)}
              <br />
              {data.totalBudget > 0
                ? `${((data.totalActual / data.totalBudget) * 100).toFixed(1)}% used · ${over.length} over`
                : 'no targets set yet'}
            </>
          )
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <h2 className="panel-title">The budget could not be loaded</h2>
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
                title="Against target"
                rule={
                  <>
                    {PERIODS.map((p) => (
                      <button
                        key={p.value}
                        type="button"
                        className={`fchip${period === p.value ? ' is-on' : ''}`}
                        aria-pressed={period === p.value}
                        onClick={() => setPeriod(p.value)}
                      >
                        {p.label}
                      </button>
                    ))}
                  </>
                }
                link={
                  <AppLink className="sec-link" to="expenses" reg={reg}>
                    Expenses →
                  </AppLink>
                }
              />
              <Panel className="stats num">
                <Kv label="Spent" value={money(data.totalActual)} note="from the expense rows" />
                <Kv
                  label="Budgeted"
                  value={data.totalBudget > 0 ? money(data.totalBudget) : '—'}
                  note={data.totalBudget > 0 ? `${budgeted.length} categories` : 'no targets set'}
                />
                <Kv
                  label="Used"
                  value={data.totalBudget > 0 ? `${((data.totalActual / data.totalBudget) * 100).toFixed(1)}%` : '—'}
                  note={data.totalBudget > 0 ? 'of the total target' : 'needs a target'}
                />
                <Kv
                  label="Over budget"
                  value={String(over.length)}
                  note={over.length > 0 ? over.map((l) => l.category).join(' · ') : 'nothing over'}
                />
              </Panel>
            </Wrap>
          </Section>

          <Section last>
            <Wrap>
              <SectionHead
                title="By category"
                rule={<>targets are stored; everything else is not</>}
                link={<Mark onClick={() => setEditing(true)}>Set targets</Mark>}
              />
              {lines.length === 0 ? (
                <Panel>
                  <p className="panel-empty">Nothing spent and nothing budgeted in this period.</p>
                </Panel>
              ) : (
                <Panel className="pad">
                  <ul className="bars-list">
                    {lines.map((l) => {
                      const pct = l.percentUsed ?? 0
                      // Capped for the geometry only. The figure beside it is never the capped one.
                      const width = Math.min(pct, 100)
                      return (
                        <li key={l.category}>
                          <span className="bl-name">{l.category}</span>
                          <span className="bl-val num">
                            {money(l.actualSpend)}
                            {l.annualBudget === null ? (
                              // Not a bar at 100%, and not £0 budgeted. The absence is the fact.
                              <em className="faint"> · no target</em>
                            ) : (
                              <em className={l.isOverBudget ? 'over' : undefined}>
                                {' '}
                                {pct.toFixed(0)}% of {money(l.annualBudget)}
                                {l.isOverBudget && l.remaining !== null && ` · ${money(-l.remaining)} over`}
                              </em>
                            )}
                          </span>
                          {l.annualBudget !== null && (
                            <span className="track">
                              <i
                                className={l.isOverBudget ? 'over' : undefined}
                                style={{ width: `${width}%` }}
                              />
                            </span>
                          )}
                        </li>
                      )
                    })}
                  </ul>

                  <CFoot>
                    <span>
                      {budgeted.length} budgeted · {untargeted.length} spending with no target ·{' '}
                      <b>{over.length} over</b>
                    </span>
                    {untargeted.length > 0 && (
                      <span>no target: {untargeted.map((l) => l.category).join(', ')}</span>
                    )}
                  </CFoot>
                </Panel>
              )}
            </Wrap>
          </Section>
        </>
      )}

      <TargetsSheet open={editing} onClose={() => setEditing(false)} reg={reg} lines={lines} period={period} />
    </AppShell>
  )
}

function TargetsSheet({
  open,
  onClose,
  reg,
  lines,
  period,
}: {
  open: boolean
  onClose: () => void
  reg: string
  lines: BudgetLine[]
  period: Period
}) {
  const [v, setV] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const get = (c: string, fallback: string) => v[c] ?? fallback
  const set = (c: string, value: string) => setV((p) => ({ ...p, [c]: value }))

  const mutation = useMutation({
    mutationFn: async () => {
      // The endpoint takes the FULL set and removes anything left out — so an empty box is a deletion, not a
      // no-op, and every line has to be sent whether it was touched or not.
      const targets = lines
        .map((l) => ({
          category: l.category,
          annualBudget: Number(get(l.category, l.annualBudget?.toString() ?? '')),
        }))
        .filter((t) => Number.isFinite(t.annualBudget) && t.annualBudget > 0)

      const result = await apiRequest<BudgetSummary>(`/api/vehicles/${encodeURIComponent(reg)}/budget/targets`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ targets, period }),
      })
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'budget'] })
      await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
      toast('Targets saved · the variance recomputed')
      setV({})
      setError(null)
      onClose()
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Could not save.'),
  })

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title="Set targets"
      subtitle="the only stored numbers on this screen"
      onSubmit={() => mutation.mutate()}
      footer={
        <Btn type="submit" onClick={() => {}}>
          {mutation.isPending ? 'Saving…' : 'Save targets'}
        </Btn>
      }
    >
      <div className="field wide">
        <span className="hint">
          An empty box removes the target. That is not the same as a target of zero — a category with no target
          shows its spend and no bar, because nobody planned for it either way.
        </span>
      </div>

      {lines.map((l) => (
        <Field key={l.category} label={`${l.category} £`} hint={`${money(l.actualSpend)} spent`}>
          {(p) => (
            <input
              type="text"
              inputMode="decimal"
              placeholder="no target"
              value={get(l.category, l.annualBudget?.toString() ?? '')}
              onChange={(e) => set(l.category, e.target.value)}
              {...p}
            />
          )}
        </Field>
      ))}

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
