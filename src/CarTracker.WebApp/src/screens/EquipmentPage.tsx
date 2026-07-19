import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../api/client'
import { ApiFailure } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { Combobox } from '../components/Combobox'
import { ConfirmButton } from '../components/ConfirmButton'
import { Kv } from '../components/Kv'
import { Pill } from '../components/Pill'
import { Field, Sheet } from '../components/Sheet'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { todayIso } from '../lib/date'
import { fieldError, formError, reportApiError, type FieldErrors } from '../lib/formErrors'
import { recentValues } from '../lib/recentValues'
import type { PillTone } from '../lib/status'
import { usePlate } from '../lib/usePlate'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'

type Status = 'Owned' | 'OnOrder' | 'ToOrder'

interface EquipmentItem {
  id: number
  name: string
  category: string | null
  purchasedDate: string | null
  sourceVendor: string | null
  cost: number | null
  storedAt: string | null
  status: Status
  notes: string | null
}

/**
 * The stock axis, and it is a fourth one.
 *
 * Not due-status and not integrity: "I own this" / "it is coming" / "I should buy it" is about a thing's
 * existence, not its urgency or its trustworthiness. It borrows the tones because a reader should not learn a
 * new colour language per screen, and it keeps its own labels because the words are what carry the meaning.
 */
const STATUS: Record<Status, { label: string; tone: PillTone }> = {
  Owned: { label: 'Owned', tone: 'ok' },
  OnOrder: { label: 'On order', tone: 'soon' },
  ToOrder: { label: 'To order', tone: 'plain' },
}

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', minimumFractionDigits: 2 })

const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

/**
 * The equipment inventory — the kit that lives with the car.
 *
 * A list rather than a table: an item is a name and a few facts, and there are no figures to align down a
 * column. It groups by category because that is how a boot is packed and how you notice the jack is missing.
 */
export function EquipmentPage() {
  const reg = useVehicleReg()
  const plate = usePlate()
  const [editing, setEditing] = useState<EquipmentItem | 'new' | null>(null)

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['vehicle', reg, 'equipment'] as const,
    queryFn: async () => {
      const result = await apiRequest<EquipmentItem[]>(`/api/vehicles/${encodeURIComponent(reg)}/equipment`)
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
  })

  const items = data ?? []
  const owned = items.filter((i) => i.status === 'Owned')
  const spend = owned.reduce((sum, i) => sum + (i.cost ?? 0), 0)

  // Grouped by category, with the uncategorised last rather than under a made-up heading.
  const categories = [...new Set(items.map((i) => i.category ?? ''))].sort((a, b) =>
    a === '' ? 1 : b === '' ? -1 : a.localeCompare(b),
  )

  // History-derived vendor suggestions for the free-text "From" field.
  const sourceSuggestions = recentValues(items, (i) => i.sourceVendor).map((value) => ({ value }))

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="equipment"
      center={{ kind: 'action', icon: 'plus', label: 'Add item', onClick: () => setEditing('new') }}
      footer={
        <>
          What lives with the car, what is coming, and what is still on the list. <b>To order</b> is not a task:
          a task is work on the vehicle, and this is a shopping list — the two are different questions and get
          different screens.
        </>
      }
    >
      <PageHead
        eyebrow="Equipment · what lives with the car"
        title="Equipment"
        plate={plate}
        pmeta={
          data === undefined ? undefined : (
            <>
              <b>{owned.length} owned</b> · {items.filter((i) => i.status !== 'Owned').length} outstanding
              <br />
              {spend > 0 ? `${money(spend)} of kit` : 'nothing costed yet'}
            </>
          )
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <h2 className="panel-title">The inventory could not be loaded</h2>
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
          {items.length > 0 && (
            <Section>
              <Wrap>
                <SectionHead title="Inventory" rule={<>owned, on order, to order</>} />
                <Panel className="stats num">
                  <Kv label="Owned" value={String(owned.length)} note="in the car or the garage" />
                  <Kv
                    label="On order"
                    value={String(items.filter((i) => i.status === 'OnOrder').length)}
                    note="bought, not arrived"
                  />
                  <Kv
                    label="To order"
                    value={String(items.filter((i) => i.status === 'ToOrder').length)}
                    note="still on the list"
                  />
                  <Kv label="Kit value" value={spend > 0 ? money(spend) : '—'} note="owned items with a cost" />
                </Panel>
              </Wrap>
            </Section>
          )}

          <Section last>
            <Wrap>
              <SectionHead
                title="Items"
                rule={<>grouped by category</>}
                link={<Mark onClick={() => setEditing('new')}>Add item</Mark>}
              />
              {items.length === 0 ? (
                <Panel>
                  <p className="panel-empty">
                    Nothing listed. This is the kit that lives with the car — a jack, a tow rope, the OBD
                    reader — and what you have noticed you are missing.
                  </p>
                </Panel>
              ) : (
                categories.map((cat) => (
                  <Panel key={cat || 'uncategorised'} className="eqgroup">
                    <div className="eqhead">{cat || 'Uncategorised'}</div>
                    <ul className="eqlist">
                      {items
                        .filter((i) => (i.category ?? '') === cat)
                        .map((i) => (
                          <li key={i.id}>
                            <Pill tone={STATUS[i.status].tone}>{STATUS[i.status].label}</Pill>
                            <span className="eqname">
                              {i.name}
                              {i.storedAt !== null && <em>{i.storedAt}</em>}
                            </span>
                            <span className="eqmeta num">
                              {i.cost !== null && money(i.cost)}
                              {i.purchasedDate !== null && ` · ${shortDate(i.purchasedDate)}`}
                              {i.sourceVendor !== null && ` · ${i.sourceVendor}`}
                            </span>
                            <Mark onClick={() => setEditing(i)}>Edit</Mark>
                          </li>
                        ))}
                    </ul>
                  </Panel>
                ))
              )}
            </Wrap>
          </Section>
        </>
      )}

      <EquipmentSheet item={editing} onClose={() => setEditing(null)} reg={reg} sourceSuggestions={sourceSuggestions} />
    </AppShell>
  )
}

function EquipmentSheet({
  item,
  onClose,
  reg,
  sourceSuggestions,
}: {
  item: EquipmentItem | 'new' | null
  onClose: () => void
  reg: string
  sourceSuggestions: { value: string }[]
}) {
  const existing = item !== 'new' && item !== null ? item : null
  const [v, setV] = useState<Record<string, string>>({})
  const [errors, setErrors] = useState<FieldErrors>({})
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const get = (k: string, fallback = '') => v[k] ?? fallback
  const set = (k: string, value: string) => setV((p) => ({ ...p, [k]: value }))

  // The one field the server can flag on an item — anything else it returns falls to the footer banner.
  const FIELD_KEYS = ['name'] as const

  // An item needs a name; everything else is optional. Checked here so the answer is instant and beside the field.
  const validate = (): FieldErrors => {
    const e: FieldErrors = {}
    if (get('name', existing?.name ?? '').trim() === '') e['name'] = ['What is it?']
    return e
  }

  const submit = () => {
    const found = validate()
    setErrors(found)
    if (Object.keys(found).length === 0) mutation.mutate()
  }

  const mutation = useMutation({
    mutationFn: async () => {
      const cost = get('cost', existing?.cost?.toString() ?? '')
      const body = {
        name: get('name', existing?.name ?? ''),
        status: get('status', existing?.status ?? 'Owned'),
        category: get('category', existing?.category ?? '') || null,
        purchasedDate: get('purchasedDate', existing === null ? todayIso() : (existing.purchasedDate ?? '')) || null,
        sourceVendor: get('sourceVendor', existing?.sourceVendor ?? '') || null,
        cost: cost === '' ? null : Number(cost),
        storedAt: get('storedAt', existing?.storedAt ?? '') || null,
        notes: get('notes', existing?.notes ?? '') || null,
      }
      const result = await apiRequest<EquipmentItem>(
        existing === null
          ? `/api/vehicles/${encodeURIComponent(reg)}/equipment`
          : `/api/vehicles/${encodeURIComponent(reg)}/equipment/${existing.id}`,
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
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'equipment'] })
      toast(existing === null ? 'Item added' : 'Item saved')
      setV({})
      setErrors({})
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  const remove = useMutation({
    mutationFn: async () => {
      if (existing === null) return
      const result = await apiRequest<null>(`/api/vehicles/${encodeURIComponent(reg)}/equipment/${existing.id}`, {
        method: 'DELETE',
      })
      if (!result.ok) throw new ApiFailure(result.error)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'equipment'] })
      toast('Item removed from the inventory')
      setV({})
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  return (
    <Sheet
      open={item !== null}
      onClose={onClose}
      title={existing === null ? 'Add item' : 'Edit item'}
      subtitle="the kit that lives with the car"
      onSubmit={submit}
      footer={
        <>
          {existing !== null && (
            <ConfirmButton onConfirm={() => remove.mutate()} pending={remove.isPending} />
          )}
          <Btn type="submit" onClick={() => {}}>
            {mutation.isPending ? 'Saving…' : 'Save item'}
          </Btn>
        </>
      }
    >
      <Field label="Name" wide error={fieldError(errors, 'name')}>
        {(p) => (
          <input
            type="text"
            placeholder="Scissor jack"
            value={get('name', existing?.name ?? '')}
            onChange={(e) => set('name', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Status">
        {(p) => (
          <select
            value={get('status', existing?.status ?? 'Owned')}
            onChange={(e) => set('status', e.target.value)}
            {...p}
          >
            <option value="Owned">Owned</option>
            <option value="OnOrder">On order</option>
            <option value="ToOrder">To order</option>
          </select>
        )}
      </Field>

      <Field label="Category" hint="how the list groups">
        {(p) => (
          <input
            type="text"
            placeholder="Recovery"
            value={get('category', existing?.category ?? '')}
            onChange={(e) => set('category', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Stored at">
        {(p) => (
          <input
            type="text"
            placeholder="Boot floor"
            value={get('storedAt', existing?.storedAt ?? '')}
            onChange={(e) => set('storedAt', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Cost £">
        {(p) => (
          <input
            type="text"
            inputMode="decimal"
            placeholder="24.99"
            value={get('cost', existing?.cost?.toString() ?? '')}
            onChange={(e) => set('cost', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Bought">
        {(p) => (
          <input
            type="date"
            value={get('purchasedDate', existing === null ? todayIso() : (existing.purchasedDate ?? ''))}
            onChange={(e) => set('purchasedDate', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="From">
        {(p) => (
          <Combobox
            {...p}
            value={get('sourceVendor', existing?.sourceVendor ?? '')}
            onChange={(val) => set('sourceVendor', val)}
            suggestions={sourceSuggestions}
            placeholder="Halfords"
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
