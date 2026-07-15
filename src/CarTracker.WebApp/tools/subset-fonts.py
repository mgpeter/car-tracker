"""Re-subset the self-hosted faces, adding the 7 text glyphs DEC-013 (as amended) keeps as text.

Preserves each face's EXISTING coverage exactly and adds only what it lacks, so this cannot silently drop a
glyph that already worked. Oswald is untouched: it needs nothing it can have (see the report).
"""
import os
from fontTools.ttLib import TTFont
from fontTools.subset import Subsetter, Options
from fontTools.varLib import instancer

SRC = os.environ.get('CT_FONT_SRC', os.path.join(os.path.dirname(os.path.abspath(__file__)), 'src-fonts'))
DST = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'src', 'assets', 'fonts')

# The 7 glyphs that are prose, not icons (DEC-013 amended). ≡ is mono-only; ₂ is inside the word "CO₂".
NEW = {0x0394: 'Δ', 0x2082: '₂', 0x2248: '≈', 0x2261: '≡', 0x2194: '↔', 0x2191: '↑', 0x2193: '↓'}

JOBS = [
    # (source ttf, shipped woff2, axis limits — 'pin' = pin to default, (lo, hi) = clamp the range)
    ('Inter.ttf', 'inter-var.woff2', {'opsz': 'pin'}),
    ('JetBrainsMono.ttf', 'jetbrains-mono-var.woff2', {'wght': (400, 800)}),
]


def cmap_of(path):
    f = TTFont(path)
    return set().union(*[set(t.cmap) for t in f['cmap'].tables])


def axes_of(path):
    f = TTFont(path)
    return [(a.axisTag, a.minValue, a.maxValue) for a in f['fvar'].axes] if 'fvar' in f else None


for src_name, out_name, limits in JOBS:
    src = os.path.join(SRC, src_name)
    out = os.path.join(DST, out_name)

    before_size = os.path.getsize(out)
    before_cps = cmap_of(out)
    before_axes = axes_of(out)

    font = TTFont(src, lazy=False)

    # Match the shipped build's axes exactly. Two reasons, both about parity with what has already been
    # verified in a browser rather than about bytes:
    #   - Inter's Google source carries an opsz axis our build does not. CSS applies opsz automatically
    #     (font-optical-sizing defaults to auto), so keeping it would change rendering. Pinned to its default.
    #   - JetBrains Mono's source is wght 100..800; the shipped build is 400..800. Clamping restores that and
    #     drops deltas for weights 100-400 that nothing asks for.
    if limits and 'fvar' in font:
        defaults = {a.axisTag: a.defaultValue for a in font['fvar'].axes}
        resolved = {t: (defaults[t] if v == 'pin' else v) for t, v in limits.items() if t in defaults}
        if resolved:
            font = instancer.instantiateVariableFont(font, resolved)

    # fontTools' gvar subsetter does `{g: variations[g] for g in retained_glyphs}` and assumes every retained
    # glyph has a gvar entry. Zero-width format characters (U+200B ZWSP, U+200D ZWJ) are in the shipped
    # subset's cmap, exist as glyphs in the source, and have NO gvar entry — they are invisible, so they have
    # nothing to vary. That combination raises KeyError. Give them the empty entry they should already have,
    # rather than dropping the codepoints and quietly narrowing coverage.
    if 'gvar' in font:
        gvar = font['gvar']
        for g in font.getGlyphOrder():
            if g not in gvar.variations:
                gvar.variations[g] = []

    src_cps = set().union(*[set(t.cmap) for t in font['cmap'].tables])
    wanted = set(before_cps) | (set(NEW) & src_cps)
    unavailable = set(NEW) - src_cps

    opts = Options()
    opts.flavor = 'woff2'
    # NOT ['*']: keeping every GSUB feature pulls uni200D (ZWJ) into the glyph closure via a layout rule,
    # and it has no gvar entry, so fontTools raises KeyError. Inter's cmap does not even contain U+200D.
    # The default set (kern, liga, calt, ...) is what the shipped build already had.
    opts.name_IDs = ['*']
    opts.name_legacy = True
    opts.notdef_outline = True
    opts.recalc_bounds = True
    sub = Subsetter(opts)
    sub.populate(unicodes=wanted)
    sub.subset(font)
    font.flavor = 'woff2'
    font.save(out)

    after_size = os.path.getsize(out)
    after_cps = cmap_of(out)
    after_axes = axes_of(out)

    lost = before_cps - after_cps
    gained = sorted(NEW[c] for c in (after_cps & set(NEW)))
    missing = sorted(NEW[c] for c in unavailable)

    print(f'{out_name}')
    print(f'   size   {before_size:>7} -> {after_size:>7} B  ({after_size - before_size:+d})')
    print(f'   cps    {len(before_cps):>7} -> {len(after_cps):>7}')
    print(f'   axes   {before_axes} -> {after_axes}')
    print(f'   gained {" ".join(gained) or "-"}')
    print(f'   source lacks: {" ".join(missing) or "none"}')
    print(f'   REGRESSION - lost codepoints: {sorted(lost) if lost else "none"}')
    print()
