import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { BudgetBars } from '../components/BudgetBars'
import { Kv } from '../components/Kv'
import { Sheet } from '../components/Sheet'
import { CFoot, Panel, Section, SectionHead, Wrap } from '../components/layout'
import { formError, reportApiError, type FieldErrors } from '../lib/formErrors'
import { AppLink } from '../lib/link'
import { usePlate } from '../lib/usePlate'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'

interface BudgetGroupLine {
  name: string
  annualBudget: number | null
  actualSpend: number
  remaining: number | null
  percentUsed: number | null
  isOverBudget: boolean
  categories: string[]
  isUncategorised: boolean
}

interface BudgetSummary {
  totalBudget: number
  totalActual: number
  lines: BudgetGroupLine[]
  /** Spend on the car itself over the period — in no group and in no line, so the screen has to say so. */
  excludedPurchase: number
}

interface CategoryItem {
  name: string
  isMirrorOnly: boolean
}

// These strings bind to the backend `BudgetPeriod` enum by name, so they must match its members exactly —
// `Rolling12Months`, not `RollingTwelveMonths`.
type Period = 'CalendarYear' | 'SincePurchase' | 'Rolling12Months'

const PERIODS: { value: Period; label: string }[] = [
  { value: 'CalendarYear', label: 'This year' },
  { value: 'Rolling12Months', label: 'Last 12 months' },
  { value: 'SincePurchase', label: 'Since purchase' },
]

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', minimumFractionDigits: 2 })

/**
 * The budget — grouped.
 *
 * A budget is a **named group of one or more expense categories** (a single-category budget is a group of one),
 * with an optional target. A group with **no target** is tracked — its spend is shown with no bar — which is not
 * the same as a group budgeted at zero. Spend in a category that is in no group folds into "Everything else".
 * New vehicles start with four default groups (Fuel / Service & Repairs / Insurance, Tax & MOT / Equipment &
 * Tools), seeded with no target.
 *
 * Over-budget bars cap at 100% and the real figure is in the text — a bar at 158% would draw outside its track.
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
  // The synthetic "Everything else" line is not a real, editable group.
  const groups = lines.filter((l) => !l.isUncategorised)
  const budgeted = groups.filter((l) => l.annualBudget !== null)
  const over = lines.filter((l) => l.isOverBudget)

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="budget"
      center={{ kind: 'action', icon: 'gear', label: 'Edit groups', onClick: () => setEditing(true) }}
      footer={
        <>
          A budget is a group of one or more categories. Targets are the only stored numbers here; every other
          figure is computed from the expense rows at render. A group with <b>no target</b> shows its spend and
          no bar, and spend in no group lands in <b>Everything else</b> — money the app knows about is never
          hidden.
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
                        className={`fchip fchip-sm${period === p.value ? ' is-on' : ''}`}
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
              <Panel className="stats four num">
                <Kv label="Spent" value={money(data.totalActual)} note="from the expense rows" />
                <Kv
                  label="Budgeted"
                  value={data.totalBudget > 0 ? money(data.totalBudget) : '—'}
                  note={data.totalBudget > 0 ? `${budgeted.length} group${budgeted.length === 1 ? '' : 's'}` : 'no targets set'}
                />
                <Kv
                  label="Used"
                  value={data.totalBudget > 0 ? `${((data.totalActual / data.totalBudget) * 100).toFixed(1)}%` : '—'}
                  note={data.totalBudget > 0 ? 'of the total target' : 'needs a target'}
                />
                <Kv
                  label="Over budget"
                  value={String(over.length)}
                  note={over.length > 0 ? over.map((l) => l.name).join(' · ') : 'nothing over'}
                />
              </Panel>
            </Wrap>
          </Section>

          <Section last>
            <Wrap>
              <SectionHead
                title="By group"
                rule={<>targets are stored; everything else is not</>}
                link={<Mark onClick={() => setEditing(true)}>Edit groups</Mark>}
              />
              {lines.length === 0 ? (
                <Panel>
                  <p className="panel-empty">Nothing spent and no groups in this period.</p>
                </Panel>
              ) : (
                <Panel className="pad">
                  <BudgetBars lines={lines} />

                  <CFoot>
                    <span>
                      {budgeted.length} with a target · {groups.length - budgeted.length} tracked ·{' '}
                      <b>{over.length} over</b>
                    </span>
                    {/* The one thing this screen leaves out, said rather than performed. Buying the car is not
                        a running cost, so it belongs in no group and not in "Everything else" either — but it
                        appeared NOWHERE, under a footer promising that money the app knows about is never
                        hidden. £1,700 absent without comment is the same defect as £1,183 absent without
                        comment, which is what this whole change is about. */}
                    {(data?.excludedPurchase ?? 0) > 0 && (
                      <span>
                        {money(data!.excludedPurchase)} for the car itself is excluded — a purchase, not a
                        running cost
                      </span>
                    )}
                  </CFoot>
                </Panel>
              )}
            </Wrap>
          </Section>
        </>
      )}

      <GroupsSheet open={editing} onClose={() => setEditing(false)} reg={reg} groups={groups} period={period} />
    </AppShell>
  )
}

interface EditableGroup {
  key: number
  name: string
  amount: string
  categories: string[]
}

let nextKey = 1

function GroupsSheet({
  open,
  onClose,
  reg,
  groups,
  period,
}: {
  open: boolean
  onClose: () => void
  reg: string
  groups: BudgetGroupLine[]
  period: Period
}) {
  const [rows, setRows] = useState<EditableGroup[]>([])
  const [seeded, setSeeded] = useState(false)
  const [errors, setErrors] = useState<FieldErrors>({})
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const { data: categories } = useQuery({
    queryKey: ['reference', 'expense-categories'] as const,
    queryFn: async () => {
      const result = await apiRequest<CategoryItem[]>('/api/reference/expense-categories')
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    staleTime: Infinity,
  })

  // Seed the editor from the current groups the first time it opens (and re-seed whenever it reopens).
  if (open && !seeded) {
    setSeeded(true)
    setRows(
      groups.map((g) => ({
        key: nextKey++,
        name: g.name,
        amount: g.annualBudget?.toString() ?? '',
        categories: [...g.categories],
      })),
    )
    setErrors({})
  }
  if (!open && seeded) setSeeded(false)

  // Which group currently owns each category — a category may be in at most one group.
  const ownerOf = new Map<string, number>()
  for (const r of rows) for (const c of r.categories) ownerOf.set(c, r.key)

  const setRow = (key: number, patch: Partial<EditableGroup>) =>
    setRows((rs) => rs.map((r) => (r.key === key ? { ...r, ...patch } : r)))

  const toggleCategory = (key: number, category: string) =>
    setRows((rs) =>
      rs.map((r) => {
        if (r.key !== key) return r
        return r.categories.includes(category)
          ? { ...r, categories: r.categories.filter((c) => c !== category) }
          : { ...r, categories: [...r.categories, category] }
      }),
    )

  const addGroup = () => setRows((rs) => [...rs, { key: nextKey++, name: '', amount: '', categories: [] }])
  const removeGroup = (key: number) => setRows((rs) => rs.filter((r) => r.key !== key))

  const mutation = useMutation({
    mutationFn: async () => {
      const body = {
        groups: rows
          // Drop fully-empty rows so an accidental "Add group" left blank is not a validation error.
          .filter((r) => r.name.trim() !== '' || r.categories.length > 0)
          .map((r) => {
            const raw = r.amount.replace(/[£,\s]/g, '')
            return {
              name: r.name.trim(),
              annualBudget: raw === '' ? null : Number(raw),
              categories: r.categories,
            }
          }),
        period,
      }
      const result = await apiRequest<BudgetSummary>(`/api/vehicles/${encodeURIComponent(reg)}/budget/groups`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'budget'] })
      await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
      toast('Budget groups saved · the variance recomputed')
      onClose()
    },
    // The server's failures here are collection-level (names, categories, targets) — they fall to the footer.
    onError: (e) => setErrors(reportApiError(e, [])),
  })

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title="Edit budget groups"
      subtitle="a group is one or more categories with an optional target"
      onSubmit={() => mutation.mutate()}
      footer={
        <Btn type="submit" onClick={() => {}}>
          {mutation.isPending ? 'Saving…' : 'Save groups'}
        </Btn>
      }
    >
      <div className="field wide">
        <span className="hint">
          Give each group a name and, optionally, an annual target (leave it empty to just track spend). Assign
          categories with the chips — each category can be in only one group, and any spend left ungrouped shows
          as “Everything else”.
        </span>
      </div>

      {rows.map((r) => (
        <div key={r.key} className="bgroup">
          <div className="bgroup-head">
            <input
              type="text"
              className="bgroup-name"
              placeholder="Group name"
              aria-label="Group name"
              value={r.name}
              onChange={(e) => setRow(r.key, { name: e.target.value })}
            />
            <input
              type="text"
              className="bgroup-amt"
              inputMode="decimal"
              placeholder="no target"
              aria-label={`${r.name || 'Group'} annual target`}
              value={r.amount}
              onChange={(e) => setRow(r.key, { amount: e.target.value })}
            />
            <Mark onClick={() => removeGroup(r.key)}>Remove</Mark>
          </div>
          <div className="bgroup-cats">
            {(categories ?? []).map((c) => {
              const owner = ownerOf.get(c.name)
              const mine = owner === r.key
              const takenElsewhere = owner !== undefined && !mine
              return (
                <button
                  key={c.name}
                  type="button"
                  className={`fchip fchip-sm${mine ? ' is-on' : ''}`}
                  aria-pressed={mine}
                  disabled={takenElsewhere}
                  title={takenElsewhere ? 'Already in another group' : undefined}
                  onClick={() => toggleCategory(r.key, c.name)}
                >
                  {c.name}
                </button>
              )
            })}
          </div>
        </div>
      ))}

      <div className="field wide">
        <Mark onClick={addGroup}>+ Add group</Mark>
      </div>

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
