import { useState } from 'react'
import type { JsonSchemaProperty } from '../api/chat'
import { useReferenceSuggestions } from '../api/reference'
import { Btn } from '../components/Btn'
import { Combobox } from '../components/Combobox'
import { ConfirmButton } from '../components/ConfirmButton'
import { Field, Select } from '../components/Sheet'
import type { ChatDraft } from './useChat'

/**
 * A proposed write, as a form the owner can correct before it happens.
 *
 * It looks like an add sheet because it **is** one, pre-filled — the same `Field`, the same `Combobox` on a
 * place name, the same two-step `ConfirmButton` to throw it away. What it is not is a sheet: a modal would
 * hide the sentence above it explaining what the assistant read off the photograph, and that sentence is the
 * thing the owner is checking the form against.
 *
 * **Every field comes from the tool's own JSON Schema**, not from a form written per tool. There are thirty
 * write tools; thirty hand-written cards would drift from their tools the week after they were written, and
 * the drift would show up as a field the owner cannot fill rather than as a broken build.
 */
export function DraftCard({
  draft,
  busy,
  errors,
  embedded = false,
  onSave,
  onChange,
  onDiscard,
}: {
  draft: ChatDraft
  busy: boolean
  errors: Record<string, string[]> | undefined
  /**
   * Rendered inside a list of drafts, where the footer belongs to the list: the owner saves the batch, not
   * each row, so a row with its own Save button would offer a second way to answer half a turn.
   */
  embedded?: boolean
  onSave?: (values: Record<string, unknown>) => void
  /** Edits, as they happen — the list holds them until the batch is saved. */
  onChange?: (values: Record<string, unknown>) => void
  onDiscard?: () => void
}) {
  const [values, setValues] = useState<Record<string, string>>(() => initial(draft))

  const properties = Object.entries(draft.schema?.properties ?? {})
  const required = new Set(draft.schema?.required ?? [])

  // What the assistant actually read, plus anything the tool insists on, is the card. The rest of a thirty-
  // parameter tool folds away: `add_vehicle` alone has fourteen optional fields, and a form that opens with
  // eleven empty boxes buries the three figures the owner is here to check.
  const shown = properties.filter(([name]) => required.has(name) || filled(draft.values[name]))
  const folded = properties.filter(([name]) => !required.has(name) && !filled(draft.values[name]))

  const garages = useReferenceSuggestions('garages')
  const washes = useReferenceSuggestions('wash-locations')

  const set = (name: string, value: string) =>
    setValues((previous) => {
      const next = { ...previous, [name]: value }
      onChange?.(coerce(next, draft.schema?.properties ?? {}, required))
      return next
    })

  return (
    <div className={embedded ? 'draft draft-embedded' : 'draft'}>
      {!embedded && (
        <div className="draft-head">
          <span className="draft-eyebrow">Ready to save</span>
          <h3>{draft.title}</h3>
          {/* The tool name, quietly. It is what the audit trail will say, and hiding it would make the row's
              attribution the one thing the owner never saw. */}
          <span className="draft-tool">{draft.tool}</span>
        </div>
      )}

      <div className="f-grid draft-grid">
        {shown.map(([name, property]) => (
          <DraftField
            key={name}
            name={name}
            property={property}
            value={values[name] ?? ''}
            error={errors?.[name]?.[0]}
            onChange={(v) => set(name, v)}
            garages={garages}
            washes={washes}
          />
        ))}
      </div>

      {folded.length > 0 && (
        <details className="draft-more">
          <summary>{folded.length} more field{folded.length === 1 ? '' : 's'}</summary>
          <div className="f-grid draft-grid">
            {folded.map(([name, property]) => (
              <DraftField
                key={name}
                name={name}
                property={property}
                value={values[name] ?? ''}
                error={errors?.[name]?.[0]}
                onChange={(v) => set(name, v)}
                garages={garages}
                washes={washes}
              />
            ))}
          </div>
        </details>
      )}

      {!embedded && (
        <div className="draft-foot">
          <ConfirmButton label="Discard" confirmLabel="Discard it?" onConfirm={() => onDiscard?.()} />
          <Btn
            onClick={() => onSave?.(coerce(values, draft.schema?.properties ?? {}, required))}
            disabled={busy}
          >
            Save it
          </Btn>
        </div>
      )}
    </div>
  )
}

/** A value the assistant supplied, as opposed to one it left for the owner. */
function filled(value: unknown): boolean {
  return value !== undefined && value !== null && String(value).trim() !== ''
}

interface DraftFieldProps {
  name: string
  property: JsonSchemaProperty
  value: string
  error: string | undefined
  onChange: (value: string) => void
  garages: { value: string; hint?: string }[]
  washes: { value: string; hint?: string }[]
}

/** One field of the draft, labelled and typed from the tool's schema. */
function DraftField({ name, property, value, error, onChange, garages, washes }: DraftFieldProps) {
  return (
    <Field
      label={labelFor(name)}
      {...(property.description !== undefined && { hint: property.description })}
      error={error}
      wide
    >
      {(field) => renderInput(name, property, value, onChange, field, { garages, washes })}
    </Field>
  )
}

/** The proposed values as form strings. Absent stays absent — an empty field is a field to fill in. */
function initial(draft: ChatDraft): Record<string, string> {
  const out: Record<string, string> = {}

  for (const [name, value] of Object.entries(draft.values)) {
    out[name] = value === null || value === undefined ? '' : String(value)
  }

  return out
}

/**
 * Form strings back to what the tool wants.
 *
 * A blank is dropped rather than sent as `""` — an empty optional field means "not stated", and a tool that
 * receives an empty string for a date has been told something false. A required blank is kept so the server
 * refuses it against its own schema and names the field, rather than this deciding on its behalf.
 */
function coerce(
  values: Record<string, string>,
  properties: Record<string, JsonSchemaProperty>,
  required: Set<string>,
): Record<string, unknown> {
  const out: Record<string, unknown> = {}

  for (const [name, raw] of Object.entries(values)) {
    const text = raw.trim()

    if (text === '' && !required.has(name)) continue

    const type = typeOf(properties[name])

    if (type === 'number' || type === 'integer') {
      const parsed = Number(text.replace(/[\s,£]/g, ''))
      out[name] = Number.isFinite(parsed) ? parsed : text
    } else if (type === 'boolean') {
      out[name] = text === 'true'
    } else {
      out[name] = text
    }
  }

  return out
}

function typeOf(property: JsonSchemaProperty | undefined): string {
  if (property === undefined) return 'string'
  const type = property.type
  if (Array.isArray(type)) return type.find((t) => t !== 'null') ?? 'string'
  return type ?? 'string'
}

/** `serviceDate` → "Service date". The tools name their parameters well; this only has to re-space them. */
export function labelFor(name: string): string {
  const spaced = name.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/_/g, ' ')
  return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase()
}

interface FieldAria {
  id: string
  'aria-describedby'?: string
  'aria-invalid'?: true
}

function renderInput(
  name: string,
  property: JsonSchemaProperty,
  value: string,
  onChange: (value: string) => void,
  field: FieldAria,
  suggestions: { garages: { value: string; hint?: string }[]; washes: { value: string; hint?: string }[] },
) {
  if (property.enum !== undefined) {
    return (
      <Select {...field} value={value} onChange={(e) => onChange(e.target.value)}>
        <option value="">—</option>
        {property.enum.map((option) => (
          <option key={option} value={option}>
            {option}
          </option>
        ))}
      </Select>
    )
  }

  // A place name is a reference row, not free text — the same combobox the add sheets use, reading the same
  // cached list, so a garage typed here and a garage typed there are one row.
  const list = /garage/i.test(name) ? suggestions.garages : /location/i.test(name) ? suggestions.washes : null

  if (list !== null) {
    return <Combobox {...field} value={value} onChange={onChange} suggestions={list} />
  }

  const type = typeOf(property)

  return (
    <input
      {...field}
      type={type === 'number' || type === 'integer' ? 'text' : /date/i.test(name) ? 'date' : 'text'}
      // Not type="number": a numeric keypad is right on a phone, but a spinner that silently swallows a pasted
      // "1,234.56" is not, and the coercion above already strips separators.
      {...((type === 'number' || type === 'integer') && { inputMode: 'decimal' })}
      value={value}
      onChange={(e) => onChange(e.target.value)}
    />
  )
}
