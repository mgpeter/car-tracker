/**
 * The icon sprite (DEC-013, as amended 2026-07-15).
 *
 * Eight symbols, replacing the glyphs the design used as icons: → ＋ ✓ ▾ ⌂ ⇄ ⠿ ⚙. Those render today only
 * because the *system font* supplies them — none is in the subset, and under DEC-010's `font-src 'self'` that
 * is a silent per-glyph fallback the CSP cannot see. Re-subsetting cannot fix them either: ⠿ is a Braille
 * pattern, and ⌂/⚙/⇄ ship in no Inter, Oswald or JetBrains Mono at any subset level.
 *
 * The other seven glyphs DEC-013 originally swept in here (Δ ₂ ≈ ≡ ↔ ↑ ↓) are NOT icons and are not here —
 * `₂` sits inside the word "CO₂". They go in the font subset instead. See `public/fonts/README.md`.
 *
 * Inline TSX in src/, deliberately, rather than a file in public/:
 *   - `public/` is outside `tokens.test.ts`'s walk. That is exactly how the Vite starter's `icons.svg` sat
 *     there carrying a raw #aa3bff. Here the guard forces `currentColor`.
 *   - An external `<use href="/icons.svg#x">` is a fetch, under a CSP this repo actually enforces.
 *   - No icon FOUC.
 *
 * House style is the design's own, taken from the `.fsel` chevron it drew in a data: URI:
 * fill:none, stroke-linecap:round, stroke-width ~1.6 at a 10px box — scaled here to a 24px box.
 */
export function IconSprite() {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      style={{ display: 'none' }}
      aria-hidden="true"
      data-icon-sprite=""
    >
      <symbol id="ct-arrow-right" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M4 12h15" />
        <path d="M13 6l6 6-6 6" />
      </symbol>

      <symbol id="ct-plus" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
        <path d="M12 5v14" />
        <path d="M5 12h14" />
      </symbol>

      <symbol id="ct-check" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M4 12.5l5.5 5.5L20 6" />
      </symbol>

      {/* Matches the chevron the design draws for .fsel / .field select, so the caret and the select agree. */}
      <symbol id="ct-caret-down" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M5 9l7 7 7-7" />
      </symbol>

      <symbol id="ct-home" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M4 11l8-7 8 7" />
        <path d="M6 9.5V20h12V9.5" />
      </symbol>

      {/* ⇄ — the auto-mirror badge: a fill becomes an expense. Two arrows, opposed. */}
      <symbol id="ct-mirror" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M4 9h16" />
        <path d="M16 5l4 4-4 4" />
        <path d="M20 15H4" />
        <path d="M8 11l-4 4 4 4" />
      </symbol>

      {/* ⠿ U+283F — Braille dots 1-6. A drag grip: two columns, three rows. */}
      <symbol id="ct-grip" viewBox="0 0 24 24" fill="currentColor" stroke="none">
        <circle cx="9" cy="6" r="1.6" />
        <circle cx="15" cy="6" r="1.6" />
        <circle cx="9" cy="12" r="1.6" />
        <circle cx="15" cy="12" r="1.6" />
        <circle cx="9" cy="18" r="1.6" />
        <circle cx="15" cy="18" r="1.6" />
      </symbol>

      <symbol id="ct-gear" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
        <circle cx="12" cy="12" r="3.2" />
        <path d="M12 2.6v3M12 18.4v3M21.4 12h-3M5.6 12h-3M18.6 5.4l-2.1 2.1M7.5 16.5l-2.1 2.1M18.6 18.6l-2.1-2.1M7.5 7.5L5.4 5.4" />
      </symbol>

      {/* The dashboard warning triangle — the bottom-nav status glyph for a due-soon or overdue screen.
          A car cluster's tell-tale, coloured by tone (amber/red) via currentColor, not by the glyph. */}
      <symbol id="ct-warning" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M12 3.6 21.4 20H2.6Z" />
        <path d="M12 9.5v4.2" />
        <path d="M12 17.2v.01" />
      </symbol>
    </svg>
  )
}
