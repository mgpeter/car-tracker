import { Pill } from './Pill'

/**
 * The wash cadence window, drawn.
 *
 * Where four stat tiles ask the reader to compute "am I inside the 21–28 day window", a bar answers it in one
 * look: elapsed days as a fill, the target window highlighted, and a marker for today. Boxes and fills, so it
 * is CSS, not an SVG — Spark is the app's only hand-rolled SVG and earns it by plotting a series; this does not.
 *
 * The status flips to Overdue past the window's end on the same `sinceLast > max` rule the stat note already
 * applies, so the pill and the note can never disagree.
 */
export function CadenceBar({ sinceLast, min, max }: { sinceLast: number; min: number; max: number }) {
  const scaleMax = Math.max(max + 7, sinceLast + 2)
  const pct = (d: number) => Math.min(100, Math.max(0, (d / scaleMax) * 100))

  const status = sinceLast > max ? 'overdue' : sinceLast >= min ? 'soon' : 'ok'
  const tone = status === 'overdue' ? 'due' : status === 'soon' ? 'soon' : 'ok'
  const label = status === 'overdue' ? 'Overdue' : status === 'soon' ? 'Due soon' : 'OK'

  return (
    <div className="cad">
      <div className="cad-head">
        <span className="cad-day num">Today · day {sinceLast}</span>
        <Pill tone={tone}>{label}</Pill>
      </div>
      <div
        className="cad-track"
        role="img"
        aria-label={`Day ${sinceLast} of the ${min} to ${max} day wash window — ${label.toLowerCase()}.`}
      >
        <div className="cad-window" style={{ left: `${pct(min)}%`, width: `${pct(max) - pct(min)}%` }} />
        <div className={`cad-fill s-${status}`} style={{ width: `${pct(sinceLast)}%` }} />
        <div className="cad-marker" style={{ left: `${pct(sinceLast)}%` }} />
      </div>
      <div className="cad-scale num">
        <span style={{ left: `${pct(min)}%` }}>day {min} · opens</span>
        <span style={{ left: `${pct(max)}%` }}>day {max} · overdue</span>
      </div>
    </div>
  )
}
