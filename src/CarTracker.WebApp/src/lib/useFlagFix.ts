import { useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useAnomalies, type AnomalyEntityType, type AnomalyItem } from '../api/anomalies'

/** The one search param this app uses. Named here so the reader and the writer cannot drift. */
export const FLAG_PARAM = 'flag'

/**
 * The receiving half of the integrity queue's **Fix this** action.
 *
 * The queue links to the screen that owns the offending row, carrying `?flag=<anomalyId>` — **the flag's id,
 * not the row's**. Three reasons, and they are the whole design:
 *
 * 1. A row id alone is ambiguous across screens. `#14` is an equipment item here and a fuel entry there, and a
 *    link that names only a number cannot be checked against anything.
 * 2. The flag carries its own `entityType`, so this hook can compare it to the caller's and return nothing on a
 *    mismatch. A stale bookmark or a hand-edited URL then opens *no* row rather than an unrelated one — it
 *    fails closed.
 * 3. The banner gets its text from the flag itself, so the target screen never re-derives the reason it was
 *    sent for. One sentence, written once, by the detector.
 *
 * **The param is stripped on arrival** (`replace: true`) and the resolved flag kept in local state for the
 * visit. Otherwise closing the sheet and pressing Back would reopen it, a refresh would reopen it, and the URL
 * would keep asserting a fix that has already happened. The highlight outlives the param because it is state,
 * not a query string.
 */
export function useFlagFix(reg: string, entityType: AnomalyEntityType): {
  /** The flag this visit is fixing, or null. Already checked to belong to `entityType`. */
  flag: AnomalyItem | null
  /** Dismiss the banner and drop the row highlight. */
  clear: () => void
} {
  const [params, setParams] = useSearchParams()
  const raw = params.get(FLAG_PARAM)
  const wanted = raw === null ? null : Number.parseInt(raw, 10)

  // Only fetch when a link actually sent us here — every log would otherwise pay for a queue it is not going
  // to read. Arriving from the queue finds the cache already warm; arriving cold (a bookmark) pays one GET.
  const { data } = useAnomalies(reg, 'open', wanted !== null)
  const [flag, setFlag] = useState<AnomalyItem | null>(null)

  useEffect(() => {
    if (wanted === null || Number.isNaN(wanted)) return

    // Wait for the list before deciding: clearing the param on a pending query would lose the request.
    if (data === undefined) return

    const found = data.find((a) => a.id === wanted) ?? null
    setFlag(found !== null && found.entityType === entityType ? found : null)
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        next.delete(FLAG_PARAM)
        return next
      },
      { replace: true },
    )
  }, [wanted, data, entityType, setParams])

  return { flag, clear: () => setFlag(null) }
}

/**
 * A ref callback that brings the flagged row into view — **once**.
 *
 * The guard is not defensive tidiness. A ref callback declared inline is a new function every render, so React
 * detaches and re-attaches it each time; without the ref this would yank the page back to the row while the
 * reader was scrolling away from it.
 */
export function useFixRowRef(entityId: number | null | undefined) {
  const scrolled = useRef<number | null>(null)

  return (id: number) => (el: HTMLElement | null) => {
    if (el === null || entityId === null || entityId === undefined) return
    if (id !== entityId || scrolled.current === entityId) return
    scrolled.current = entityId
    el.scrollIntoView({ block: 'center' })
  }
}

/**
 * Opens the row a flag points at, once.
 *
 * Guarded by a ref rather than by the effect's dependencies, because "the sheet has been offered for this
 * flag" is not derivable from props: the user closing the sheet sets `editing` back to null, and a
 * dependency-driven effect would helpfully reopen it forever.
 *
 * `editable` returning false is the mirrored-mileage case — a reading written by a service record is corrected
 * at its source, so the row is highlighted and *no* sheet opens. The screen says where the fix lives instead.
 */
export function useOpenFixedRow<T>(
  entityId: number | null | undefined,
  rows: T[] | undefined,
  idOf: (row: T) => number,
  open: (row: T) => void,
  editable?: (row: T) => boolean,
): void {
  const offered = useRef<number | null>(null)

  useEffect(() => {
    if (entityId === null || entityId === undefined || rows === undefined) return
    if (offered.current === entityId) return

    const row = rows.find((r) => idOf(r) === entityId)
    if (row === undefined) return

    offered.current = entityId
    if (editable?.(row) ?? true) open(row)
    // `idOf`, `open` and `editable` are re-created every render by every caller, so listing them would make
    // this an every-render effect. The ref is what makes it run once, and that is the actual guard.
  }, [entityId, rows])
}
