import { readFile, readdir } from 'node:fs/promises'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { CLIENT_STORAGE, everythingIsExempt } from './clientStorage'

/**
 * The cookie notice renders {@link CLIENT_STORAGE}, so a key written anywhere in this codebase and not
 * declared there is a false statement in a published legal document (DEC-024).
 *
 * This is `coverage.test.ts`'s shape applied to storage instead of to components: scan the source, extract
 * what it actually does, and fail on anything the declaration does not account for. It was checked by adding
 * an undeclared key before it was kept.
 */

const ROOT = process.cwd()
const SCANNED = [join(ROOT, 'src'), join(ROOT, 'plugins')]

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
 * `localStorage.getItem('literal')` and friends, capturing the literal.
 *
 * Only a literal argument is matched. A call passing a variable is reported separately below, because a guard
 * that silently ignored `localStorage.setItem(k, v)` would be defeated by the first refactor that extracts a
 * constant - which is exactly what the four library modules do.
 */
const LITERAL_CALL = /localStorage\.(?:get|set|remove)Item\(\s*['"`]([^'"`]+)['"`]/g
const ANY_CALL = /localStorage\.(?:get|set|remove)Item\(/g

/** Files allowed to call localStorage with a non-literal, because they read their key from the registry. */
const REGISTRY_READERS = [
  'lib/theme.ts',
  'lib/fuelUnit.ts',
  'lib/settings.ts',
  'lib/dismissed.ts',
  'lib/clientStorage.ts',
]

describe('the client-storage registry', () => {
  it('accounts for every key literal in the codebase', async () => {
    const files = (await Promise.all(SCANNED.map(walk))).flat()
    const sources = files.filter((f) => /\.(ts|tsx|html)$/.test(f) && !/\.test\.tsx?$/.test(f))

    const declared = CLIENT_STORAGE.map((e) => e.key)
    const undeclared: string[] = []

    for (const file of sources) {
      const text = await readFile(file, 'utf8')
      for (const match of text.matchAll(LITERAL_CALL)) {
        const key = match[1]!
        // A per-subject key is declared with a placeholder suffix, so match on the prefix before it.
        const covered = declared.some((d) => (d.includes('<') ? key.startsWith(d.split('<')[0]!) : d === key))
        if (!covered) undeclared.push(`${key}  (${file.replace(ROOT, '')})`)
      }
    }

    expect(
      undeclared,
      `these keys are written but not declared in lib/clientStorage.ts, so the cookie notice does not `
        + `mention them:\n  ${undeclared.join('\n  ')}`,
    ).toEqual([])
  })

  it('allows a non-literal key only in the modules that read one from the registry', async () => {
    const files = (await Promise.all(SCANNED.map(walk))).flat()
    const sources = files.filter((f) => /\.(ts|tsx|html)$/.test(f) && !/\.test\.tsx?$/.test(f))

    const offenders: string[] = []

    for (const file of sources) {
      const relative = file.replace(ROOT, '').replaceAll('\\', '/')
      if (REGISTRY_READERS.some((r) => relative.endsWith(r))) continue

      const text = await readFile(file, 'utf8')
      const all = [...text.matchAll(ANY_CALL)].length
      const literal = [...text.matchAll(LITERAL_CALL)].length
      // Every call outside the four library modules must name its key inline, where the scan above can see it.
      if (all > literal) offenders.push(relative)
    }

    expect(
      offenders,
      `these files call localStorage with a computed key, where the registry scan cannot see it. Read the key `
        + `from lib/clientStorage.ts instead:\n  ${offenders.join('\n  ')}`,
    ).toEqual([])
  })

  it('finds the pre-paint theme script outside src/, which is a second reader of a registry key', async () => {
    // `plugins/theme-csp.ts` injects a render-blocking script that reads the theme before React mounts, and
    // its bytes are hashed into the CSP. It is outside src/, so a scan of src/ alone would miss a key the app
    // genuinely writes - which is why SCANNED names both directories.
    const plugin = await readFile(join(ROOT, 'plugins/theme-csp.ts'), 'utf8')
    expect(plugin).toContain('ct-theme')
  })

  it('declares nothing that would need consent, which is what the notice claims', () => {
    // The claim the cookie page makes out loud. If somebody adds analytics, this goes red beside the page that
    // promises there is none, rather than the page quietly continuing to promise it.
    expect(everythingIsExempt()).toBe(true)
  })

  it('gives every entry a purpose and a lifetime, because the page renders both', () => {
    for (const entry of CLIENT_STORAGE) {
      expect(entry.label.length, entry.key).toBeGreaterThan(0)
      expect(entry.purpose.length, entry.key).toBeGreaterThan(0)
      expect(entry.lifetime.length, entry.key).toBeGreaterThan(0)
    }
  })

  it('marks the one key it cannot verify, and only that one', () => {
    // Auth0 composes its key at runtime, so no literal exists to scan for. Stating the exception here keeps it
    // from being mistaken for coverage this guard does not have.
    const unverifiable = CLIENT_STORAGE.filter((e) => e.unverifiable !== undefined)
    expect(unverifiable).toHaveLength(1)
    expect(unverifiable[0]!.key).toContain('auth0')
  })
})
