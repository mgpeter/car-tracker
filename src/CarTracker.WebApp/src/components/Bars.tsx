export interface BarDatum {
  name: string
  value: number
  /** Optional: the design tints the first three bars and leaves the rest plain. */
  tone?: 'g1' | 'g2' | 'g3'
}

const money = (n: number) =>
  n.toLocaleString('en-GB', { style: 'currency', currency: 'GBP', minimumFractionDigits: 2 })

/**
 * Proportional spend bars.
 *
 * The whole component is `width: ${pct}%` — no chart library, exactly as the design has it, and it does not
 * need one.
 *
 * **The width is capped and the true figure is never the width.** The design's budget bars clamp to
 * `width:100%` and carry the overage in text beside them; the risk that creates is subtle and worth naming,
 * because it is not the clamp itself. A bar at 158% would draw *outside its track* and, in a flex row, shove
 * the label off the panel — so a cap is right. What is wrong is a cap that reads as "at the limit" when the
 * truth is "half as much again". So `pct` is capped for the geometry only, `over` marks it, and the figure
 * beside it is never the capped one.
 */
export function Bars({ data, total }: { data: BarDatum[]; total: number }) {
  // Everything divides by this. A vehicle with no spend at all is the first day of ownership, not an error.
  const basis = total > 0 ? total : 0

  return (
    <ul className="bars-list">
      {data.map((d) => {
        const raw = basis > 0 ? (d.value / basis) * 100 : 0
        const width = Math.min(raw, 100)
        return (
          <li key={d.name}>
            <span className="bl-name">{d.name}</span>
            <span className="bl-val num">{money(d.value)}</span>
            <span className="track">
              <i className={d.tone} style={{ width: `${width}%` }} />
            </span>
          </li>
        )
      })}
    </ul>
  )
}
