import { useCallback, useId, useRef, type FormEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useFocusTrap, useInertBackground } from '../lib/useFocusTrap'
import { useScrollLock } from '../lib/useScrollLock'
import { Mark } from './Btn'

interface SheetProps {
  open: boolean
  onClose: () => void
  /** Names the dialog. Wired by id via useId, not duplicated into an aria-label as the design does. */
  title: string
  /** The context line under the title — "last 30 Jun · 14 days ago". Announced via aria-describedby. */
  subtitle?: string
  /**
   * When present the sheet renders a real `<form>` and the footer button becomes `type="submit"`.
   *
   * The design has **no `<form>` anywhere** and zero onSubmit handlers — every sheet is a div of inputs with
   * a `type="button"` save. It sets `enterKeyHint="done"`, which paints the right key on a phone keyboard and
   * wires it to nothing: Enter does nothing in any of its 25 sheets. This makes that hint true.
   */
  onSubmit?: () => void
  footer?: ReactNode
  children: ReactNode
}

/**
 * The bottom sheet / centred dialog.
 *
 * A div with an owned focus trap, NOT native `<dialog>`. Three reasons, in order of weight:
 *
 *  1. **jsdom 29 cannot run `<dialog>`** — `showModal`/`close` are `undefined`. Every sheet test would be
 *     exercising a polyfill rather than the browser. Task 1.3 rejected exactly that trade for `vitest-axe`.
 *  2. **The top layer would break the design's stack.** theme.css specifies `.ovl:80 / .sheet:90 /
 *     .toast:100` — the toast is *meant* to sit above a sheet ("wash logged…" while the sheet closes). A
 *     `<dialog>` in the top layer beats every z-index, so a non-dialog toast could never paint over it.
 *  3. **The scrim gets worse, not better.** `.ovl` is a real sibling today, so a scrim click is an ordinary
 *     handler; with `::backdrop` you need the `e.target === dialogRef.current` trick.
 *
 * Everything the design lacks is here: aria-modal, aria-labelledby, Escape, focus trap, focus restore, scroll
 * lock, and inert on the page behind. The design has none — I grepped all 17 files for Escape/keydown/focus()
 * and found zero.
 */
export function Sheet({ open, onClose, title, subtitle, onSubmit, footer, children }: SheetProps) {
  const ref = useRef<HTMLDivElement>(null)
  const titleId = useId()
  const subtitleId = useId()

  useFocusTrap({ active: open, containerRef: ref, onEscape: onClose })
  useScrollLock(open)
  useInertBackground(open)

  const handleSubmit = useCallback(
    (e: FormEvent) => {
      e.preventDefault()
      onSubmit?.()
    },
    [onSubmit],
  )

  if (!open) return null

  const body = (
    <>
      <div className="f-grid">{children}</div>
      {footer !== undefined && <div className="sh-foot">{footer}</div>}
    </>
  )

  const sheet = (
    <>
      {/* A sibling, not a parent — the design's structure, and the reason a scrim click stays simple.
          aria-hidden because the sheet is already aria-modal; announcing an empty scrim is noise. */}
      <div className="ovl" onClick={onClose} aria-hidden="true" />
      <div
        className="sheet"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        {...(subtitle !== undefined && { 'aria-describedby': subtitleId })}
        // Focus lands here rather than on the first input, so the title is announced first.
        tabIndex={-1}
        ref={ref}
      >
        <div className="sh-grip" aria-hidden="true" />
        <div className="sh-head">
          <h3 id={titleId}>{title}</h3>
          {subtitle !== undefined && (
            <span className="sh-sub" id={subtitleId}>
              {subtitle}
            </span>
          )}
          <Mark onClick={onClose}>Close</Mark>
        </div>
        {onSubmit === undefined ? body : <form onSubmit={handleSubmit}>{body}</form>}
      </div>
    </>
  )

  // Portalled to body so `inert` can be set on #root. Inside #root the sheet would inert itself.
  return createPortal(sheet, document.body)
}

/**
 * A `<select>` with the design's chevron.
 *
 * The wrapper exists because a `<select>` is a replaced element and `::after` on it is unreliable — the
 * chevron hangs off the wrapper instead. That also lets the chevron be a *mask* tinted by `var(--accent)`
 * rather than an SVG with the accent's light value baked in, which is what the design ships and why its
 * chevron stays light in dark mode.
 */
export function Select({
  children,
  ...rest
}: React.SelectHTMLAttributes<HTMLSelectElement> & { children: ReactNode }) {
  return (
    <span className="sel-wrap">
      <select {...rest}>{children}</select>
    </span>
  )
}

interface FieldProps {
  label: string
  /**
   * The clarifier under the input — "£0 skips the expense mirror". Wired via aria-describedby.
   *
   * Explicitly `| undefined`, against `exactOptionalPropertyTypes`. That setting earns its keep where absent
   * and present-but-empty are different states (a null MPG is not a missing MPG); a hint has no such
   * distinction, and the common call is `hint={errors.x?.[0]}` — a lookup that yields undefined when there is
   * nothing to say. Forcing every caller to conditionally spread the prop would be ceremony protecting a
   * difference that does not exist.
   */
  hint?: string | undefined
  /** Spans both grid columns. */
  wide?: boolean
  /** Receives the generated id, so label association cannot be forgotten. */
  children: (props: { id: string; 'aria-describedby'?: string }) => ReactNode
}

/**
 * A labelled form field.
 *
 * The id is generated and handed to the input via a render prop, so `for`/`id` cannot drift apart. The design
 * gets this wrong in three of its sheets — `wash`, `garage` and `settings` use a bare `<label>` with the input
 * as a *sibling*, so the label is associated with nothing: clicking it does not focus the field, and a screen
 * reader announces an unlabelled input. `useId` fixes that for everyone, permanently.
 */
export function Field({ label, hint, wide = false, children }: FieldProps) {
  const id = useId()
  const hintId = useId()

  return (
    <div className={wide ? 'field wide' : 'field'}>
      <label htmlFor={id}>{label}</label>
      {children({ id, ...(hint !== undefined && { 'aria-describedby': hintId }) })}
      {hint !== undefined && (
        <span className="hint" id={hintId}>
          {hint}
        </span>
      )}
    </div>
  )
}
