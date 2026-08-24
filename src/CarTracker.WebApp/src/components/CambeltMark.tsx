/**
 * The Cambelt mark — a toothed belt as a stadium ring with tooth ticks, drawn beside the wordmark.
 *
 * `currentColor` throughout, so it takes its colour from whatever it sits in: `--sand` on `.brand` and
 * `.eyebrow`, brightening to `--head-fg` when the brand link is hovered. That is the reason it is inlined
 * rather than imported as a file — an `<img>` cannot follow a hover, and the cream variant in
 * `archive/assets/svg/` hardcodes `#ECE4D3`, which the hex guard in `styles/tokens.test.ts` walks `src/`
 * looking for. No hex here, so no exemption is needed.
 *
 * **This is the small geometry: three teeth per row, on a 32 grid.** The mark's own rule is six teeth at
 * 44px and above, three below it, because six close up and the ring reads as a solid blob once each tooth
 * lands on less than a pixel. Both call sites render at 20px. The six-tooth drawing, for anything large
 * enough to earn it, is `archive/assets/svg/cambelt-icon-currentcolor.svg` on a 512 grid.
 *
 * No `<title>` and `aria-hidden`: the wordmark next to it already says the name, so a title would give the
 * brand link two accessible names and add text content to a node that tests match by exact text.
 */

/** Tooth centres along the ring, left to right. Mirrored top and bottom. */
const TEETH = [7.42, 15.5, 23.58] as const

/** Top row sits under the ring's inner edge; bottom row above it. */
const ROWS = [10.3, 19.2] as const

export function CambeltMark({ className = 'brand-mark' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 32 32" aria-hidden="true" focusable="false">
      <rect x="3.39" y="9.15" width="25.22" height="13.7" rx="6.85" fill="none" stroke="currentColor" strokeWidth="2.3" />
      {ROWS.map((y) => TEETH.map((x) => <rect key={`${x}-${y}`} x={x} y={y} width="1" height="2.5" fill="currentColor" />))}
    </svg>
  )
}
