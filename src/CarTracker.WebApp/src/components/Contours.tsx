/**
 * The topographic contour lines behind every page header — the field manual's signature.
 *
 * The design draws four variants across the 17 screens, and three of them are **slices of one path table**:
 * the dossier's five curves are the full set, the garage hero takes the first four, and the standard page head
 * takes the last three (into a shorter viewBox, so the header crops to the upper curves). Only the vehicle
 * card's is genuinely separate — a different size and a lighter stroke.
 *
 * That is why this is one component with a variant, not four SVGs: they were never four drawings.
 *
 * `stroke` is `var(--sand)`; the design hardcodes `#C9B588` in all 19 instances, which is that token's value.
 * `aria-hidden` — it is texture, and describing it to a screen reader would be noise.
 */

/** Ordered top of the header downward. Slices below index into this. */
const CURVES = [
  'M-40,265 C200,205 320,285 520,215 C760,133 900,265 1240,175',
  'M-40,215 C220,155 340,235 540,170 C780,90 920,215 1240,130',
  'M-40,165 C240,115 360,190 560,125 C800,53 940,170 1240,85',
  'M-40,115 C260,75 380,145 580,85 C820,20 960,125 1240,45',
  'M-40,68 C280,38 400,103 600,48 C840,-7 980,88 1240,13',
] as const

/** The vehicle card's own drawing — 400x160 and a lighter stroke, not a slice of the above. */
const CARD_CURVES = [
  'M-20,130 C80,95 140,140 220,105 C300,70 340,120 420,85',
  'M-20,85 C90,55 150,100 230,65 C310,30 350,80 420,45',
] as const

export type ContourVariant = 'phead' | 'hero' | 'dossier' | 'card'

interface VariantSpec {
  viewBox: string
  paths: readonly string[]
  strokeWidth: number
}

const VARIANTS: Record<ContourVariant, VariantSpec> = {
  /** Standard page head, 15 screens. The last three curves in a 200-tall box. */
  phead: { viewBox: '0 0 1200 200', paths: CURVES.slice(2), strokeWidth: 1.3 },
  /** The garage hero. The first four. */
  hero: { viewBox: '0 0 1200 300', paths: CURVES.slice(0, 4), strokeWidth: 1.3 },
  /** The dashboard dossier. All five — the superset the others are cut from. */
  dossier: { viewBox: '0 0 1200 300', paths: CURVES, strokeWidth: 1.3 },
  /** A vehicle card's header strip. */
  card: { viewBox: '0 0 400 160', paths: CARD_CURVES, strokeWidth: 1.2 },
}

export function Contours({ variant }: { variant: ContourVariant }) {
  const { viewBox, paths, strokeWidth } = VARIANTS[variant]

  return (
    <svg className="contours" viewBox={viewBox} preserveAspectRatio="xMidYMid slice" aria-hidden="true">
      <g fill="none" stroke="var(--sand)" strokeWidth={strokeWidth}>
        {paths.map((d) => (
          <path key={d} d={d} />
        ))}
      </g>
    </svg>
  )
}
