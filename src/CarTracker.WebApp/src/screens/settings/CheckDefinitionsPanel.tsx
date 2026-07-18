import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest, type VehicleSummary } from '../../api/client'
import { ApiFailure, queryKeys } from '../../api/queries'
import { Btn, Mark } from '../../components/Btn'
import { Cadence } from '../../components/Cadence'
import { Panel, SectionHead } from '../../components/layout'
import { Field, Sheet } from '../../components/Sheet'
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

/** Reads the definitions off the checks summary — the same shape the checks screen renders. */
type Checks = VehicleSummary['checks']

const queryKey = (reg: string) => ['vehicle', reg, 'checks'] as const

/**
 * Check definitions.
 *
 * This is the only screen that can create one. `CheckDefinition` is vehicle-scoped and unseeded, so until
 * `VehicleFactory` grew its starter set and this panel existed, a vehicle's checks screen showed 0 of 18 with
 * no way to change that. BT53 predates the starter set and has none — this is where its 18 get typed in.
 */
export function CheckDefinitionsPanel({ reg }: { reg: string }) {
  const [editing, setEditing] = useState<Definition | 'new' | null>(null)
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const { data, isPending } = useQuery({
    queryKey: queryKey(reg),
    queryFn: async () => {
      const r = await apiRequest<Checks>(`/api/vehicles/${encodeURIComponent(reg)}/checks`)
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
  })

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: queryKey(reg) })
    await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
    // The garage card counts checks too.
    await queryClient.invalidateQueries({ queryKey: queryKeys.garage })
  }

  const remove = useMutation({
    mutationFn: async (id: number) => {
      const r = await apiRequest<null>(`/api/vehicles/${encodeURIComponent(reg)}/checks/definitions/${id}`, {
        method: 'DELETE',
      })
      if (!r.ok) throw new ApiFailure(r.error)
    },
    onSuccess: async () => {
      await refresh()
      toast('Check deleted · its logs went with it')
    },
  })

  const checks = data?.checks ?? []

  return (
    <>
      <SectionHead
        title="Check definitions"
        rule={
          isPending ? (
            <>loading…</>
          ) : (
            <>
              {checks.length} defined · order sets the sequence on the checks screen
            </>
          )
        }
        link={<Mark onClick={() => setEditing('new')}>Add a check</Mark>}
      />

      <Panel>
        {checks.length === 0 && !isPending && (
          // Not a shrug. This vehicle predates the starter set, and the checks screen is empty *because* this
          // list is — saying so beats an empty table that looks like a loading failure.
          <p style={{ padding: '18px', margin: 0, color: 'var(--muted)' }}>
            No checks defined, so the checks screen has nothing to show. Add them here — or the generic starter
            set arrives automatically with any car added from the garage.
          </p>
        )}

        {checks.length > 0 && (
          <>
            <div className="cdhead">
              <span>Check</span>
              <span className="ci">Cadence</span>
              <span className="ci">Days</span>
              <span className="ci">Active</span>
              <span className="ci">Order</span>
            </div>
            {checks.map((c) => (
              <div className="cdrow num" key={c.checkDefinitionId}>
                <span className="cn">{c.name}</span>
                <span className="ci" data-label="Cadence">
                  <Cadence>{c.cadenceLabel}</Cadence>
                </span>
                <span className="ci" data-label="Interval days">{c.intervalDays}</span>
                <span className="ci" data-label="Active">
                  {/* The design renders a bare ✓ with no ✗ counterpart, so nothing distinguishes true from
                      absent — and it hides the column on mobile, taking the value with it. Text, both ways. */}
                  <span className={c.status === 'NeverLogged' ? 'never' : ''}>Yes</span>
                </span>
                <span className="ci">
                  <Mark
                    onClick={() => {
                      const found = checks.find((x) => x.checkDefinitionId === c.checkDefinitionId)
                      if (found) {
                        setEditing({
                          id: found.checkDefinitionId,
                          name: found.name,
                          cadenceLabel: found.cadenceLabel,
                          intervalDays: found.intervalDays,
                          guidance: null,
                          displayOrder: 0,
                          isActive: true,
                        })
                      }
                    }}
                  >
                    Edit
                  </Mark>
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
  const [draft, setDraft] = useState<{ name: string; cadenceLabel: string; intervalDays: string; guidance: string }>({
    name: '',
    cadenceLabel: '',
    intervalDays: '',
    guidance: '',
  })
  const [error, setError] = useState<string | null>(null)
  const { toast } = useToast()

  // Seed the form the first time a given definition opens it.
  const [seededFor, setSeededFor] = useState<number | 'new' | null>(null)
  const key = existing?.id ?? (editing === 'new' ? ('new' as const) : null)
  if (key !== null && key !== seededFor) {
    setSeededFor(key)
    setDraft({
      name: existing?.name ?? '',
      cadenceLabel: existing?.cadenceLabel ?? '',
      intervalDays: existing ? String(existing.intervalDays) : '',
      guidance: existing?.guidance ?? '',
    })
    setError(null)
  }

  const save = useMutation({
    mutationFn: async () => {
      const body = {
        name: draft.name.trim(),
        cadenceLabel: draft.cadenceLabel.trim(),
        intervalDays: Number(draft.intervalDays),
        guidance: draft.guidance.trim() === '' ? null : draft.guidance.trim(),
      }
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
    onError: (e) => setError(e instanceof Error ? e.message : 'Could not save.'),
  })

  const submit = () => {
    if (draft.name.trim() === '') return setError('A check needs a name.')
    if (draft.cadenceLabel.trim() === '') return setError('A cadence label — it is what the screen shows.')
    if (!(Number(draft.intervalDays) > 0)) return setError('An interval of at least one day.')
    save.mutate()
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
      <Field label="Name" wide>
        {(p) => (
          <input
            type="text"
            placeholder="Oil filler cap underside"
            autoFocus
            value={draft.name}
            onChange={(e) => setDraft((d) => ({ ...d, name: e.target.value }))}
            {...p}
          />
        )}
      </Field>
      <Field label="Cadence" hint="what the screen shows — prose, so '3–4 weekly' is fine">
        {(p) => (
          <input
            type="text"
            placeholder="Weekly"
            value={draft.cadenceLabel}
            onChange={(e) => setDraft((d) => ({ ...d, cadenceLabel: e.target.value }))}
            {...p}
          />
        )}
      </Field>
      <Field label="Interval days" hint="what the status actually derives from">
        {/* Two fields for what looks like one thing, because they are two things. The label is prose the
            screen shows; the interval is the number the status computes from. The design's "3–4 weekly / 21–28"
            is exactly why: a range reads well and cannot be counted. */}
        {(p) => (
          <input
            type="text"
            inputMode="numeric"
            placeholder="7"
            value={draft.intervalDays}
            onChange={(e) => setDraft((d) => ({ ...d, intervalDays: e.target.value }))}
            {...p}
          />
        )}
      </Field>
      <Field label="Guidance" wide hint="what to look for — shown under the name on the checks screen">
        {(p) => (
          <input
            type="text"
            placeholder="mayo residue = possible head gasket"
            value={draft.guidance}
            onChange={(e) => setDraft((d) => ({ ...d, guidance: e.target.value }))}
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
