import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import type { VehicleSummary } from '../api/client'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { ConfirmButton } from '../components/ConfirmButton'
import { Absent, DataTable, Sub, type Column } from '../components/DataTable'
import { Kv } from '../components/Kv'
import { TableControls } from '../components/TableControls'
import { useTableView, type FilterGroup, type SortKey } from '../components/useTableView'
import { IntegrityPill } from '../components/Pill'
import { Field, Sheet } from '../components/Sheet'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { AppLink } from '../lib/link'
import { usePlate } from '../lib/usePlate'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'

interface ExpenseItem {
  id: number
  entryDate: string
  category: string
  subCategory: string | null
  vendor: string | null
  amount: number
  mileage: number | null
  paymentMethod: string | null
  fuelEntryId: number | null
  serviceRecordId: number | null
  notes: string | null
}

/** A row mirrored from a fill or a service record — read-only here, edited at its source. */
const isMirrored = (e: ExpenseItem) => e.fuelEntryId !== null || e.serviceRecordId !== null

interface ExpenseLog {
  rollups: VehicleSummary['spend']
  entries: ExpenseItem[]
}

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', minimumFractionDigits: 2 })

const dayMonth = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })

const year = (iso: string) => new Date(`${iso}T00:00:00`).getFullYear()

/**
 * The expenses log.
 *
 * Two things the workbook's Expenses sheet does that have no equivalent here, and both are the point:
 *
 * 1. **It carries ~30 trailing blank rows holding a running-total formula.** There is no running-total column
 *    in the schema; the rollups above are `SUM()` at render, and they are the same figures the dashboard
 *    shows because they come from the same service.
 * 2. **It carries one lumped "fuel to date" row of £725.70** instead of per-fill entries, which is the
 *    £163.16 gap against the Fuel Log's real total. Every fill now mirrors in automatically — those rows are
 *    the ones marked "from fuel", and they cannot be edited here, because the fill is the record and this is
 *    its shadow.
 */
export function ExpensesPage() {
  const reg = useVehicleReg()
  const plate = usePlate()
  const [editing, setEditing] = useState<ExpenseItem | 'new' | null>(null)

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['vehicle', reg, 'expenses'] as const,
    queryFn: async () => {
      const result = await apiRequest<ExpenseLog>(`/api/vehicles/${encodeURIComponent(reg)}/expenses`)
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
  })

  const rollups = data?.rollups
  const mirrored = data?.entries.filter((e) => e.fuelEntryId !== null).length ?? 0

  const categories = useMemo(
    () => [...new Set((data?.entries ?? []).map((e) => e.category))].sort(),
    [data?.entries],
  )

  const groups: FilterGroup<ExpenseItem>[] = useMemo(() => {
    const day = (n: number) => {
      const d = new Date()
      d.setDate(d.getDate() - n)
      return d.toISOString().slice(0, 10)
    }
    const yearStart = `${new Date().getFullYear()}-01-01`
    return [
      {
        id: 'category',
        label: 'Category',
        render: 'chips',
        options: categories.map((c) => ({ id: c, label: c, test: (e: ExpenseItem) => e.category === c })),
      },
      {
        id: 'range',
        label: 'Period',
        render: 'select',
        options: [
          { id: '30', label: 'Last 30 days', test: (e) => e.entryDate >= day(30) },
          { id: '90', label: 'Last 90 days', test: (e) => e.entryDate >= day(90) },
          { id: 'ytd', label: 'This year', test: (e) => e.entryDate >= yearStart },
        ],
      },
    ]
  }, [categories])

  const sorts: SortKey<ExpenseItem>[] = useMemo(
    () => [
      { id: 'date', label: 'Date', compare: (a, b) => a.entryDate.localeCompare(b.entryDate) },
      { id: 'amount', label: 'Amount', compare: (a, b) => a.amount - b.amount },
    ],
    [],
  )

  const view = useTableView(data?.entries ?? [], { groups, sorts, defaultSortId: 'date', defaultDir: 'desc' })
  const filteredTotal = view.rows.reduce((sum, e) => sum + e.amount, 0)

  const columns: Column<ExpenseItem>[] = [
    {
      key: 'date',
      label: 'Date',
      width: '70px',
      priority: 'essential',
      render: (e) => (
        <b>
          {dayMonth(e.entryDate)}
          <Sub>{year(e.entryDate)}</Sub>
        </b>
      ),
    },
    {
      key: 'category',
      label: 'Category',
      width: '1fr',
      render: (e) => (
        <>
          {e.category}
          {e.subCategory !== null && <Sub>{e.subCategory}</Sub>}
        </>
      ),
    },
    {
      key: 'vendor',
      label: 'Vendor',
      width: '1fr',
      priority: 'secondary',
      render: (e) => (
        <>
          {e.vendor ?? <Absent>not recorded</Absent>}
          {e.notes !== null && <Sub>{e.notes}</Sub>}
        </>
      ),
    },
    {
      key: 'mileage',
      label: 'Odometer',
      width: '76px',
      align: 'right',
      priority: 'secondary',
      render: (e) => (e.mileage === null ? <Absent /> : e.mileage.toLocaleString('en-GB')),
    },
    {
      key: 'payment',
      label: 'Paid by',
      width: '80px',
      priority: 'secondary',
      render: (e) => e.paymentMethod ?? <Absent />,
    },
    {
      key: 'amount',
      label: 'Amount',
      width: '84px',
      align: 'right',
      priority: 'essential',
      render: (e) => <b>{money(e.amount)}</b>,
    },
    {
      key: 'source',
      label: 'Source',
      width: '92px',
      render: (e) =>
        !isMirrored(e) ? (
          <Absent>entered</Absent>
        ) : (
          // Blue: this is a statement about where the datum came from, not about urgency. The row is a shadow
          // of a fill or a service record and the API refuses to edit it — the mirror only holds if it cannot
          // drift from its source.
          <IntegrityPill>{e.fuelEntryId !== null ? 'From fuel' : 'From service'}</IntegrityPill>
        ),
    },
  ]

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="expenses"
      center={{ kind: 'action', icon: 'plus', label: 'Add expense', onClick: () => setEditing('new') }}
      footer={
        <>
          Every total here is <b>SUM() at render</b>. The workbook's Expenses sheet carried a running-total
          column down ~30 blank rows; a stored total is a total that can disagree with its own rows. Fuel rows
          are mirrored from the fuel log automatically and are edited there, not here.
        </>
      }
    >
      <PageHead
        eyebrow="Expenses · computed live"
        title="Expenses"
        plate={plate}
        pmeta={
          rollups === undefined ? undefined : (
            <>
              Since purchase <b>{money(rollups.totalSincePurchase)}</b>
              <br />
              Totals are computed from the rows —<br />
              there is no running-total column
            </>
          )
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <h2 className="panel-title">The expenses log could not be loaded</h2>
              <p className="panel-empty">{error instanceof Error ? error.message : 'The request failed.'}</p>
              <button className="btn" type="button" onClick={() => void refetch()}>
                Try again
              </button>
            </Panel>
          </Wrap>
        </Section>
      ) : isPending || data === undefined || rollups === undefined ? (
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
                title="Rollups"
                rule={<>computed from the rows, never stored</>}
                link={
                  <AppLink className="sec-link" to="dashboard" reg={reg}>
                    Dashboard →
                  </AppLink>
                }
              />
              <Panel className="stats num">
                <Kv label="This year" value={money(rollups.totalYtd)} note="all categories" />
                <Kv label="Fuel" value={money(rollups.fuelYtd)} note={`${mirrored} mirrored from fills`} />
                <Kv label="Service & repairs" value={money(rollups.serviceAndRepairsYtd)} note="this year" />
                <Kv label="Insurance, tax & MOT" value={money(rollups.statutoryYtd)} note="this year" />
                <Kv
                  label="Cost per mile"
                  value={rollups.costPerMileExcludingPurchase === null ? '—' : money(rollups.costPerMileExcludingPurchase)}
                  note={rollups.costPerMileExcludingPurchase === null ? 'needs mileage' : 'running only'}
                />
                <Kv
                  label="Monthly average"
                  value={rollups.monthlyAverage === null ? '—' : money(rollups.monthlyAverage)}
                  note="ex-purchase"
                />
              </Panel>
            </Wrap>
          </Section>

          <Section last>
            <Wrap>
              <SectionHead
                title="Entries"
                rule={
                  <>
                    {data.entries.length} row{data.entries.length === 1 ? '' : 's'}
                    {mirrored > 0 && <> · {mirrored} mirrored from fuel</>}
                  </>
                }
                link={<Mark onClick={() => setEditing('new')}>Add expense</Mark>}
              />
              {data.entries.length === 0 ? (
                <Panel>
                  <p className="panel-empty">
                    No expenses yet. Fills mirror in here automatically — everything else is entered.
                  </p>
                </Panel>
              ) : (
                <>
                  <TableControls view={view} noun="rows" />

                  {/* The filtered sum, and only when a filter is active — a client SUM over the visible rows,
                      labelled as the filtered view's total, distinct from the server's authoritative YTD rollup
                      above ("This year · all categories"), so the two are never mistaken for one. */}
                  {view.filtered && view.count > 0 && (
                    <div className="filtered-total num" role="status">
                      <span className="ft-label">Filtered view</span>
                      <span className="ft-value">{money(filteredTotal)}</span>
                      <span className="ft-note">
                        {view.count} of {view.total} rows — not the YTD figure above
                      </span>
                    </div>
                  )}

                  {view.count === 0 ? (
                    <Panel>
                      <p className="panel-empty">No expenses match this filter. Clear it to see all {view.total}.</p>
                    </Panel>
                  ) : (
                    <DataTable
                      columns={columns}
                      rows={view.rows}
                      rowKey={(e) => e.id}
                      label="Expenses"
                      onRowClick={setEditing}
                      // A mirror row is not editable here — clicking it would 409. It stays read-only, and its
                      // "From fuel"/"From service" pill is the pointer to where it is edited.
                      rowClickable={(e) => !isMirrored(e)}
                      rowLabel={(e) => `Edit the ${e.category} expense on ${dayMonth(e.entryDate)}`}
                    />
                  )}
                </>
              )}
            </Wrap>
          </Section>
        </>
      )}

      <AddExpenseSheet editing={editing} onClose={() => setEditing(null)} reg={reg} />
    </AppShell>
  )
}

interface CategoryItem {
  name: string
  isMirrorOnly: boolean
}

/**
 * The seeded categories, from the API.
 *
 * **This was a hardcoded list and every wrong guess in it was a 400.** It read "Repairs", "Road tax",
 * "Cleaning", "Other", "Tools", "Tyres"; the seed says `Repair`, `Tax`, `Wash`, `Misc`, `Tools/Equipment`, and
 * two of those are not categories at all. The endpoint validates the name against the same table, so eight of
 * the twelve options offered were rejected on save — invisible until someone tried.
 *
 * A seeded list is data. Copying it into another language produces a copy that cannot be kept in step, which
 * is the same mistake as the hand-guessed `MileageOrigin` map on the mileage screen.
 */
function useCategories() {
  return useQuery({
    queryKey: ['reference', 'expense-categories'] as const,
    queryFn: async () => {
      const result = await apiRequest<CategoryItem[]>('/api/reference/expense-categories')
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    // Seeded reference data. It changes when a migration runs, not while the sheet is open.
    staleTime: Infinity,
  })
}

function AddExpenseSheet({
  editing,
  onClose,
  reg,
}: {
  editing: ExpenseItem | 'new' | null
  onClose: () => void
  reg: string
}) {
  const existing = editing !== 'new' && editing !== null ? editing : null
  const { data: categories } = useCategories()
  const [v, setV] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const { toast } = useToast()

  // Seed the form once per open, from the row being edited (or blank for a new one).
  const [seededFor, setSeededFor] = useState<number | 'new' | null>(null)
  const key = existing?.id ?? (editing === 'new' ? ('new' as const) : null)
  if (key !== null && key !== seededFor) {
    setSeededFor(key)
    setV(
      existing === null
        ? {}
        : {
            entryDate: existing.entryDate,
            category: existing.category,
            amount: existing.amount.toFixed(2),
            subCategory: existing.subCategory ?? '',
            vendor: existing.vendor ?? '',
            mileage: existing.mileage === null ? '' : String(existing.mileage),
            paymentMethod: existing.paymentMethod ?? '',
            notes: existing.notes ?? '',
          },
    )
    setError(null)
  }

  const get = (k: string) => v[k] ?? ''
  const set = (k: string, value: string) => setV((p) => ({ ...p, [k]: value }))

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'expenses'] })
    await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'mileage'] })
    await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
    await queryClient.invalidateQueries({ queryKey: queryKeys.garage })
  }

  const mutation = useMutation({
    mutationFn: async () => {
      const body = {
        entryDate: get('entryDate'),
        category: get('category'),
        amount: Number(get('amount')),
        subCategory: get('subCategory') || null,
        vendor: get('vendor') || null,
        mileage: get('mileage') === '' ? null : Number(get('mileage')),
        paymentMethod: get('paymentMethod') || null,
        notes: get('notes') || null,
      }
      const result = await apiRequest<ExpenseItem>(
        existing === null
          ? `/api/vehicles/${encodeURIComponent(reg)}/expenses`
          : `/api/vehicles/${encodeURIComponent(reg)}/expenses/${existing.id}`,
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
      toast(existing === null ? 'Expense saved · the rollups recomputed' : 'Expense updated · the rollups recomputed')
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
      const result = await apiRequest<null>(`/api/vehicles/${encodeURIComponent(reg)}/expenses/${existing.id}`, {
        method: 'DELETE',
      })
      if (!result.ok) throw new ApiFailure(result.error)
    },
    onSuccess: async () => {
      await invalidate()
      toast('Expense deleted · the rollups recomputed')
      setV({})
      setSeededFor(null)
      onClose()
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Could not delete.'),
  })

  return (
    <Sheet
      open={editing !== null}
      onClose={onClose}
      title={existing === null ? 'Add expense' : 'Edit expense'}
      subtitle="rollups recompute from the rows"
      onSubmit={() => mutation.mutate()}
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
            {mutation.isPending ? 'Saving…' : existing === null ? 'Save expense' : 'Save changes'}
          </Btn>
        </>
      }
    >
      <Field label="Date">
        {(p) => <input type="date" value={get('entryDate')} onChange={(e) => set('entryDate', e.target.value)} {...p} />}
      </Field>

      <Field label="Category" hint="Fuel is absent — a fill writes its own row">
        {(p) => (
          <select value={get('category')} onChange={(e) => set('category', e.target.value)} {...p}>
            <option value="">Choose…</option>
            {/* Filtered on what the API refuses, not on a list of names typed here twice. A hand-typed fuel
                expense is exactly the workbook's lumped "fuel to date" row — the £163.16 gap — so the endpoint
                rejects it and this hides it. One fact, one place. */}
            {(categories ?? [])
              .filter((c) => !c.isMirrorOnly)
              .map((c) => (
                <option key={c.name} value={c.name}>
                  {c.name}
                </option>
              ))}
          </select>
        )}
      </Field>

      <Field label="Amount £">
        {(p) => <input type="text" inputMode="decimal" placeholder="57.91" value={get('amount')} onChange={(e) => set('amount', e.target.value)} {...p} />}
      </Field>

      <Field label="Vendor">
        {(p) => <input type="text" placeholder="K & P Motors" value={get('vendor')} onChange={(e) => set('vendor', e.target.value)} {...p} />}
      </Field>

      <Field label="Sub-category">
        {(p) => <input type="text" placeholder="Cambelt" value={get('subCategory')} onChange={(e) => set('subCategory', e.target.value)} {...p} />}
      </Field>

      <Field label="Paid by">
        {(p) => <input type="text" placeholder="Card" value={get('paymentMethod')} onChange={(e) => set('paymentMethod', e.target.value)} {...p} />}
      </Field>

      <Field label="Odometer" hint="optional — but it writes a mileage reading, like every other log">
        {(p) => <input type="text" inputMode="numeric" placeholder="80,712" value={get('mileage')} onChange={(e) => set('mileage', e.target.value)} {...p} />}
      </Field>

      <Field label="Notes" wide>
        {(p) => <input type="text" value={get('notes')} onChange={(e) => set('notes', e.target.value)} {...p} />}
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
