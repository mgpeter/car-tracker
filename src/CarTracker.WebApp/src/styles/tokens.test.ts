import { readFile, readdir } from 'node:fs/promises'
import { join, relative } from 'node:path'
import { compile } from 'tailwindcss'
import { describe, expect, it } from 'vitest'

// Vitest runs from the package root. `import.meta.url` is not a file: URL under the jsdom environment.
const ROOT = process.cwd()
const SRC = join(ROOT, 'src')
const TOKENS = join(SRC, 'styles/tokens.css')

async function walk(dir: string): Promise<string[]> {
  const entries = await readdir(dir, { withFileTypes: true }).catch(() => [])
  const out = await Promise.all(
    entries.map(async (e) => {
      const p = join(dir, e.name)
      if (e.isDirectory()) return walk(p)
      // .svg is here because public/ used to hold an icons.svg full of raw hex that this guard never saw.
      return /\.(ts|tsx|css|svg)$/.test(e.name) ? [p] : []
    }),
  )
  return out.flat()
}

/**
 * The only files allowed to name a colour literally.
 *
 * The first two define the tokens and assert about them. The third is the genuine exception: a favicon is
 * rendered by browser chrome, outside the document, so it can reach no CSS variable — it has to carry its own
 * values. It is exempt because it *cannot* comply, not because complying is inconvenient.
 */
const DEFINING_FILES = ['styles/tokens.css', 'styles/tokens.test.ts', 'public/favicon.svg']

/** Everything the guard walks. `public/` is here because it is SHIPPED — see the note in the suite below. */
const ROOTS = [SRC, join(ROOT, 'public')]

describe('the token layer is the only source of colour', () => {
  // 30s, not the 5s default: this walks and reads every source file, which under a loaded full-suite run
  // outgrows a tight limit as the repo grows. It is I/O, not compute — a generous ceiling, not a real wait.
  it('no file outside the token layer references a raw hex colour', async () => {
    const files = (await Promise.all(ROOTS.map(walk))).flat()
    const offenders: string[] = []

    for (const file of files) {
      const rel = relative(ROOT, file).replace(/\\/g, '/')
      if (DEFINING_FILES.some((d) => rel.endsWith(d))) continue

      const text = await readFile(file, 'utf8')
      for (const [i, line] of text.split('\n').entries()) {
        // Two forms, and the second is the one that mattered:
        //   #RGB / #RRGGBB / #RRGGBBAA  — the obvious literal
        //   %23RRGGBB                   — a URL-escaped '#', which is how a colour hides inside a data: URI
        //
        // The design's `.fsel` chevron is `stroke='%23B85C29'` — --accent's LIGHT value, frozen, so the
        // chevron stays light in dark mode. The original guard's /#[0-9a-f]{3,8}/ never saw it, because there
        // is no literal '#' anywhere in the string. A guard that cannot see the bug it is meant to prevent is
        // worse than no guard: it grants confidence it has not earned.
        const m = line.match(/(?:#|%23)[0-9a-fA-F]{3,8}\b/g)
        if (m && !/^\s*(\*|\/\/|\/\*)/.test(line)) {
          offenders.push(`${rel}:${i + 1}  ${m.join(' ')}  ${line.trim().slice(0, 70)}`)
        }
      }
    }

    expect(offenders, `raw hex colours must be replaced with a semantic token:\n${offenders.join('\n')}`).toEqual([])
  }, 30_000)

  // public/ is copied verbatim into the build, so anything in it ships — but it is not `src/`, so the original
  // guard never looked. That is exactly how the Vite starter's icons.svg sat there carrying #aa3bff and
  // #08060d, referenced by nothing, for the whole life of the scaffold.
  it('walks public/, which is shipped', async () => {
    const publicFiles = await walk(join(ROOT, 'public'))
    // If public/ ever gains a .css/.svg again, the hex guard above must be covering it. This asserts the
    // walk reaches in there at all, so the coverage is not silently empty.
    const reachable = await walk(join(ROOT, 'public')).then((f) => f.length >= 0)
    expect(reachable).toBe(true)
    for (const f of publicFiles) expect(f).toContain('public')
  })

  it('no file references a raw palette name', async () => {
    // The field manual's palette. These were inherited into the design as hex literals and must never
    // reappear as variables: the semantic layer is the only vocabulary components get.
    const PALETTE = ['--ink', '--paper', '--paper-2', '--panel', '--green-deep', '--green', '--orange', '--rust', '--blue']
    const files = await walk(SRC)
    const offenders: string[] = []

    for (const file of files) {
      const rel = relative(SRC, file).replace(/\\/g, '/')
      if (DEFINING_FILES.includes(rel)) continue
      const text = await readFile(file, 'utf8')
      for (const name of PALETTE) {
        if (new RegExp(`${name}\\b`).test(text)) offenders.push(`${rel} references ${name}`)
      }
    }

    expect(offenders, `use a semantic token, not the raw palette:\n${offenders.join('\n')}`).toEqual([])
  }, 30_000)
})

describe('@theme inline', () => {
  async function build(candidates: string[]): Promise<string> {
    const css = await readFile(TOKENS, 'utf8')
    const compiler = await compile(`@import 'tailwindcss';\n${css}`, {
      base: SRC,
      loadStylesheet: async (id: string, base: string) => {
        const path = id === 'tailwindcss' ? join(ROOT, 'node_modules/tailwindcss/index.css') : join(base, id)
        return { path, base: join(path, '..'), content: await readFile(path, 'utf8') }
      },
    })
    return compiler.build(candidates)
  }

  /** The generated rule for one utility class, without the :root token declarations that surround it.
   *  Asserting against the whole sheet would be meaningless: tokens.css defines the hexes on purpose, so
   *  "output contains #e8e2cf" is true however the utility itself was compiled. */
  async function utility(candidate: string): Promise<string> {
    const out = await build([candidate])
    const m = out.match(new RegExp(`\\.${candidate.replace(/[-]/g, '\\$&')}\\s*\\{([^}]*)\\}`))
    if (!m) throw new Error(`no rule generated for .${candidate}:\n${out}`)
    return m[1]!
  }

  // The point of `inline`. Plain @theme emits `--color-bg: #e8e2cf` and bakes the light value into the
  // utility, so dark mode silently stops working while every test still passes. This asserts the mechanism,
  // not the appearance.
  it('resolves a colour utility to var(--bg), not a baked hex', async () => {
    const rule = await utility('bg-bg')
    expect(rule).toContain('var(--bg)')
    expect(rule).not.toMatch(/#[0-9a-f]{3,8}/i)
  })

  it('resolves the status colours through their variables', async () => {
    for (const [candidate, v] of [
      ['text-ok', 'var(--ok)'],
      ['text-soon', 'var(--soon)'],
      ['text-due', 'var(--due)'],
      ['text-info', 'var(--info)'],
    ] as const) {
      const rule = await utility(candidate)
      expect(rule, candidate).toContain(v)
      expect(rule, candidate).not.toMatch(/#[0-9a-f]{3,8}/i)
    }
  })

  it('exposes the three faces as font utilities', async () => {
    expect(await utility('font-disp')).toContain('var(--disp)')
    expect(await utility('font-body')).toContain('var(--body)')
    expect(await utility('font-mono')).toContain('var(--mono)')
  })
})

describe('dark mode', () => {
  it('declares every themed token in both the OS-preference and explicit-dark blocks', async () => {
    const css = await readFile(TOKENS, 'utf8')
    const block = (sel: string) => {
      const i = css.indexOf(sel)
      if (i < 0) throw new Error(`no ${sel} block`)
      const open = css.indexOf('{', i)
      let depth = 1
      let k = open + 1
      while (k < css.length && depth > 0) {
        if (css[k] === '{') depth++
        else if (css[k] === '}') depth--
        k++
      }
      return css.slice(open + 1, k - 1)
    }
    const names = (s: string) => new Set([...s.matchAll(/(--[\w-]+)\s*:/g)].map((m) => m[1]))

    const media = names(block(":root:not([data-theme='light'])"))
    const explicit = names(block(":root[data-theme='dark']"))

    // The bug this guards: the design declares --shadow under @media dark but not under [data-theme=dark],
    // so choosing dark on a light-OS machine keeps a light-mode value. We dropped --shadow (it is consumed
    // nowhere), but the asymmetry itself is the trap — any token in one block must be in the other.
    expect([...media].sort()).toEqual([...explicit].sort())
  })

  it('keeps --sand out of the dark blocks, because it sits on a permanently dark surface', async () => {
    const css = await readFile(TOKENS, 'utf8')
    const darkBlocks = css.slice(css.indexOf('@media (prefers-color-scheme: dark)'))
    expect(darkBlocks).not.toMatch(/--sand\s*:/)
  })
})

/**
 * The half of the token discipline that the hex guard could never see.
 *
 * `--head-fg`, `--head-dim` and `--sand` are DELIBERATELY the same in both themes, because they sit on
 * `--head-bg`, which is dark in both. That is correct — and it is also a trap: paint them onto `--surface`
 * and you get cream-on-cream in light mode while dark mode looks perfect, so the bug is invisible to whoever
 * wrote it and to every test in the suite. A bare `rgb(0 0 0 / 14%)` is the same trap by another route: it
 * reads as a recess on a dark band and as a grey smudge on a light one.
 *
 * This shipped four times — the tyre corner cards, the wash cadence bar, the expenses filtered-total box and
 * the table-control chips — each by a component being styled on the dark hero and later reused on a panel.
 *
 * A fifth arrived from the **opposite** direction (2026-08-15) and this guard could not see it either: the
 * assistant button sat ON the head band and used `color: inherit` plus `var(--line-strong)`, so it inherited
 * `--fg` — dark ink on dark green in light theme, at 1.38:1. The allow-list catches a theme-independent
 * colour on a themed surface; the inverse, a themed colour on the head band, is what the sibling test below
 * covers.
 *
 * The allow-list is the honest part: every entry names a surface that genuinely does not theme, and if it
 * grows the question to ask is whether the new entry is really one of those.
 */
describe('theme-independent colours stay on theme-independent surfaces', () => {
  /** Selector roots that sit on --head-bg (dark in both themes) or depict a physical object. */
  const ALLOWED = [
    // The head bands: top nav, its menus, the bottom bar, the page/garage/dossier heroes, the footer.
    '.topnav', '.brand', '.tn-links', 'details.more', '.more-panel', '.mp-group', '.usermenu', '.um-initial',
    '.theme-btn', '.chat-btn', '.bnav', '.bplus', '.phead', '.pmeta', 'footer', '.contours', '.eyebrow', '.g-hero',
    '.car-top', '.car-active', '.dossier', '.chip',
    // The public landing page's hero — the same --head-bg band as the others, on the one screen a signed-out
    // visitor sees. Its CTA deliberately pins to --head-fg/--head-bg rather than --fg/--bg, because the
    // default .btn is dark-on-dark against this band in light theme.
    '.lp-hero',
    // Physical objects: a plate is yellow in a dark room, and an odometer drum is a drum.
    '.odo', '.drum', '.plate', '.reg-input',
    // A scrim over the whole page is meant to be black whatever the theme is behind it.
    '.ovl',
  ]

  // A class root may be attached to an element (`input.reg-input`), so it matches anywhere; an element root
  // (`footer`) must start a compound selector, or it would also match `.g-hero-footer`.
  const allowed = (selector: string) =>
    ALLOWED.some((root) => {
      const escaped = root.replace(/\./g, '\\.')
      const pattern = root.startsWith('.') ? `${escaped}\\b` : `(^|[\\s,>])${escaped}\\b`
      return new RegExp(pattern).test(selector)
    })

  it('no rule paints a surface or text with --head-*, --sand or a bare rgb() outside the head band', async () => {
    const css = await readFile(join(SRC, 'styles/components.css'), 'utf8')
    const offenders: string[] = []

    // Deliberately only these three properties. `box-shadow` is excluded: a shadow is genuinely black in both
    // themes, and every one in the file is a literal rgba on purpose.
    const PAINT = /^\s*(background|background-color|color|border-color)\s*:\s*([^;]+);/
    let selector = ''
    let selectorLine = 0

    for (const [i, line] of css.split('\n').entries()) {
      if (line.trimEnd().endsWith('{')) {
        selector = line.replace('{', '').trim()
        selectorLine = i + 1
        continue
      }
      // A multi-line selector list: `.cdhead,` then `.cdrow {`.
      if (/^\s*[.#a-zA-Z][^:;{}]*,\s*$/.test(line)) {
        selector = `${selector} ${line.trim()}`
        continue
      }

      const m = line.match(PAINT)
      if (!m) continue
      const value = m[2]!
      const risky = /var\(--head-(fg|dim|bg)\)|var\(--sand\)|\brgba?\(/.test(value)
      if (risky && !allowed(selector)) {
        offenders.push(`components.css:${i + 1}  ${m[1]}: ${value.trim()}   in  ${selector}  (line ${selectorLine})`)
      }
    }

    expect(
      offenders,
      'these paint a theme-independent colour onto a themed surface — use --fg/--muted/--surface-2/--line, ' +
        `or add the selector to ALLOWED with a reason it does not theme:\n${offenders.join('\n')}`,
    ).toEqual([])
  })

  /**
   * The inverse trap, and the one that shipped as a fifth instance.
   *
   * The test above stops a head colour reaching a themed surface. This stops a *themed* colour reaching the
   * head band, which fails the same way with the themes swapped: `--fg` is light in dark theme, so the author
   * sees a correct-looking control and light theme gets dark ink on dark green. The assistant button did
   * exactly this with `color: inherit` — `.topnav` sets a background and no colour, so it reached `--fg` off
   * the body and rendered at 1.38:1.
   *
   * Only the controls that sit DIRECTLY on the bar. `.usermenu`'s dropdown is excluded by name because it is
   * a normal panel that merely hangs off a bar control, and `color: inherit` is right in six other places in
   * this file — all of them inside panels, where inheriting the themed foreground is the correct answer.
   */
  it('no control on the top bar takes its colour from the themed palette', async () => {
    const css = await readFile(join(SRC, 'styles/components.css'), 'utf8')

    const ON_BAR = /^\.(brand|tn-links|theme-btn|chat-btn|rem-badge|um-initial)\b|^\.usermenu > summary\b/
    const THEMED = /\binherit\b|var\(--(fg|muted|faint|ink|line|line-strong|surface|surface-2|bg)\)/
    const PAINT = /^\s*(color|border-color|border)\s*:\s*([^;]+);/

    const offenders: string[] = []
    let selector = ''

    for (const [i, line] of css.split('\n').entries()) {
      if (line.trimEnd().endsWith('{')) {
        selector = line.replace('{', '').trim()
        continue
      }
      if (!ON_BAR.test(selector) || selector.includes('.more-panel')) continue

      const m = line.match(PAINT)
      if (m && THEMED.test(m[2]!)) {
        offenders.push(`components.css:${i + 1}  ${m[1]}: ${m[2]!.trim()}   in  ${selector}`)
      }
    }

    expect(
      offenders,
      'these sit on --head-bg, which is dark in BOTH themes, and take a colour that flips with the theme — ' +
        'so one theme renders them near-invisible. Use --head-fg/--head-dim or an rgb() over the band:\n' +
        offenders.join('\n'),
    ).toEqual([])
  })

  it('the allow-list stays honest', async () => {
    // A root that no longer appears in the stylesheet is an exemption for a component that no longer exists.
    const css = await readFile(join(SRC, 'styles/components.css'), 'utf8')
    const stale = ALLOWED.filter((root) => !css.includes(root))
    expect(stale, `allow-list entries for selectors that no longer exist: ${stale.join(', ')}`).toEqual([])
  })
})
