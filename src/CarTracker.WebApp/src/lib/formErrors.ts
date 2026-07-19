import { ApiFailure } from '../api/queries'

/**
 * Per-field form errors, keyed by lowercased field name.
 *
 * The reserved key `_` holds form-level messages — anything that isn't tied to a single input, shown in the
 * sheet's footer banner rather than beside a field. Every field key is lowercased so the lookup is stable
 * regardless of how the server cased it (see `reportApiError`).
 */
export type FieldErrors = Record<string, string[]>

/** The banner key: errors with no field to attach to. */
export const FORM_KEY = '_'

/**
 * Turn a failed mutation into `FieldErrors`.
 *
 * The server already returns an RFC 9457 field→messages map on a 400 (validation problem). The trouble is only
 * that its keys are cased inconsistently — some `nameof(...)` PascalCase (`Litres`), some hardcoded lowercase
 * (`type`), some dotted or collection-level (`Insurance.PeriodEnd`, `Targets`). So: lowercase every key, keep
 * the ones the sheet actually renders (`fieldKeys`, also lowercased) against their field, and fold everything
 * else — dotted, collection-level, framework-generated, or a plain non-validation failure — into `_`, so a
 * real reason is never silently dropped.
 *
 * `fieldKeys` is the set of field names the caller renders `error=` for; pass them lowercase or not, it
 * normalises either way.
 */
export function reportApiError(err: unknown, fieldKeys: readonly string[] = []): FieldErrors {
  const known = new Set(fieldKeys.map((k) => k.toLowerCase()))
  const out: FieldErrors = {}

  const push = (key: string, messages: string[]) => {
    const existing = out[key]
    if (existing) existing.push(...messages)
    else out[key] = [...messages]
  }

  const serverErrors = err instanceof ApiFailure && err.error.kind === 'http' ? err.error.errors : undefined

  if (serverErrors !== undefined && Object.keys(serverErrors).length > 0) {
    for (const [rawKey, messages] of Object.entries(serverErrors)) {
      const key = rawKey.toLowerCase()
      push(known.has(key) ? key : FORM_KEY, messages)
    }
    return out
  }

  // No field map — a conflict, a not-found, a network drop, or a bare message. Show the server's own reason.
  const message = err instanceof Error ? err.message : 'Could not save.'
  return { [FORM_KEY]: [message] }
}

/** The first message for a field, or undefined — the shape `<Field error=…>` wants. */
export function fieldError(errors: FieldErrors, key: string): string | undefined {
  return errors[key.toLowerCase()]?.[0]
}

/** The form-level message for the footer banner, or undefined. */
export function formError(errors: FieldErrors): string | undefined {
  return errors[FORM_KEY]?.[0]
}
