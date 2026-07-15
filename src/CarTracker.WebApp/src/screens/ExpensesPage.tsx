import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import type { VehicleSummary } from '../api/client'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { Absent, DataTable, Sub, type Column } from '../components/DataTable'
import { Kv } from '../components/Kv'
import { IntegrityPill } from '../components/Pill'
import { Field, Sheet } from '../components/Sheet'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { AppLink } from '../lib/link'
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
  notes: string | null
}

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
  const [adding, setAdding] = useState(false)

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
        e.fuelEntryId === null ? (
          <Absent>entered</Absent>
        ) : (
          // Blue: this is a statement about where the datum came from, not about urgency. The row is a shadow
          // of a fill and the API refuses to edit it — §3.2's auto-mirroring is what closes the £163.16 gap,
          // and it only holds if the mirror cannot drift from its source.
          <IntegrityPill>From fuel</IntegrityPill>
        ),
    },
  ]

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="expenses"
      center={{ kind: 'action', icon: 'plus', label: 'Add expense', onClick: () => setAdding(true) }}
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
        plate={reg}
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
                link={<Mark onClick={() => setAdding(true)}>Add expense</Mark>}
              />
              {data.entries.length === 0 ? (
                <Panel>
                  <p className="panel-empty">
                    No expenses yet. Fills mirror in here automatically — everything else is entered.
                  </p>
                </Panel>
              ) : (
                <DataTable
                  columns={columns}
                  rows={[...data.entries].reverse()}
                  rowKey={(e) => e.id}
                  label="Expenses, newest first"
                />
              )}
            </Wrap>
          </Section>
        </>
      )}

      <AddExpenseSheet open={adding} onClose={() => setAdding(false)} reg={reg} />
    </AppShell>
  )
}

/** The 13 seeded categories, minus Fuel — see the note in the sheet. */
const CATEGORIES = [
  'Service',
  'Repairs',
  'Parts',
  'Tyres',
  'MOT',
  'Insurance',
  'Road tax',
  'Breakdown cover',
  'Cleaning',
  'Tools',
  'Parking',
  'Other',
]

function AddExpenseSheet({ open, onClose, reg }: { open: boolean; onClose: () => void; reg: string }) {
  const [v, setV] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const get = (k: string) => v[k] ?? ''
  const set = (k: string, value: string) => setV((p) => ({ ...p, [k]: value }))

  const mutation = useMutation({
    mutationFn: async () => {
      const result = await apiRequest<ExpenseItem>(`/api/vehicles/${encodeURIComponent(reg)}/expenses`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          entryDate: get('entryDate'),
          category: get('category'),
          amount: Number(get('amount')),
          subCategory: get('subCategory') || null,
          vendor: get('vendor') || null,
          mileage: get('mileage') === '' ? null : Number(get('mileage')),
          paymentMethod: get('paymentMethod') || null,
          notes: get('notes') || null,
        }),
      })
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'expenses'] })
      await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
      await queryClient.invalidateQueries({ queryKey: queryKeys.garage })
      toast('Expense saved · the rollups recomputed')
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
      title="Add expense"
      subtitle="rollups recompute from the rows"
      onSubmit={() => mutation.mutate()}
      footer={
        <Btn type="submit" onClick={() => {}}>
          {mutation.isPending ? 'Saving…' : 'Save expense'}
        </Btn>
      }
    >
      <Field label="Date">
        {(p) => <input type="date" value={get('entryDate')} onChange={(e) => set('entryDate', e.target.value)} {...p} />}
      </Field>

      <Field label="Category" hint="Fuel is absent — a fill writes its own row">
        {(p) => (
          <select value={get('category')} onChange={(e) => set('category', e.target.value)} {...p}>
            <option value="">Choose…</option>
            {/* Fuel is deliberately not here, and the API refuses it too: a hand-typed fuel expense is exactly
                the workbook's lumped "fuel to date" row, which is the £163.16 gap. Log the fill; the expense
                writes itself. */}
            {CATEGORIES.map((c) => (
              <option key={c} value={c}>
                {c}
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
