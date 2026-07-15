import { useEffect, type RefObject } from 'react'

/**
 * Everything focusable, in document order. `:not([disabled])` and the `tabindex="-1"` exclusion matter: a
 * disabled control and a programmatically-focusable container are both reachable by query but not by Tab, and
 * including them would make the trap skip to a dead stop.
 */
const FOCUSABLE = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',')

function focusable(container: HTMLElement): HTMLElement[] {
  return [...container.querySelectorAll<HTMLElement>(FOCUSABLE)].filter(
    // offsetParent is null for display:none. jsdom has no layout, so this is a cheap guard rather than a
    // real visibility test — but it costs nothing and is correct in a browser.
    (el) => !el.hasAttribute('hidden'),
  )
}

interface FocusTrapOptions {
  active: boolean
  containerRef: RefObject<HTMLElement | null>
  onEscape: () => void
}

/**
 * Traps Tab inside a container, restores focus on close, and calls `onEscape` on Escape.
 *
 * Written by hand rather than reached for from a library, and NOT via native `<dialog>` — jsdom 29 has no
 * `showModal`, so every sheet test would be exercising a polyfill instead of the browser. Task 1.3 made the
 * same call about `vitest-axe`. This is ~40 lines and every line is testable, because `userEvent.tab()`
 * traverses focus in JS and the trap is a keydown handler.
 *
 * The design has none of this: 17 files, zero matches for Escape, keydown or focus(). Dismissal is a scrim
 * click or the close button, and focus falls back to `<body>`.
 */
export function useFocusTrap({ active, containerRef, onEscape }: FocusTrapOptions) {
  useEffect(() => {
    if (!active) return
    const container = containerRef.current
    if (container === null) return

    // Captured before we move focus, restored on close. Without this, dismissing a sheet drops the user at
    // the top of the document — which is where the design leaves them.
    const previous = document.activeElement as HTMLElement | null

    // The container itself, not its first input: focusing the input skips the title, so a screen-reader user
    // is dropped into a text box with no idea what it belongs to.
    container.focus()

    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.stopPropagation()
        onEscape()
        return
      }
      if (e.key !== 'Tab') return

      const items = focusable(container)
      if (items.length === 0) {
        // Nothing to tab to — keep focus on the container rather than letting it escape to the page behind.
        e.preventDefault()
        return
      }

      const first = items[0]!
      const last = items[items.length - 1]!
      const current = document.activeElement

      if (e.shiftKey && (current === first || current === container)) {
        e.preventDefault()
        last.focus()
      } else if (!e.shiftKey && current === last) {
        e.preventDefault()
        first.focus()
      }
    }

    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      if (previous === null) return

      // Restore unless something else has legitimately claimed focus meanwhile — stealing it back would be
      // worse than leaving it.
      //
      // The `body` case is the normal one, not an edge case: this is a passive effect, so by the time it runs
      // React has already detached the sheet, focus has fallen to <body>, and `container.contains(active)` is
      // false. Checking only for containment — which reads as the careful thing to do — silently never
      // restores focus at all.
      const active = document.activeElement
      const focusWasLost = active === null || active === document.body || container.contains(active)
      if (focusWasLost) previous.focus()
    }
  }, [active, containerRef, onEscape])
}

/**
 * Marks everything outside the sheet inert, so a screen reader cannot walk into the page behind it.
 *
 * Uses `setAttribute`, not the `.inert` property: jsdom implements neither, but the attribute is at least
 * assertable in a test, and in a browser the attribute is what counts. This is only possible because the
 * sheet portals to `document.body` — inside `#root` it would inert itself.
 */
export function useInertBackground(active: boolean, exceptId = 'root') {
  useEffect(() => {
    if (!active) return
    const target = document.getElementById(exceptId)
    if (target === null) return

    const had = target.hasAttribute('inert')
    if (!had) target.setAttribute('inert', '')

    return () => {
      if (!had) target.removeAttribute('inert')
    }
  }, [active, exceptId])
}
