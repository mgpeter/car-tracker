import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../../api/client'
import { ApiFailure, queryKeys } from '../../api/queries'
import { Btn, Mark } from '../../components/Btn'
import { Pill } from '../../components/Pill'
import { Panel, SectionHead } from '../../components/layout'
import { Field, Sheet } from '../../components/Sheet'
import { fieldError, formError, reportApiError, type FieldErrors } from '../../lib/formErrors'
import { useToast } from '../../shell/Toast'

/** A reference-list row, across all three list kinds. */
interface RefRow {
  name: string
  referenceCount: number
  /** Garages only. */
  contact?: string | null
  notes?: string | null
  /** Categories only. */
  isSystem?: boolean
  isMirrorOnly?: boolean
}

interface ListConfig {
  kind: 'garages' | 'wash-locations' | 'expense-categories'
  title: string
  rule: string
  /** The reference noun for counts and copy. */
  noun: string
  /** Whether the list supports creating rows here (categories are seeded-closed). */
  canAdd: boolean
  /** Whether a row shows a Contact field (garages). */
  hasContact: boolean
  /** Whether a row shows a Notes field (garages, wash locations). */
  hasNotes: boolean
}

const CONFIGS: ListConfig[] = [
  { kind: 'garages', title: 'Garages', rule: 'used by service records, tasks and the default garage', noun: 'record', canAdd: true, hasContact: true, hasNotes: true },
  { kind: 'wash-locations', title: 'Wash locations', rule: 'used by the wash log', noun: 'wash', canAdd: true, hasContact: false, hasNotes: true },
  { kind: 'expense-categories', title: 'Expense categories', rule: 'seeded closed — rename for display; Fuel is locked', noun: 'record', canAdd: false, hasContact: false, hasNotes: false },
]

const base = (kind: ListConfig['kind']) => `/api/reference/${kind}`

/**
 * The reference lists — garages, wash locations, expense categories — made editable.
 *
 * These are keyed by name and pointed at by foreign keys that look like free text, so the guards are the point:
 * a delete of a referenced row is refused with the count (or re-homes its records first), a system category is
 * locked, and Fuel is never offered because the fuel-to-expense mirror resolves it by name. All of that lives
 * server-side (ReferenceListEditor); this panel surfaces it.
 */
export function ReferenceListsPanel() {
  return (
    <>
      {CONFIGS.map((config) => (
        <RefList key={config.kind} config={config} />
      ))}
    </>
  )
}

function RefList({ config }: { config: ListConfig }) {
  const [editing, setEditing] = useState<RefRow | 'new' | null>(null)
  const queryClient = useQueryClient()

  const { data, isPending } = useQuery({
    queryKey: ['reference', config.kind] as const,
    queryFn: async () => {
      const r = await apiRequest<RefRow[]>(base(config.kind))
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
  })

  const refresh = async () => {
    await queryClient.invalidateQueries({ queryKey: ['reference', config.kind] })
    // The pick-lists and the garage card project the same names.
    await queryClient.invalidateQueries({ queryKey: queryKeys.garage })
  }

  const rows = data ?? []

  return (
    <>
      <SectionHead
        title={config.title}
        rule={isPending ? <>loading…</> : <>{config.rule}</>}
        link={config.canAdd ? <Mark onClick={() => setEditing('new')}>Add</Mark> : undefined}
      />
      <Panel>
        {rows.length === 0 && !isPending && (
          <p style={{ padding: '18px', margin: 0, color: 'var(--muted)' }}>
            None yet. {config.canAdd ? 'Add one, or it is created the first time a record names it.' : 'These are seeded.'}
          </p>
        )}
        {rows.map((row) => (
          <div className="setrow num" key={row.name}>
            <span className="sk">{row.name}</span>
            <span className="sv">
              {row.referenceCount > 0 ? `${row.referenceCount} ${config.noun}${row.referenceCount === 1 ? '' : 's'}` : 'unused'}
              {row.isMirrorOnly && <i>system · the fuel mirror files here</i>}
              {row.isSystem && !row.isMirrorOnly && <i>system · seeded, undeletable</i>}
            </span>
            {row.isMirrorOnly ? (
              <Pill tone="plain">Locked</Pill>
            ) : (
              <Mark onClick={() => setEditing(row)}>Edit</Mark>
            )}
          </div>
        ))}
      </Panel>

      <RowSheet
        config={config}
        editing={editing}
        rows={rows}
        onClose={() => setEditing(null)}
        onSaved={refresh}
      />
    </>
  )
}

function RowSheet({
  config,
  editing,
  rows,
  onClose,
  onSaved,
}: {
  config: ListConfig
  editing: RefRow | 'new' | null
  rows: RefRow[]
  onClose: () => void
  onSaved: () => Promise<void>
}) {
  const existing = editing !== null && editing !== 'new' ? editing : null
  const [draft, setDraft] = useState({ name: '', contact: '', notes: '' })
  const [rehomeTo, setRehomeTo] = useState('')
  const [errors, setErrors] = useState<FieldErrors>({})
  const { toast } = useToast()

  // The name is the only field the server flags; a delete-guard 409 ("3 records use this") carries no field and
  // falls to the footer banner.
  const FIELD_KEYS = ['name'] as const

  const [seededFor, setSeededFor] = useState<string | 'new' | null>(null)
  const key = existing?.name ?? (editing === 'new' ? ('new' as const) : null)
  if (key !== null && key !== seededFor) {
    setSeededFor(key)
    setDraft({ name: existing?.name ?? '', contact: existing?.contact ?? '', notes: existing?.notes ?? '' })
    setRehomeTo('')
    setErrors({})
  }

  // The Fuel category is rename-locked server-side; disable its name field so the UI says so before the save.
  const renameLocked = existing?.isMirrorOnly === true
  const systemUndeletable = existing?.isSystem === true
  const others = rows.filter((r) => r.name !== existing?.name)

  const save = useMutation({
    mutationFn: async () => {
      const name = draft.name.trim()
      if (existing === null) {
        const body: Record<string, unknown> = { name }
        if (config.hasContact) body.contact = draft.contact.trim() || null
        if (config.hasNotes) body.notes = draft.notes.trim() || null
        const r = await apiRequest<unknown>(base(config.kind), {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body),
        })
        if (!r.ok) throw new ApiFailure(r.error)
        return 'created' as const
      }
      const body: Record<string, unknown> = {}
      if (!renameLocked && name !== existing.name) body.name = name
      if (config.hasContact) body.contact = draft.contact.trim() || null
      if (config.hasNotes) body.notes = draft.notes.trim() || null
      const r = await apiRequest<unknown>(`${base(config.kind)}/${encodeURIComponent(existing.name)}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })
      if (!r.ok) throw new ApiFailure(r.error)
      return 'saved' as const
    },
    onSuccess: async (kind) => {
      await onSaved()
      toast(kind === 'created' ? `"${draft.name.trim()}" added` : 'Saved · records keep their reference')
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  const remove = useMutation({
    mutationFn: async () => {
      if (existing === null) return
      const query = existing.referenceCount > 0 ? `?rehomeTo=${encodeURIComponent(rehomeTo)}` : ''
      const r = await apiRequest<null>(`${base(config.kind)}/${encodeURIComponent(existing.name)}${query}`, {
        method: 'DELETE',
      })
      if (!r.ok) throw new ApiFailure(r.error)
    },
    onSuccess: async () => {
      await onSaved()
      toast(existing && existing.referenceCount > 0 ? 'Removed · its records were re-homed' : 'Removed · existing records keep their saved value')
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  const submit = () => {
    if (draft.name.trim() === '') return setErrors({ name: ['A name is required.'] })
    save.mutate()
  }

  const canDelete = existing !== null && !systemUndeletable
  const needsRehome = existing !== null && existing.referenceCount > 0

  const tryDelete = () => {
    // The re-home guard has no single field to attach to — it is about the delete as a whole, so it goes to the
    // banner. This is the same alert the server's 409 count lands in.
    if (needsRehome && rehomeTo === '')
      return setErrors({ _: [`${existing.referenceCount} record${existing.referenceCount === 1 ? '' : 's'} use this — pick where they go first.`] })
    remove.mutate()
  }

  return (
    <Sheet
      open={editing !== null}
      onClose={onClose}
      title={existing === null ? `Add ${config.noun === 'wash' ? 'wash location' : config.title.slice(0, -1).toLowerCase()}` : existing.name}
      subtitle={config.rule}
      onSubmit={submit}
      footer={
        <>
          {canDelete && (
            <Btn variant="ghost" onClick={tryDelete}>
              {remove.isPending ? 'Removing…' : needsRehome ? 'Re-home & delete' : 'Delete'}
            </Btn>
          )}
          <Btn onClick={() => {}} type="submit">
            {save.isPending ? 'Saving…' : 'Save'}
          </Btn>
        </>
      }
    >
      <Field label="Name" wide error={fieldError(errors, 'name')} hint={renameLocked ? 'the fuel mirror resolves this by name — it cannot be renamed' : 'renaming re-points every record that uses it'}>
        {(p) => (
          <input
            type="text"
            value={draft.name}
            disabled={renameLocked}
            onChange={(e) => setDraft((d) => ({ ...d, name: e.target.value }))}
            {...p}
          />
        )}
      </Field>

      {config.hasContact && (
        <Field label="Contact">
          {(p) => <input type="text" placeholder="01234 567890" value={draft.contact} onChange={(e) => setDraft((d) => ({ ...d, contact: e.target.value }))} {...p} />}
        </Field>
      )}
      {config.hasNotes && (
        <Field label="Notes" wide>
          {(p) => <input type="text" value={draft.notes} onChange={(e) => setDraft((d) => ({ ...d, notes: e.target.value }))} {...p} />}
        </Field>
      )}

      {needsRehome && (
        <Field label="Re-home to" wide hint={`${existing.referenceCount} ${config.noun}${existing.referenceCount === 1 ? '' : 's'} use this — pick where they go before deleting`}>
          {(p) => (
            <select value={rehomeTo} onChange={(e) => setRehomeTo(e.target.value)} {...p}>
              <option value="">Choose…</option>
              {others.map((r) => (
                <option key={r.name} value={r.name}>{r.name}</option>
              ))}
            </select>
          )}
        </Field>
      )}

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
