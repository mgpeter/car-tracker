import { readFile, readdir } from 'node:fs/promises'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

const SRC = join(process.cwd(), 'src')

async function walk(dir: string): Promise<string[]> {
  const entries = await readdir(dir, { withFileTypes: true }).catch(() => [])
  const out = await Promise.all(
    entries.map(async (e) => {
      const p = join(dir, e.name)
      return e.isDirectory() ? walk(p) : [p]
    }),
  )
  return out.flat()
}

/**
 * Components exempt from needing their own axe test, with the reason each is covered elsewhere.
 *
 * An exemption list is only honest if it is short and each line says why. If this grows, the rule is being
 * worked around rather than followed.
 */
const EXEMPT: Record<string, string> = {
  IconSprite: 'a hidden <symbol> library — it renders nothing; Icon.test covers what comes out of it',
  Gallery: 'the axe surface itself — Gallery.test renders it and sweeps the whole page',
  GalleryPage: 'ditto — swept as a whole',
  Row: 'a local layout span inside the gallery, not exported vocabulary',
  App: 'renders GalleryPage, which is swept',
  RuleMark: 'an <i> inside SectionHead; swept via the primitives composition',
  Num: 'a <span> wrapper with no semantics of its own',
  NavMoreSheet: 'rendered by AppShell; shell.test sweeps it with the More sheet open',
  Footer: 'rendered by AppShell, which is swept',
  VehicleCard: 'rendered by GaragePage; GaragePage.test sweeps the page with a card on it',
  AddVehicleSheet: 'rendered by GaragePage; GaragePage.test sweeps it with the sheet open',
  GarageError: 'a local error panel inside GaragePage, swept with it',
  LinkProvider: 'a context provider — renders only its children',
  ThemeProvider: 'a context provider — renders only its children',
  ToastProvider: 'a context provider; its live region is swept via the shell and the gallery',
  AppLink: 'renders an <a>; swept wherever the shell is',
  StatutoryPanel: 'rendered by SettingsPage; SettingsPage.test sweeps the page with it loaded',
  CheckDefinitionsPanel: 'ditto — swept with the page, in both the empty and populated states',
}

/**
 * Task 4.7 says "run vitest-axe across every ported component". That instruction rots silently: someone adds
 * a component, forgets the axe test, and the suite still passes — the sweep quietly covers less each time
 * while reporting the same green.
 *
 * So the coverage itself is a test. Every exported component either has `axe(` in a test file that imports
 * it, or is on the exemption list above with a stated reason.
 */
describe('axe coverage cannot rot', () => {
  it('every exported component is swept by axe, or exempt for a stated reason', async () => {
    const files = await walk(SRC)
    const sources = files.filter((f) => /\.tsx$/.test(f) && !/\.test\.tsx$/.test(f))
    const tests = files.filter((f) => /\.test\.tsx?$/.test(f))

    const testText = await Promise.all(tests.map((f) => readFile(f, 'utf8')))
    const axeTests = testText.filter((t) => t.includes('axe('))
    // The gallery is swept whole by Gallery.test, so anything it renders is genuinely covered. Making that
    // count is what turns this guard into something useful rather than a list of excuses: a new component
    // earns its coverage by appearing in the gallery — which is also where the greyscale and visual checks
    // happen. The guard therefore keeps the gallery complete, not just the axe calls.
    const gallery = await readFile(join(SRC, 'gallery/Gallery.tsx'), 'utf8')

    const missing: string[] = []

    for (const file of sources) {
      const text = await readFile(file, 'utf8')
      // `export function Foo(` — the whole codebase declares components this way.
      const exported = [...text.matchAll(/export function ([A-Z]\w*)/g)].map((m) => m[1]!)

      for (const name of exported) {
        if (name in EXEMPT) continue
        const pattern = new RegExp(`\\b${name}\\b`)
        const covered = axeTests.some((t) => pattern.test(t)) || pattern.test(gallery)
        if (!covered) missing.push(name)
      }
    }

    expect(
      missing,
      `these components have no axe coverage. Add one, or add an exemption with a reason:\n  ${missing.join('\n  ')}`,
    ).toEqual([])
  })

  it('the exemption list stays honest', async () => {
    // Every exemption must name something that still exists. A stale exemption is how a real gap hides.
    const files = await walk(SRC)
    const sources = files.filter((f) => /\.tsx$/.test(f))
    const all = new Set<string>()
    for (const file of sources) {
      const text = await readFile(file, 'utf8')
      for (const m of text.matchAll(/(?:export )?function ([A-Z]\w*)/g)) all.add(m[1]!)
    }

    const stale = Object.keys(EXEMPT).filter((name) => !all.has(name))
    expect(stale, `exemptions for components that no longer exist: ${stale.join(', ')}`).toEqual([])
  })
})
