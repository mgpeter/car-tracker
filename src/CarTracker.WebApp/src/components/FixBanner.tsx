import type { ReactNode } from 'react'
import type { AnomalyItem } from '../api/anomalies'
import { ANOMALY_KIND } from '../lib/anomalyCopy'
import { AppLink } from '../lib/link'
import type { ScreenId } from '../shell/nav'
import { Panel } from './layout'

/**
 * Why you are on this screen, when you arrived from the integrity queue's **Fix this**.
 *
 * **`.attn.attn-info`, not a fourth callout.** The first cut invented `.fixban*` for this and `.fixnote` for
 * the mirrored case, which put two blue boxes of different widths on one screen giving opposite instructions —
 * and `MileagePage` already renders an `.attn.attn-info` thirty lines above. `.attn` is the house shape for
 * exactly this: `1fr auto`, prose left and actions right, with the measure and the spacing already settled.
 *
 * It carries the detector's own sentence rather than a re-worded one. The flag already names both figures —
 * "Reading of 83,000 mi on 27 Jun 2026 is above the current 80,712 mi" — and re-deriving that here would be a
 * second implementation of a claim the domain already made, which is the failure this project exists to
 * prevent. The banner is a courier, not an author.
 *
 * There is no "done" button. Fixing the data retracts the flag by itself on the next write
 * (`AnomalyScanner.Reconcile`), so a button here would only ever be able to lie about it.
 */
export function FixBanner({
  flag,
  reg,
  onDismiss,
  note,
  action,
}: {
  flag: AnomalyItem
  reg: string
  onDismiss: () => void
  /**
   * Replaces the default "correct the row below" line. The mirrored-reading case passes its own sentence,
   * because "the row below" is precisely what it cannot offer — the row is read-only on this screen.
   */
  note?: ReactNode
  /** Where the fix actually lives, when it is not this screen. */
  action?: { screen: ScreenId; label: string }
}) {
  return (
    <Panel className="attn attn-info">
      <div>
        <div className="attn-k">Fixing a flagged row</div>
        <h3>{ANOMALY_KIND[flag.kind].title}</h3>
        {/* The detector's prose, in the mono the queue renders it in — it is a pair of figures, not a
            sentence about them. */}
        <p className="fix-msg num">{flag.message}</p>
        <p>
          {note ?? (
            <>
              Correct the row below and the flag clears itself — the detectors re-run on every write, so there
              is nothing here to mark done.
            </>
          )}
        </p>
      </div>

      <div className="attn-act">
        {action !== undefined && (
          <AppLink className="btn" to={action.screen} reg={reg}>
            {action.label} →
          </AppLink>
        )}
        <AppLink className="mark" to="data-integrity" reg={reg}>
          ← Data integrity
        </AppLink>
        <button className="mark" type="button" onClick={onDismiss}>
          Dismiss
        </button>
      </div>
    </Panel>
  )
}
