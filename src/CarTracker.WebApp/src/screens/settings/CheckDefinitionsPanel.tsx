import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../../api/client'
import { ApiFailure, queryKeys } from '../../api/queries'
import { Btn, Mark } from '../../components/Btn'
import { Cadence } from '../../components/Cadence'
import { Pill } from '../../components/Pill'
import { Panel, SectionHead } from '../../components/layout'
import { Field, Sheet } from '../../components/Sheet'
import { fieldError, formError, reportApiError, type FieldErrors } from '../../lib/formErrors'
import { useToast } from '../../shell/Toast'

interface Definition {
  id: number
  name: string
  cadenceLabel: string
  intervalDays: number
  guidance: string | null
  displayOrder: number
  isActive: boolean
}

const defsKey = (reg: string) => ['vehicle', reg, 'check-definitions'] as const

/**
 * Check definitions — the settings editor over the stored definitions, including retired ones.
 *
 * Reads `/checks/definitions` (not the status summary, which carries only active checks and no guidance) so the
 * panel can manage the three fields the design draws: **Active** — a toggle bound to `IsActive`, and the action
 * the panel leads with, because retiring keeps a check's logs where deleting cascades them; **guidance** and
 * **order**, edited inline. All of it drives the `PATCH` that already existed with nothing calling it.
 */
export function CheckDefinitionsPanel({ reg }: { reg: string }) {
  const [editing, setEditing] = useState<Definition | 'new' | null>(null)
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const { data, isPending } = useQuery({
    queryKey: defsKey(reg),
    queryFn: async () => {
      const r = await apiRequest<Definition[]>(`/api/vehicles/${encodeURIComponent(reg)}/checks/definitions`)
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
  })

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: defsKey(reg) })
    await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'checks'] })
    await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
    await queryClient.invalidateQueries({ queryKey: queryKeys.garage })
    await queryClient.invalidateQueries({ queryKey: queryKeys.reminders(reg) })
  }

  const patch = useMutation({
    mutationFn: async ({ id, body }: { id: number; body: Record<string, unknown> }) => {
      const r = await apiRequest<unknown>(`/api/vehicles/${encodeURIComponent(reg)}/checks/definitions/${id}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })
      if (!r.ok) throw new ApiFailure(r.error)
    },
    onSuccess: refresh,
  })

  const remove = useMutation({
    mutationFn: async (id: number) => {
      const r = await apiRequest<null>(`/api/vehicles/${encodeURIComponent(reg)}/checks/definitions/${id}`, { method: 'DELETE' })
      if (!r.ok) throw new ApiFailure(r.error)
    },
    onSuccess: async () => {
      await refresh()
      toast('Check deleted · its logs went with it')
    },
  })

  const defs = data ?? []
  const activeCount = defs.filter((d) => d.isActive).length

  return (
    <>
      <SectionHead
        title="Check definitions"
        rule={
          isPending ? (
            <>loading…</>
          ) : (
            <>
              {activeCount} active{defs.length > activeCount ? ` · ${defs.length - activeCount} retired` : ''} · order sets the sequence on the checks screen
            </>
          )
        }
        link={<Mark onClick={() => setEditing('new')}>Add a check</Mark>}
      />

      <Panel>
        {defs.length === 0 && !isPending && (
          <p style={{ padding: '18px', margin: 0, color: 'var(--muted)' }}>
            No checks defined, so the checks screen has nothing to show. Add them here — or the generic starter
            set arrives automatically with any car added from the garage.
          </p>
        )}

        {defs.length > 0 && (
          <>
            <div className="cdhead">
              <span>Check</span>
              <span className="ci">Cadence</span>
              <span className="ci">Days</span>
              <span className="ci">Active</span>
              <span className="ci">Order</span>
            </div>
            {defs.map((d) => (
              <div className="cdrow num" key={d.id}>
                <span className="cn">{d.name}</span>
                <span className="ci" data-label="Cadence">
                  <Cadence>{d.cadenceLabel}</Cadence>
                </span>
                <span className="ci" data-label="Interval days">{d.intervalDays}</span>
                <span className="ci" data-label="Active">
                  {/* Retire is a toggle, not a delete: it drops the check from the active count and the 18 while
                      its logs survive. The button says the ACTION; the pill says the STATE. */}
                  <Mark
                    onClick={() => patch.mutate({ id: d.id, body: { isActive: !d.isActive } })}
                    aria-label={d.isActive ? `Retire ${d.name}` : `Reactivate ${d.name}`}
                  >
                    {d.isActive ? <Pill tone="ok">Active</Pill> : <Pill tone="plain">Retired</Pill>}
                  </Mark>
                </span>
                <span className="ci" data-label="Order">
                  {d.displayOrder}
                  <Mark onClick={() => setEditing(d)}>Edit</Mark>
                </span>
              </div>
            ))}
          </>
        )}
      </Panel>

      <DefinitionSheet
        reg={reg}
        editing={editing}
        onClose={() => setEditing(null)}
        onSaved={refresh}
        onDelete={(id) => {
          remove.mutate(id)
          setEditing(null)
        }}
      />
    </>
  )
}

function DefinitionSheet({
  reg,
  editing,
  onClose,
  onSaved,
  onDelete,
}: {
  reg: string
  editing: Definition | 'new' | null
  onClose: () => void
  onSaved: () => Promise<void>
  onDelete: (id: number) => void
}) {
  const existing = editing !== null && editing !== 'new' ? editing : null
  const [draft, setDraft] = useState({ name: '', cadenceLabel: '', intervalDays: '', guidance: '', displayOrder: '' })
  const [errors, setErrors] = useState<FieldErrors>({})
  const { toast } = useToast()

  const FIELD_KEYS = ['name', 'cadencelabel', 'intervaldays'] as const

  const [seededFor, setSeededFor] = useState<number | 'new' | null>(null)
  const key = existing?.id ?? (editing === 'new' ? ('new' as const) : null)
  if (key !== null && key !== seededFor) {
    setSeededFor(key)
    setDraft({
      name: existing?.name ?? '',
      cadenceLabel: existing?.cadenceLabel ?? '',
      intervalDays: existing ? String(existing.intervalDays) : '',
      guidance: existing?.guidance ?? '',
      displayOrder: existing ? String(existing.displayOrder) : '',
    })
    setErrors({})
  }

  const save = useMutation({
    mutationFn: async () => {
      const body: Record<string, unknown> = {
        name: draft.name.trim(),
        cadenceLabel: draft.cadenceLabel.trim(),
        intervalDays: Number(draft.intervalDays),
        guidance: draft.guidance.trim() === '' ? null : draft.guidance.trim(),
      }
      if (draft.displayOrder.trim() !== '') body.displayOrder = Number(draft.displayOrder)
      const path = existing
        ? `/api/vehicles/${encodeURIComponent(reg)}/checks/definitions/${existing.id}`
        : `/api/vehicles/${encodeURIComponent(reg)}/checks/definitions`

      const r = await apiRequest<unknown>(path, {
        method: existing ? 'PATCH' : 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })
      if (!r.ok) throw new ApiFailure(r.error)
    },
    onSuccess: async () => {
      await onSaved()
      toast(existing ? 'Check updated' : `"${draft.name}" added · status derives from its next log`)
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  const validate = (): FieldErrors => {
    const e: FieldErrors = {}
    if (draft.name.trim() === '') e['name'] = ['A check needs a name.']
    if (draft.cadenceLabel.trim() === '') e['cadencelabel'] = ['A cadence label — it is what the screen shows.']
    if (!(Number(draft.intervalDays) > 0)) e['intervaldays'] = ['An interval of at least one day.']
    return e
  }

  const submit = () => {
    const found = validate()
    setErrors(found)
    if (Object.keys(found).length === 0) save.mutate()
  }

  return (
    <Sheet
      open={editing !== null}
      onClose={onClose}
      title={existing ? 'Edit check' : 'Add a check'}
      subtitle="status is never stored — it derives from the last log and this interval"
      onSubmit={submit}
      footer={
        <>
          {existing && (
            // Delete cascades the logs — the rare "should never have existed" case. Retire (the Active toggle
            // in the row) is the ordinary way to remove a check from the 18.
            <Btn variant="ghost" onClick={() => onDelete(existing.id)}>
              Delete
            </Btn>
          )}
          <Btn onClick={() => {}} type="submit">
            {save.isPending ? 'Saving…' : 'Save'}
          </Btn>
        </>
      }
    >
      <Field label="Name" wide error={fieldError(errors, 'name')}>
        {(p) => <input type="text" placeholder="Oil filler cap underside" autoFocus value={draft.name} onChange={(e) => setDraft((d) => ({ ...d, name: e.target.value }))} {...p} />}
      </Field>
      <Field label="Cadence" error={fieldError(errors, 'cadencelabel')} hint="what the screen shows — prose, so '3–4 weekly' is fine">
        {(p) => <input type="text" placeholder="Weekly" value={draft.cadenceLabel} onChange={(e) => setDraft((d) => ({ ...d, cadenceLabel: e.target.value }))} {...p} />}
      </Field>
      <Field label="Interval days" error={fieldError(errors, 'intervaldays')} hint="what the status actually derives from">
        {(p) => <input type="text" inputMode="numeric" placeholder="7" value={draft.intervalDays} onChange={(e) => setDraft((d) => ({ ...d, intervalDays: e.target.value }))} {...p} />}
      </Field>
      <Field label="Order" hint="the sequence on the checks screen">
        {(p) => <input type="text" inputMode="numeric" placeholder="10" value={draft.displayOrder} onChange={(e) => setDraft((d) => ({ ...d, displayOrder: e.target.value }))} {...p} />}
      </Field>
      <Field label="Guidance" wide hint="what to look for — shown under the name on the checks screen">
        {(p) => <input type="text" placeholder="mayo residue = possible head gasket" value={draft.guidance} onChange={(e) => setDraft((d) => ({ ...d, guidance: e.target.value }))} {...p} />}
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
