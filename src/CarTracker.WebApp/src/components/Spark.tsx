export interface SparkPoint {
  date: string
  mpg: number
}

interface SparkProps {
  points: SparkPoint[]
  /** Rendered under the chart and folded into the accessible name. */
  unit?: string
}

const W = 300
const H = 80
const PAD = 6

const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })

const one = (n: number) => n.toFixed(1)

/**
 * The MPG sparkline.
 *
 * §8 defers *real* trend charts — multi-series, tooltips, zoom — and this does not discharge that. The design's
 * chart is a picture of a chart: thirteen hand-typed coordinate pairs with no data behind them. This is the
 * same picture with `points.map()` where the typing was, which is all it ever needed.
 *
 * **Its caption and `aria-label` are hardcoded prose in the design**, and that is the part worth being careful
 * about. `aria-label="Fuel economy across 13 fills, ranging 24.5 to 32.2 MPG, latest 25.4"` is the entire chart
 * for anyone using a screen reader — it is not a decoration on the real thing, it *is* the real thing — and a
 * frozen string is a chart that stops being true after the next fill while continuing to sound authoritative.
 * Both are derived here.
 *
 * The count is derived too, and on the real history it disagrees with the design: thirteen fills but twelve
 * measurable intervals, because the first has no predecessor to measure from. The design plots thirteen points.
 * The thirteenth is DEC-012's phantom — an interval implied by "miles since last = 334" against a previous
 * reading that exists nowhere — and it is one of the five defects this project exists to have caught. It is not
 * plotted here because it never happened.
 */
export function Spark({ points, unit = 'MPG' }: SparkProps) {
  if (points.length === 0) {
    return (
      <div className="spark">
        <p className="spark-empty">
          No measurable intervals yet. MPG needs two fills — the first has nothing to measure from.
        </p>
      </div>
    )
  }

  const values = points.map((p) => p.mpg)
  const lo = Math.min(...values)
  const hi = Math.max(...values)
  // A flat series would divide by zero. Give it a band so the line sits mid-height rather than vanishing.
  const span = hi - lo || 1

  const x = (i: number) => (points.length === 1 ? W / 2 : PAD + (i * (W - PAD * 2)) / (points.length - 1))
  const y = (v: number) => H - PAD - ((v - lo) / span) * (H - PAD * 2)

  const coords = points.map((p, i) => `${x(i).toFixed(1)},${y(p.mpg).toFixed(1)}`)
  const line = coords.join(' ')
  const area = `M${coords.join(' L')} L${x(points.length - 1).toFixed(1)},${H - PAD} L${x(0).toFixed(1)},${H - PAD} Z`

  const first = points[0]!
  const last = points[points.length - 1]!
  const bestIdx = values.indexOf(hi)
  const best = points[bestIdx]!

  const label =
    points.length === 1
      ? `Fuel economy: one measured interval, ${one(last.mpg)} ${unit} on ${shortDate(last.date)}.`
      : `Fuel economy across ${points.length} measured intervals, ranging ${one(lo)} to ${one(hi)} ${unit}. ` +
        `Best ${one(hi)} on ${shortDate(best.date)}. Latest ${one(last.mpg)} on ${shortDate(last.date)}.`

  return (
    <div className="spark">
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={label}>
        <defs>
          <linearGradient id="mpgFill" x1="0" y1="0" x2="0" y2="1">
            <stop className="mpg-stop-a" offset="0%" />
            <stop className="mpg-stop-b" offset="100%" />
          </linearGradient>
        </defs>
        <g className="grid">
          <line x1={PAD} y1={PAD} x2={W - PAD} y2={PAD} />
          <line x1={PAD} y1={H / 2} x2={W - PAD} y2={H / 2} />
          <line x1={PAD} y1={H - PAD} x2={W - PAD} y2={H - PAD} />
        </g>
        {points.length > 1 && <path d={area} fill="url(#mpgFill)" />}
        <polyline className="mpg-line" points={line} />
        <circle className="dot-best" cx={x(bestIdx)} cy={y(hi)} r={2.6} />
        <circle className="dot-last" cx={x(points.length - 1)} cy={y(last.mpg)} r={4} />
      </svg>
      <div className="spark-cap">
        <span>
          {shortDate(first.date)} · {one(first.mpg)}
        </span>
        <span>
          best {one(hi)} · {shortDate(best.date)}
        </span>
        <span>latest {one(last.mpg)}</span>
      </div>
    </div>
  )
}
