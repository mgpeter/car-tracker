import { createHash } from 'node:crypto'
import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { THEME_SCRIPT, themeScriptHash } from '../../plugins/theme-csp'

describe('the pre-paint script', () => {
  // The failure mode this guards is silent in the worst way: if the CSP hash and the injected script differ
  // by a single byte, the browser refuses to run the script, says so only in the console, and the app looks
  // exactly like it did before the no-flash work — correct after paint, wrong during it.
  it('hashes to the value the CSP advertises', () => {
    const expected = `sha256-${createHash('sha256').update(THEME_SCRIPT, 'utf8').digest('base64')}`
    expect(themeScriptHash()).toBe(expected)
  })

  it('changes its hash when the script changes', () => {
    expect(themeScriptHash('/* different */')).not.toBe(themeScriptHash(THEME_SCRIPT))
  })

  it('only acts on an explicit choice, leaving system to CSS', () => {
    // If this script ever writes a resolved value for `system`, the theme stops tracking a live OS change
    // and tokens.css's :not([data-theme='light']) branch becomes dead. Keep it dumb.
    expect(THEME_SCRIPT).not.toMatch(/matchMedia|prefers-color-scheme/)
    expect(THEME_SCRIPT).toContain("t==='dark'")
    expect(THEME_SCRIPT).toContain("t==='light'")
  })

  it('cannot throw where storage is unavailable', () => {
    // localStorage throws in private-mode Safari. An exception in <head> stops parsing.
    expect(THEME_SCRIPT).toMatch(/^try\{/)
    expect(THEME_SCRIPT).toMatch(/catch\(e\)\{\}$/)
  })

  it('runs the real script against a stored preference', () => {
    localStorage.setItem('ct-theme', 'dark')
    document.documentElement.removeAttribute('data-theme')

    // eslint-disable-next-line no-new-func -- executing the shipped string is the point of the test
    new Function(THEME_SCRIPT)()

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    localStorage.clear()
    document.documentElement.removeAttribute('data-theme')
  })

  it('leaves the attribute absent when the stored choice is system', () => {
    localStorage.setItem('ct-theme', 'system')
    document.documentElement.removeAttribute('data-theme')

    // eslint-disable-next-line no-new-func -- executing the shipped string is the point of the test
    new Function(THEME_SCRIPT)()

    expect(document.documentElement.hasAttribute('data-theme')).toBe(false)
    localStorage.clear()
  })
})

/**
 * Asserts against `dist/index.html`, so it only runs after a build. `npm run build && npm test` is the CI
 * order; locally, a stale dist is worse than none, hence the explicit skip rather than a silent pass.
 *
 * **Most of what this block used to assert now lives in the gateway**, because the Content-Security-Policy
 * moved there: a build-time meta tag can only ever name the Auth0 tenant the build knew about, which made the
 * app impossible to deploy against anyone else's. What remains here is what the *build* is still responsible
 * for - shipping exactly one pre-paint script, in the right place, and shipping no policy of its own. The
 * policy's contents (the hash, `img-src blob:`, `font-src 'self'`, the tenant in `connect-src`) are asserted
 * against a running container in CI, which is the only place they now exist.
 */
describe('the built document', async () => {
  const dist = join(process.cwd(), 'dist/index.html')
  const html = await readFile(dist, 'utf8').catch(() => null)

  /**
   * The intersection trap, and the reason this is the first test in the block.
   *
   * Multiple policies do not override one another, they intersect - the effective permission is what both
   * allow. A meta tag left behind naming the build's tenant, beside the gateway's header naming the
   * deployment's, would leave `connect-src` as `'self'` alone. Login would then fail on exactly the
   * deployments that had configured themselves correctly, which is about the worst failure ordering
   * available.
   */
  it.runIf(html !== null)('ships no policy of its own, so nothing can intersect the header', () => {
    expect(html!).not.toContain('Content-Security-Policy')
  })

  /**
   * The gateway finds this script by its marker attribute and hashes the bytes between the tags, so the
   * attribute is load-bearing and a second one would make the subject ambiguous.
   */
  it.runIf(html !== null)('ships exactly one marked pre-paint script for the gateway to hash', () => {
    const matches = html!.match(/data-theme-preload/g) ?? []
    expect(matches).toHaveLength(1)

    const script = /<script data-theme-preload[^>]*>([\s\S]*?)<\/script>/.exec(html!)?.[1]
    expect(script, 'the marked script must have a body to hash').toBeTruthy()
    expect(script).toBe(THEME_SCRIPT)
  })

  it.runIf(html !== null)('runs the script before the stylesheet, so the theme is settled at first paint', () => {
    const scriptAt = html!.indexOf('data-theme-preload')
    const cssAt = html!.indexOf('rel="stylesheet"')
    expect(cssAt).toBeGreaterThan(-1)
    expect(scriptAt).toBeLessThan(cssAt)
  })

  /**
   * The runtime config has to arrive before the app module runs, or `authConfig.ts` reads an undefined global
   * and silently falls back to the compiled-in defaults - which on a self-hosted deployment means signing in
   * against this project's Auth0 tenant instead of theirs.
   */
  it.runIf(html !== null)('loads the runtime config before the app module', () => {
    const configAt = html!.indexOf('/config.js')
    const moduleAt = html!.indexOf('type="module"')
    expect(configAt, 'the document must request /config.js').toBeGreaterThan(-1)
    expect(moduleAt).toBeGreaterThan(-1)
    expect(configAt).toBeLessThan(moduleAt)
  })
})
