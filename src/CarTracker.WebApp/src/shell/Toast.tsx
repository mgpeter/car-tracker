import { createContext, use, useCallback, useEffect, useRef, useState, type ReactNode } from 'react'

/**
 * How long a toast stays. ONE value.
 *
 * The design drifts across 3800, 4000, 4200 and 4600ms with no discernible reason — it is 17 copy-pastes of
 * the same helper, each nudged. 4000 is the middle and the most common.
 */
const TOAST_MS = 4000

interface ToastContextValue {
  /** Show a message. A second call replaces the first — there is no queue, as in the design. */
  toast: (message: string) => void
}

const ToastContext = createContext<ToastContextValue | null>(null)

/**
 * One toast, replacing the identical `toastMsg` helper pasted into all 17 screens.
 *
 * `role="status"` — a polite live region, so it is announced without stealing focus. The design gets this
 * right; what it does not have is a single owner, so 17 copies each keep their own timer.
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState('')
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const toast = useCallback((next: string) => {
    setMessage(next)
    if (timer.current !== null) clearTimeout(timer.current)
    timer.current = setTimeout(() => setMessage(''), TOAST_MS)
  }, [])

  useEffect(() => () => {
    if (timer.current !== null) clearTimeout(timer.current)
  }, [])

  return (
    <ToastContext value={{ toast }}>
      {children}
      {/* Always mounted, so the live region exists before it has anything to say — a region that appears at
          the same moment as its content is announced unreliably. */}
      <div className="toast-region" role="status" aria-live="polite">
        {message !== '' && <div className="toast num">{message}</div>}
      </div>
    </ToastContext>
  )
}

export function useToast(): ToastContextValue {
  const ctx = use(ToastContext)
  if (ctx === null) throw new Error('useToast must be used within a ToastProvider')
  return ctx
}
