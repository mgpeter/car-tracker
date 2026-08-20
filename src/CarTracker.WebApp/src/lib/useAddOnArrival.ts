import { useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'

/** The quick-add search param. See `FLAG_PARAM` in `useFlagFix.ts` for the other one. */
export const ADD_PARAM = 'add'

/**
 * The receiving half of the dashboard's quick-add band.
 *
 * Quick add links to the screen that owns the thing you are adding, carrying `?add=1`, and that screen opens
 * its own add sheet on arrival. The alternative was mounting six sheets on the dashboard, which `QuickAdd`'s
 * own doc argues against: the sheets live where their data does, and several of them need rows the dashboard
 * summary does not carry.
 *
 * **The param is stripped on arrival** (`replace: true`), and the answer kept in local state for the visit -
 * the same rule `useFlagFix` follows and for the same three reasons. Without it, closing the sheet and
 * pressing Back reopens it, a refresh reopens it, and the URL goes on asserting an intent that was acted on
 * the moment you got here.
 *
 * **It fires once, guarded by a ref rather than by dependencies**, for the reason `useOpenFixedRow` records:
 * "the sheet has been offered" is not derivable from props, because closing the sheet sets the caller's state
 * back to null and a dependency-driven effect would helpfully reopen it forever.
 *
 * @param open Called once, on a visit that arrived from a quick-add link. Stable identity is not required.
 */
export function useAddOnArrival(open: () => void): void {
  const [params, setParams] = useSearchParams()
  const asked = params.get(ADD_PARAM) !== null
  const offered = useRef(false)

  // Read through a ref so a caller passing an inline arrow does not re-run this every render. The same trap
  // `useFocusTrap` fell into with `onEscape`, where it cost a keystroke per render.
  const handler = useRef(open)
  handler.current = open

  useEffect(() => {
    if (!asked || offered.current) return

    offered.current = true

    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        next.delete(ADD_PARAM)
        return next
      },
      { replace: true },
    )

    handler.current()
  }, [asked, setParams])
}

/**
 * The same signal, as a value rather than a callback.
 *
 * For the one screen whose sheet cannot be opened with a constant: the checks sheet takes the list of checks
 * to log, so the *selection* is the open signal and there is no `'new'` to pass. That screen has to wait for
 * its own query before it can decide what to open, so it needs to know an add was asked for and act later.
 *
 * Returns true until the caller acknowledges it with `taken()`. The param is still stripped immediately.
 */
export function useAddRequest(): { asked: boolean; taken: () => void } {
  const [params, setParams] = useSearchParams()
  const [asked, setAsked] = useState(params.get(ADD_PARAM) !== null)
  const stripped = useRef(false)

  useEffect(() => {
    if (params.get(ADD_PARAM) === null || stripped.current) return

    stripped.current = true
    setParams(
      (prev) => {
        const next = new URLSearchParams(prev)
        next.delete(ADD_PARAM)
        return next
      },
      { replace: true },
    )
  }, [params, setParams])

  return { asked, taken: () => setAsked(false) }
}
