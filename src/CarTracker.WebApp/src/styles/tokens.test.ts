import { readFile, readdir } from 'node:fs/promises'
import { join, relative } from 'node:path'
import { compile } from 'tailwindcss'
import { describe, expect, it } from 'vitest'

// Vitest runs from the package root. `import.meta.url` is not a file: URL under the jsdom environment.
const ROOT = process.cwd()
const SRC = join(ROOT, 'src')
const TOKENS = join(SRC, 'styles/tokens.css')

async function walk(dir: string): Promise<string[]> {
  const entries = await readdir(dir, { withFileTypes: true })
  const out = await Promise.all(
    entries.map(async (e) => {
      const p = join(dir, e.name)
      if (e.isDirectory()) return walk(p)
      return /\.(ts|tsx|css)$/.test(e.name) ? [p] : []
    }),
  )
  return out.flat()
}

/** Files allowed to name a colour literally: the token layer defines them, this test asserts about them. */
const DEFINING_FILES = ['styles/tokens.css', 'styles/tokens.test.ts']

describe('the token layer is the only source of colour', () => {
  it('no file outside the token layer references a raw hex colour', async () => {
    const files = await walk(SRC)
    const offenders: string[] = []

    for (const file of files) {
      const rel = relative(SRC, file).replace(/\\/g, '/')
      if (DEFINING_FILES.includes(rel)) continue

      const text = await readFile(file, 'utf8')
      for (const [i, line] of text.split('\n').entries()) {
        // #RGB, #RRGGBB, #RRGGBBAA — but not an #id in a URL fragment or a hex in a comment about one.
        const m = line.match(/#[0-9a-fA-F]{3,8}\b/g)
        if (m && !/^\s*(\*|\/\/|\/\*)/.test(line)) {
          offenders.push(`${rel}:${i + 1}  ${m.join(' ')}  ${line.trim().slice(0, 70)}`)
        }
      }
    }

    expect(offenders, `raw hex colours must be replaced with a semantic token:\n${offenders.join('\n')}`).toEqual([])
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
  })
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
