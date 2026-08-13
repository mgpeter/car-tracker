import type { AnomalyItem } from '../api/anomalies'
import { AppLink } from '../lib/link'

/**
 * Why you are on this screen, when you arrived from the integrity queue's **Fix this**.
 *
 * It carries the detector's own sentence rather than a re-worded one. The flag already names both figures —
 * "Reading of 83,000 mi on 27 Jun 2026 is above the current 80,712 mi" — and re-deriving that here would be a
 * second implementation of a claim the domain already made, which is the failure this project exists to
 * prevent. The banner is a courier, not an author.
 *
 * **Blue, never a due tone.** Integrity is its own axis (`lib/status.ts`); "this datum is unreliable" is a
 * different question from "this is overdue".
 *
 * There is no "done" button. Fixing the data retracts the flag by itself on the next write
 * (`AnomalyScanner.Reconcile`), so a button here would only ever be able to lie about it.
 */
export function FixBanner({ flag, reg, onDismiss }: { flag: AnomalyItem; reg: string; onDismiss: () => void }) {
  return (
    // No live region. It is present from the first paint of the screen you navigated to, and a `role="status"`
    // announces *changes* — here it would only add a second announcement of text already in reading order,
    // ahead of the table it introduces.
    <div className="fixban">
      <div className="fixban-h">
        <span className="fixban-k">Fixing a flagged row</span>
        <AppLink to="data-integrity" reg={reg} className="mark">
          ← Data integrity
        </AppLink>
        <button className="fixban-x" type="button" onClick={onDismiss} aria-label="Dismiss this notice">
          ×
        </button>
      </div>
      <p className="fixban-m">{flag.message}</p>
      <p className="fixban-n">
        Correct the row below and the flag clears itself — the detectors re-run on every write, so there is
        nothing here to mark done.
      </p>
    </div>
  )
}
