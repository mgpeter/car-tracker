# Fonts

Three self-hosted variable faces, served from `'self'` per **DEC-010**. Not vendor blobs — **derived
artifacts**, reproducible with `../../tools/subset-fonts.py`.

| File | Face | Role | Size | Codepoints | Axes |
|---|---|---|---|---|---|
| `oswald-var.woff2` | Oswald | display (`--disp`) | 21,472 B | 226 | `wght 400–700` |
| `inter-var.woff2` | Inter | body (`--body`) | 45,832 B | 234 | `wght 100–900` |
| `jetbrains-mono-var.woff2` | JetBrains Mono | data + labels (`--mono`) | 30,052 B | 234 | `wght 400–800` |

## Provenance

**Oswald** is the original extraction: base64-decoded out of `archive/dashboard-full-claude-design/fonts.css`,
already Latin-subset upstream by the design tool. It has never been re-subset — it needs nothing it can have.

**Inter** and **JetBrains Mono** were re-subset on 2026-07-15 from the upstream variable TTFs
(`github.com/google/fonts`, OFL) to add the seven glyphs **DEC-013 (as amended)** keeps as *text* rather than
icons: `Δ ₂ ≈ ≡ ↔ ↑ ↓`. The design used these in prose — `Δ prior` is a column header, `≈ 206 days` quantifies
an estimate, `₂` sits inside the word "CO₂" — so they cannot be SVG. Without them they fell back to a system
face, which is exactly the silent degradation DEC-010 exists to prevent and which a per-*font* check cannot
see, fallback being per-*glyph*.

Both got **smaller** while gaining coverage (101,160 B → 97,356 B total), because the re-subset also restored
axis parity with the shipped build:

- Inter's upstream source carries an `opsz` axis ours does not. CSS applies `opsz` automatically
  (`font-optical-sizing` defaults to `auto`), so shipping it would have silently changed rendering against a
  build already verified in a browser. It is **pinned to its default**, which also drops its deltas.
- JetBrains Mono's source is `wght 100–800`; the shipped build is `400–800`. **Clamped**, dropping deltas for
  weights nothing asks for.

The script asserts **no codepoint is ever lost** and reports what each source lacks.

## Known gap — one site, unfixable by fonts

**Oswald has no `₂` (U+2082).** `archive/…/tasks.dc.html:184` renders `<h4>Compression + CO₂ sniff test</h4>`,
and `.tcard h4` is `font-family: var(--disp)` — so that heading takes "CO" from Oswald and `₂` from a system
face today. Re-subsetting cannot fix it: the glyph is not in the source at any subset level.

The other three CO₂ sites (`expenses`, `issues`, `service-history`) are body copy and resolve to Inter, which
has it. This one is a **screens-spec decision** for the tasks screen — span the `₂` in the body face, use a real
`<sub>`, or accept the mixed render. Recorded so it is not rediscovered as a mystery.

**`≡` is absent from Inter** (upstream — Inter ships 2,849 codepoints and not that one). It is only ever used in
`.cfoot`, which is `var(--mono)`, so JetBrains Mono covers it. If `≡` ever appears in body copy it will fall
back, and no subsetting will help.

## Regenerating

```bash
pip install fonttools brotli
# fetch the upstream variable TTFs into tools/src-fonts/ (Inter.ttf, JetBrainsMono.ttf)
python tools/subset-fonts.py     # or CT_FONT_SRC=/path/to/ttfs python tools/subset-fonts.py
```

The script pins/clamps axes, patches empty `gvar` entries for zero-width format glyphs (U+200B, U+200D — they
are in the cmap, exist as glyphs, and have no variation data, which fontTools' gvar subsetter assumes away and
raises `KeyError` on), then reports size, codepoint count, axes, gained glyphs and any lost coverage.
