interface OdometerProps {
  /** Whole miles. The domain has no tenths — see below, it matters. */
  miles: number
  /** How many drums. Six is the Freelander's, and what the design draws. */
  digits?: number
}

/**
 * The odometer drum stack.
 *
 * **The design's last drum is styled `.tenths` and fed the units digit.** With `odo: '080712'` it renders a
 * red "2" that a reader fluent in odometers reads as *two tenths* — 8,071.2 miles against a true 80,712. A
 * factor of ten, in the largest type on the page, on the one screen whose entire premise is that its numbers
 * are trustworthy.
 *
 * `MileageReading.Mileage` is an integer and there is no tenth anywhere in the schema, so the honest fix is
 * that no drum can claim to be one. The skin stays — the red drum is the design's signature and reads as
 * hardware — but the class is `.drum-last`, because a name is what the next person edits against, and
 * `.tenths` invites someone to helpfully "restore" a precision we have never had.
 *
 * The accessible name states the reading outright rather than spelling digits, which is also what fixes it for
 * a screen reader: the design's `aria-label="Odometer reading in miles"` names the control and withholds the
 * value, so the one number the drum exists to convey never reaches anyone not looking at it.
 */
export function Odometer({ miles, digits = 6 }: OdometerProps) {
  const shown = Math.max(0, Math.trunc(miles))
  const text = String(shown).padStart(digits, '0')
  // A reading wider than the drum stack: show the last N digits, as real hardware does when it rolls over,
  // but never silently — the label carries the true figure regardless.
  const drums = text.slice(-digits).split('')

  return (
    <div className="drum num" role="img" aria-label={`Odometer: ${shown.toLocaleString('en-GB')} miles`}>
      {drums.map((d, i) => (
        <i key={i} className={i === drums.length - 1 ? 'drum-last' : undefined} aria-hidden="true">
          {d}
        </i>
      ))}
    </div>
  )
}
