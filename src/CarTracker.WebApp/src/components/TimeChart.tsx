export interface ChartPoint {
  /** ISO date, YYYY-MM-DD. */
  date: string
  value: number
}

import { useId } from 'react'

export interface ChartSeries {
  id: string
  label: string
  points: ChartPoint[]
}

interface TimeChartProps {
  series: ChartSeries[]
  /** Value-axis unit, e.g. "MPG", "£/L", "£". */
  unit: string
  /** Formats a value for the axis and the end labels. */
  format?: (v: number) => string
  /**
   * The derived accessible name — required, and built from the data by the caller, never a frozen string. For a
   * screen reader this IS the chart; a stale caption "sounds authoritative while being false after the next fill".
   */
  label: string
  height?: number
  /** Shown when there is nothing to plot — a real state, not a blank box. */
  emptyMessage?: string
  /**
   * Single-series only: which direction is "good", so the two extremes can be marked on the good/bad axis —
   * the better extreme green, the worse one rust. `'higher'` for MPG (max is good, min is bad), `'lower'` for
   * L/100 km and £/L (min is good, max is bad). Ignored when there is more than one series.
   */
  good?: 'higher' | 'lower'
}

const W = 320
const PAD_L = 34
const PAD_R = 44 // room for the series end-labels
const PAD_T = 8
const PAD_B = 18

const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' })

const time = (iso: string) => new Date(`${iso}T00:00:00`).getTime()

// Solid, then dashed variants — a greyscale-legible way to tell series apart without relying on hue.
const DASHES = ['0', '5 3', '2 3', '7 3 2 3']

/**
 * A small hand-rolled time-series chart — the real §8 trend chart the `Spark` sparkline stood in for.
 *
 * It generalises `Spark`'s approach (which stays the compact dashboard variant) with a value axis, a time axis
 * and one-or-more series, and keeps the two properties a generic chart library gets wrong for this app: the
 * accessible name is **derived** from the data (passed in by the caller), and series are told apart by
 * position, dash pattern and a direct end-label — never colour alone — so the chart survives greyscale. No
 * library: the app self-hosts under a strict CSP, and the two hard parts are already solved here.
 *
 * A *single* series takes the dashboard `Spark` treatment instead — a green line over a soft green area fade,
 * with its two extremes marked on the good/bad axis (better one green, worse one rust) — since one line needs
 * no telling apart and the fuel trends plot the same economy figure the sparkline does. Multiple series keep
 * the sand/dash/end-label look.
 */
export function TimeChart({ series, unit, format = (v) => v.toFixed(1), label, height = 120, emptyMessage, good }: TimeChartProps) {
  // Unique per instance: the fuel page renders two of these, so a fixed gradient id would collide. Strip the
  // colons React's useId emits so the value is a safe `url(#…)` reference.
  const fillId = `tcfill-${useId().replace(/:/g, '')}`
  const solo = series.length === 1

  const all = series.flatMap((s) => s.points)
  if (all.length === 0) {
    return (
      <div className="tchart">
        <p className="spark-empty">{emptyMessage ?? 'Nothing to plot yet.'}</p>
      </div>
    )
  }

  const H = height
  const values = all.map((p) => p.value)
  const lo = Math.min(...values)
  const hi = Math.max(...values)
  const span = hi - lo || 1

  const times = all.map((p) => time(p.date))
  const tMin = Math.min(...times)
  const tMax = Math.max(...times)
  const tSpan = tMax - tMin || 1

  const x = (iso: string) => PAD_L + ((time(iso) - tMin) / tSpan) * (W - PAD_L - PAD_R)
  const y = (v: number) => H - PAD_B - ((v - lo) / span) * (H - PAD_T - PAD_B)

  const firstDate = all.reduce((a, p) => (time(p.date) < time(a) ? p.date : a), all[0]!.date)
  const lastDate = all.reduce((a, p) => (time(p.date) > time(a) ? p.date : a), all[0]!.date)

  return (
    <div className="tchart">
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={label} preserveAspectRatio="none">
        {/* Value-axis guide lines + labels: just the range, top and bottom. */}
        <g className="tc-grid">
          <line x1={PAD_L} y1={PAD_T} x2={W - PAD_R} y2={PAD_T} />
          <line x1={PAD_L} y1={H - PAD_B} x2={W - PAD_R} y2={H - PAD_B} />
        </g>
        <text className="tc-axis" x={PAD_L - 4} y={PAD_T + 4} textAnchor="end">{format(hi)}</text>
        <text className="tc-axis" x={PAD_L - 4} y={H - PAD_B} textAnchor="end">{format(lo)}</text>
        <text className="tc-axis" x={PAD_L} y={H - 4} textAnchor="start">{shortDate(firstDate)}</text>
        <text className="tc-axis" x={W - PAD_R} y={H - 4} textAnchor="end">{shortDate(lastDate)}</text>

        {solo && (
          <defs>
            <linearGradient id={fillId} x1="0" y1="0" x2="0" y2="1">
              <stop className="tc-fill-a" offset="0%" />
              <stop className="tc-fill-b" offset="100%" />
            </linearGradient>
          </defs>
        )}

        {solo
          ? (() => {
              const pts = [...series[0]!.points].sort((a, b) => time(a.date) - time(b.date))
              const coords = pts.map((p) => `${x(p.date).toFixed(1)},${y(p.value).toFixed(1)}`)
              const last = pts[pts.length - 1]!
              const baseY = H - PAD_B
              // Mark the two extremes on the good/bad axis. Which is good flips with the metric: for "higher"
              // the max is good and the min bad; for "lower" it's the other way round.
              const vals = pts.map((p) => p.value)
              const maxPt = pts[vals.indexOf(Math.max(...vals))]!
              const minPt = pts[vals.indexOf(Math.min(...vals))]!
              const maxClass = good === 'lower' ? 'tc-dot-bad' : 'tc-dot-good'
              const minClass = good === 'lower' ? 'tc-dot-good' : 'tc-dot-bad'
              return (
                <>
                  {pts.length > 1 && (
                    <path
                      className="tc-area"
                      d={`M${coords.join(' L')} L${x(last.date).toFixed(1)},${baseY} L${x(pts[0]!.date).toFixed(1)},${baseY} Z`}
                      fill={`url(#${fillId})`}
                    />
                  )}
                  {pts.length > 1 && <polyline className="tc-line solo" points={coords.join(' ')} />}
                  {good && maxPt !== minPt ? (
                    <>
                      <circle className={maxClass} cx={x(maxPt.date)} cy={y(maxPt.value)} r={4} />
                      <circle className={minClass} cx={x(minPt.date)} cy={y(minPt.value)} r={4} />
                    </>
                  ) : (
                    // No direction (or a flat line): just mark the latest point, neutrally.
                    <circle className="tc-dot-last" cx={x(last.date)} cy={y(last.value)} r={4} />
                  )}
                </>
              )
            })()
          : series.map((s, i) => {
              if (s.points.length === 0) return null
              const pts = [...s.points].sort((a, b) => time(a.date) - time(b.date))
              const line = pts.map((p) => `${x(p.date).toFixed(1)},${y(p.value).toFixed(1)}`).join(' ')
              const last = pts[pts.length - 1]!
              return (
                <g key={s.id}>
                  {pts.length > 1 ? (
                    <polyline className="tc-line" points={line} strokeDasharray={DASHES[i % DASHES.length]} />
                  ) : null}
                  <circle className="tc-dot" cx={x(last.date)} cy={y(last.value)} r={2.4} />
                  {/* Direct end-label: the series names itself where it ends, so there is no colour-only legend. */}
                  <text className="tc-serieslabel" x={x(last.date) + 4} y={y(last.value) + 3}>{s.label}</text>
                </g>
              )
            })}
      </svg>
      <div className="tc-cap">{unit}</div>
    </div>
  )
}
