import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../../api/client'
import { ApiFailure, queryKeys, useGarage } from '../../api/queries'
import { useStarterChecks, useVehicleChecks } from '../../api/reference'
import { Btn, Mark } from '../../components/Btn'
import { Cadence } from '../../components/Cadence'
import { CheckSelectList, type SelectableCheck } from '../../components/CheckSelectList'
import { Pill } from '../../components/Pill'
import { Panel, SectionHead } from '../../components/layout'
import { Field, Select, Sheet } from '../../components/Sheet'
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
  const [addingSet, setAddingSet] = useState(false)
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
        link={
          <>
            <Mark onClick={() => setAddingSet(true)}>Add checks…</Mark>
            <span style={{ color: 'var(--faint)', margin: '0 8px' }}>·</span>
            <Mark onClick={() => setEditing('new')}>Add a check</Mark>
          </>
        }
      />

      <Panel>
        {defs.length === 0 && !isPending && (
          <p style={{ padding: '18px', margin: 0, color: 'var(--muted)' }}>
            No checks defined, so the checks screen has nothing to show. <Mark onClick={() => setAddingSet(true)}>Add
            checks…</Mark> to pull in the generic starter set (or copy another car's), or add them one at a time.
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

      <AddCheckSetSheet
        reg={reg}
        open={addingSet}
        existingNames={defs.map((d) => d.name)}
        onClose={() => setAddingSet(false)}
        onAdded={refresh}
      />
    </>
  )
}

/**
 * Add a whole set of checks to this vehicle — the generic starter set, or a copy of another car's active checks
 * — without leaving the settings screen. The same `<CheckSelectList>` the add-vehicle sheet uses, with the
 * checks this vehicle already has shown locked ("already added"), so only the missing ones can be picked. The
 * server (`POST …/add-set`) diffs by name as a backstop and reports what it skipped.
 */
function AddCheckSetSheet({
  reg,
  open,
  existingNames,
  onClose,
  onAdded,
}: {
  reg: string
  open: boolean
  existingNames: string[]
  onClose: () => void
  onAdded: () => Promise<void>
}) {
  const [source, setSource] = useState<'GenericStarterSet' | 'CopyFromVehicle'>('GenericStarterSet')
  const [copyFromId, setCopyFromId] = useState<number | null>(null)
  const [deselected, setDeselected] = useState<Set<string>>(new Set())
  const [error, setError] = useState<string | null>(null)
  const { toast } = useToast()

  // Other vehicles are copy sources. `reg` is the route slug and a garage row carries the real plate, so compare
  // normalised (spaces stripped, upper-cased) to keep the vehicle itself out of its own copy list.
  const norm = (s: string) => s.replace(/\s/g, '').toUpperCase()
  const { data: garage } = useGarage()
  const sources = (Array.isArray(garage) ? garage : []).filter((v) => norm(v.registration) !== norm(reg))
  const canCopy = sources.length > 0
  const isCopy = source === 'CopyFromVehicle' && canCopy

  const effectiveCopyId = isCopy ? (copyFromId ?? sources[0]?.vehicleId ?? null) : null
  const copySourceReg = sources.find((v) => v.vehicleId === effectiveCopyId)?.registration ?? ''

  const { data: starterChecks } = useStarterChecks(open && !isCopy)
  const { data: copyChecks } = useVehicleChecks(copySourceReg, open && isCopy)
  const checks: SelectableCheck[] = isCopy ? (copyChecks ?? []).filter((d) => d.isActive) : (starterChecks ?? [])

  const locked = new Set(existingNames)
  // Exactly what will be added: shown, not locked, not deselected. Sent explicitly so locked names never ride along.
  const keptNames = checks.filter((c) => !locked.has(c.name) && !deselected.has(c.name)).map((c) => c.name)

  const toggle = (name: string) =>
    setDeselected((s) => {
      const n = new Set(s)
      if (n.has(name)) n.delete(name)
      else n.add(name)
      return n
    })

  const reset = () => {
    setSource('GenericStarterSet')
    setCopyFromId(null)
    setDeselected(new Set())
    setError(null)
  }

  const add = useMutation({
    mutationFn: async () => {
      const body = {
        source,
        selectedCheckNames: keptNames,
        copyFromVehicleId: isCopy ? effectiveCopyId : undefined,
      }
      const r = await apiRequest<{ added: unknown[]; skipped: string[] }>(
        `/api/vehicles/${encodeURIComponent(reg)}/checks/definitions/add-set`,
        { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) },
      )
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
    onSuccess: async (res) => {
      await onAdded()
      const added = res.added.length
      const skipped = res.skipped.length
      toast(`Added ${added} check${added === 1 ? '' : 's'}${skipped > 0 ? ` · ${skipped} already present` : ''}`)
      reset()
      onClose()
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Could not add the checks.'),
  })

  return (
    <Sheet
      open={open}
      onClose={() => {
        reset()
        onClose()
      }}
      title="Add checks"
      subtitle="the generic set or another car's — only the ones this car does not already have"
      onSubmit={() => {
        if (keptNames.length > 0) add.mutate()
      }}
      footer={
        <Btn type="submit" onClick={() => {}}>
          {add.isPending
            ? 'Adding…'
            : keptNames.length > 0
              ? `Add ${keptNames.length} check${keptNames.length === 1 ? '' : 's'}`
              : 'Add checks'}
        </Btn>
      }
    >
      <Field label="From" wide hint="the generic starter set, or a copy of another car's active checks">
        {(p) => (
          <Select
            value={source}
            onChange={(e) => {
              setSource(e.target.value as 'GenericStarterSet' | 'CopyFromVehicle')
              setDeselected(new Set())
            }}
            {...p}
          >
            <option value="GenericStarterSet">Generic starter set</option>
            {canCopy && <option value="CopyFromVehicle">Copy from another vehicle</option>}
          </Select>
        )}
      </Field>

      {isCopy && (
        <Field label="Copy from" wide>
          {(p) => (
            <Select
              value={String(effectiveCopyId ?? '')}
              onChange={(e) => {
                setCopyFromId(Number(e.target.value))
                setDeselected(new Set())
              }}
              {...p}
            >
              {sources.map((v) => (
                <option key={v.vehicleId} value={v.vehicleId}>
                  {v.registration} — {v.name}
                </option>
              ))}
            </Select>
          )}
        </Field>
      )}

      {checks.length > 0 && (
        <CheckSelectList checks={checks} deselected={deselected} onToggle={toggle} locked={locked} header="adding to this car" />
      )}

      {error !== null && (
        <div className="field wide">
          <span className="hint err" role="alert">
            {error}
          </span>
        </div>
      )}
    </Sheet>
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
