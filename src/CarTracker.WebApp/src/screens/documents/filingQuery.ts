import type { DocumentLinkKind } from '../../api/documents'

/**
 * The keys a filing link and an upload prefill share, and the checks both apply to them.
 *
 * A link from `offer_to_file` (`/file?vehicle=...`) and the prefill it turns into (`/:reg/photos?file=1&...`)
 * are both URLs anyone can type, so every value is checked here and dropped when it does not fit. The link
 * authorises nothing either way: the owner still picks the file and presses Upload, and the endpoint checks the
 * type, the link id and the allowance again (assumed: A1).
 */

/** The link keys, in the order the first valid one wins. */
export const LINK_KEYS: Record<DocumentLinkKind, string> = {
  ServiceRecord: 'serviceRecordId',
  Expense: 'expenseEntryId',
  Issue: 'issueId',
}

const MAX_TITLE = 160

export const cleanTitle = (value: string | null): string | undefined => {
  const trimmed = value?.trim().slice(0, MAX_TITLE)
  return trimmed === undefined || trimmed === '' ? undefined : trimmed
}

export const cleanDate = (value: string | null): string | undefined =>
  value !== null && /^\d{4}-\d{2}-\d{2}$/.test(value) && !Number.isNaN(Date.parse(value)) ? value : undefined

/** The first link key carrying a plain positive id, or none. At most one link survives. */
export function cleanLink(params: URLSearchParams): { kind: DocumentLinkKind; id: number } | undefined {
  for (const [kind, key] of Object.entries(LINK_KEYS) as [DocumentLinkKind, string][]) {
    const raw = params.get(key)
    if (raw !== null && /^[1-9]\d{0,9}$/.test(raw)) return { kind, id: Number(raw) }
  }
  return undefined
}
