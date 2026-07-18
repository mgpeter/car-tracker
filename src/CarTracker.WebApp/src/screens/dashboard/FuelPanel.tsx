import type { VehicleSummary } from '../../api/client'
import { Kv } from '../../components/Kv'
import { Spark, type SparkPoint } from '../../components/Spark'
import { Panel } from '../../components/layout'
import { AppLink } from '../../lib/link'

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', minimumFractionDigits: 2 })

const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

/**
 * Fuel economy.
 *
 * The headline is `AverageMpg` — cumulative, total distance over litres actually pumped. Not the mean of the
 * per-fill figures, which is a different question that the summary also answers; the two agree to 0.05 on the
 * real history and a divergence would be a signal.
 *
 * **The empty and near-empty states are the ones the design cannot show.** It has thirteen fills frozen into
 * it, so it never renders a car with one fill, or none. One fill produces no MPG at all — there is nothing to
 * measure from — and that is not a bug to paper over with a zero.
 */
export function FuelPanel({ summary }: { summary: VehicleSummary }) {
  const { fuel } = summary
  const reg = summary.registration

  // Only plausible measured intervals. Implausible figures are computed correctly from exact litres and are
  // still not real — a five-litre splash after 300 miles gives 272 mpg — so the domain marks them and keeps
  // them off the aggregates. Plotting them would put a spike in the chart that the headline denies.
  const points: SparkPoint[] = fuel.entries
    .filter((e) => e.mpg !== null && e.isPlausible)
    .map((e) => ({ date: e.entryDate, mpg: e.mpg as number }))

  const best = fuel.entries.find((e) => e.mpg !== null && e.isPlausible && e.mpg === fuel.bestMpg)
  const worst = fuel.entries.find((e) => e.mpg !== null && e.isPlausible && e.mpg === fuel.worstMpg)
  const last = fuel.entries.at(-1)

  return (
    <Panel className="pad">
      {fuel.averageMpg === null ? (
        <>
          <div className="big num">—</div>
          <div className="big-sub">
            {fuel.fillCount === 0
              ? 'No fills logged yet'
              : `${fuel.fillCount} fill${fuel.fillCount === 1 ? '' : 's'} logged · MPG needs a second fill to measure from`}
          </div>
        </>
      ) : (
        <>
          <div className="big num">
            {fuel.averageMpg.toFixed(1)} <span className="big-unit">MPG</span>
          </div>
          <div className="big-sub">
            {fuel.totalLitres.toFixed(1)} litres over {fuel.fillCount} fill
            {fuel.fillCount === 1 ? '' : 's'}
            {/* The count that catches DEC-012. Thirteen fills, twelve measurable intervals — the design says
                thirteen of both, and two of its headline figures rest on the interval that never happened. */}
            {fuel.measuredIntervalCount !== fuel.fillCount && (
              <> · {fuel.measuredIntervalCount} measurable intervals</>
            )}
            {fuel.implausibleCount > 0 && (
              <> · {fuel.implausibleCount} implausible, excluded</>
            )}
          </div>
        </>
      )}

      {/* An open tank: partial fills logged since the last fill to full. Calm, not a flag — the deferred MPG
          simply arrives at the next full fill, and its litres are already counted in. */}
      {fuel.pendingFillCount > 0 && (
        <div className="big-sub">
          Part-tank in progress · {fuel.pendingFillCount} fill{fuel.pendingFillCount === 1 ? '' : 's'}
          {fuel.pendingMiles !== null && <> · {fuel.pendingMiles.toLocaleString('en-GB')} mi</>} ·{' '}
          {fuel.pendingLitres.toFixed(2)} L — MPG pending next full fill
        </div>
      )}

      <Spark points={points} />

      <div className="mpg-grid">
        <Kv
          label="Best"
          value={fuel.bestMpg === null ? '—' : fuel.bestMpg.toFixed(1)}
          note={best !== undefined ? shortDate(best.entryDate) : 'no measurable interval'}
        />
        <Kv
          label="Worst"
          value={fuel.worstMpg === null ? '—' : fuel.worstMpg.toFixed(1)}
          note={worst !== undefined ? shortDate(worst.entryDate) : 'no measurable interval'}
        />
        <Kv
          label="Avg price/L"
          // Volume-weighted, per DEC-011 — SUM(cost)/SUM(litres), not the mean of the price column. The
          // workbook takes the plain mean and gets 1.5949 against this 1.5973. Not a defect: a different
          // question, answered correctly. It is why that one sits outside the count of five.
          value={fuel.averagePricePerLitre === null ? '—' : `£${fuel.averagePricePerLitre.toFixed(3)}`}
          note={fuel.totalCost > 0 ? `${money(fuel.totalCost)} pumped` : 'nothing pumped yet'}
        />
      </div>

      {last !== undefined && (
        <div className="split">
          <Kv
            label="Last fill"
            value={shortDate(last.entryDate)}
            note={`${last.litres.toFixed(2)} L · ${money(last.totalCost)}`}
          />
          <Kv
            label="That tank"
            value={last.mpg === null ? '· ·' : `${last.mpg.toFixed(1)}`}
            note={
              last.mpg === null
                ? last.unreliableReason === 'AwaitingFullTank'
                  ? 'partial fill · MPG pending your next full fill'
                  : 'no previous fill to measure from'
                : !last.isPlausible
                  ? 'outside the plausible band — a missed fill or a mistyped odometer'
                  : fuel.averageMpg !== null
                    ? `${last.milesSinceLast?.toLocaleString('en-GB') ?? '—'} mi · ${(last.mpg - fuel.averageMpg >= 0 ? '+' : '')}${(last.mpg - fuel.averageMpg).toFixed(1)} vs average`
                    : `${last.milesSinceLast?.toLocaleString('en-GB') ?? '—'} mi`
            }
          />
        </div>
      )}

      <div className="panel-foot">
        <AppLink className="sec-link" to="fuel" reg={reg}>
          Full fuel log →
        </AppLink>
      </div>
    </Panel>
  )
}
