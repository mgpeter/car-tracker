import { useState } from 'react'

/**
 * A destructive action that asks once, inline, before it fires.
 *
 * **No modal.** Deleting a log entry cascades — a fill takes its mileage reading and its mirrored expense with
 * it — so a single mis-tap should not do it. But a dialog stacked over an open sheet is its own trap (focus,
 * scroll-lock and inert all fight), so the confirm happens in place: the first press swaps the label to a
 * warning that names the cascade, the second within a few seconds commits, and a blur resets it. The precedent
 * is the settings check-editor's footer Delete; this makes the two-state behaviour one component so every log's
 * delete reads and sounds the same.
 *
 * The accessible name changes with the state, so a screen reader hears "Delete" become "Confirm delete — also
 * removes the mirrored expense" rather than a button that silently rearmed.
 */
export function ConfirmButton({
  onConfirm,
  label = 'Delete',
  confirmLabel = 'Confirm delete',
  /** Appended to the confirm label to name what else goes, e.g. "also removes the mirrored expense". */
  cascade,
  pending = false,
}: {
  onConfirm: () => void
  label?: string
  confirmLabel?: string
  cascade?: string | undefined
  pending?: boolean
}) {
  const [armed, setArmed] = useState(false)

  const confirmText = cascade ? `${confirmLabel} — ${cascade}` : confirmLabel

  return (
    <button
      className={`btn ghost${armed ? ' armed' : ''}`}
      type="button"
      // A blur disarms it: tabbing away or clicking elsewhere is a change of mind, and the button must not
      // stay hot waiting for an accidental second press later.
      onBlur={() => setArmed(false)}
      onClick={() => {
        if (armed) {
          onConfirm()
          setArmed(false)
        } else {
          setArmed(true)
        }
      }}
    >
      {pending ? 'Deleting…' : armed ? confirmText : label}
    </button>
  )
}
