import { useEffect } from 'react'

/**
 * Module-level, deliberately.
 *
 * Two sheets can overlap in principle (the dashboard has three, and a nav sheet can open over a form). If each
 * lock restored `overflow` on its own teardown, the first to close would unlock the page while the second was
 * still open. Counting means the last one out restores it — and restores it to whatever was there before the
 * first lock, not to a hardcoded `''`.
 */
let depth = 0
let restore: string | null = null

/** Stops the page behind a sheet from scrolling. The design has no scroll lock at all. */
export function useScrollLock(active: boolean) {
  useEffect(() => {
    if (!active) return

    if (depth === 0) {
      restore = document.body.style.overflow
      document.body.style.overflow = 'hidden'
    }
    depth += 1

    return () => {
      depth -= 1
      if (depth === 0) {
        document.body.style.overflow = restore ?? ''
        restore = null
      }
    }
  }, [active])
}

/** Test-only: the module-level counter would otherwise leak between tests in the same file. */
export function __resetScrollLock() {
  depth = 0
  restore = null
  document.body.style.overflow = ''
}
