import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useDocumentTypes } from '../../api/documents'
import { cleanDate, cleanLink, cleanTitle, LINK_KEYS } from './filingQuery'
import type { UploadInitial } from './UploadSheet'

/** The flag a filing link sets on the screen it lands on. */
export const FILE_PARAM = 'file'

const PREFILL_KEYS = [FILE_PARAM, 'type', 'title', 'date', ...Object.values(LINK_KEYS)]

/**
 * The receiving half of a filing link: `?file=1&type=...&title=...&date=...&issueId=...` on Photos or
 * Documents, as `/file` writes it.
 *
 * Returns the upload sheet's `initial` while `file=1` is present, and a `clear` that removes the prefill from
 * the URL with a replace, so closing the sheet and pressing Back does not reopen it. The link chip shows the
 * record kind only, because the link carries an id and no label (assumed: A12, A17). A category the server's
 * table does not know is left for the sheet's own default.
 */
export function useFilePrefill(): { initial: UploadInitial | null; clear: () => void } {
  const [params, setParams] = useSearchParams()
  const { data: types, isError } = useDocumentTypes()
  const active = params.get(FILE_PARAM) === '1'

  const initial = useMemo((): UploadInitial | null => {
    // Waits for the category table, because the sheet reads `initial` once, when it opens.
    if (!active || (types === undefined && !isError)) return null
    const initial: UploadInitial = {}
    const type = types?.find((t) => t.type === params.get('type'))?.type
    if (type !== undefined) initial.type = type
    const title = cleanTitle(params.get('title'))
    if (title !== undefined) initial.title = title
    const date = cleanDate(params.get('date'))
    if (date !== undefined) initial.documentDate = date
    const link = cleanLink(params)
    if (link !== undefined) initial.link = link
    return initial
  }, [active, params, types, isError])

  const clear = useCallback(() => {
    setParams(
      (previous) => {
        const next = new URLSearchParams(previous)
        for (const key of PREFILL_KEYS) next.delete(key)
        return next
      },
      { replace: true },
    )
  }, [setParams])

  return { initial, clear }
}
