import { addMonths, todayIso } from '../lib/date'

interface DateQuickFillProps {
  /** The date the offsets are measured from — the record's own date, or today when it has none yet. */
  base?: string | undefined
  onPick: (iso: string) => void
}

/**
 * "+6 months" / "+1 year" shortcuts for a forward-looking date (a next-service or a follow-up), so the common
 * case is one tap instead of scrolling a date picker. Measured from `base` if set, else today.
 */
export function DateQuickFill({ base, onPick }: DateQuickFillProps) {
  const from = base !== undefined && base !== '' ? base : todayIso()
  return (
    <div className="quickfill">
      <button type="button" className="qf-link" onClick={() => onPick(addMonths(from, 6))}>
        +6 months
      </button>
      <button type="button" className="qf-link" onClick={() => onPick(addMonths(from, 12))}>
        +1 year
      </button>
    </div>
  )
}
