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
 */
describe('the built document', async () => {
  const dist = join(process.cwd(), 'dist/index.html')
  const html = await readFile(dist, 'utf8').catch(() => null)
  const decode = (s: string) => s.replace(/&#39;/g, "'").replace(/&quot;/g, '"').replace(/&amp;/g, '&')

  it.runIf(html !== null)('puts the CSP before the script it governs', () => {
    // The bug this exists for: a <meta> CSP only governs what is parsed after it. With the script first, the
    // policy never sees it — the script runs, the hash is decorative, and a wrong hash fails silently in the
    // direction that looks like success. Both tags are head-prepended, so array order decides this.
    const cspAt = html!.indexOf('Content-Security-Policy')
    const scriptAt = html!.indexOf('data-theme-preload')
    expect(cspAt).toBeGreaterThan(-1)
    expect(scriptAt).toBeGreaterThan(-1)
    expect(cspAt, 'the CSP meta must precede the pre-paint script').toBeLessThan(scriptAt)
  })

  it.runIf(html !== null)('advertises a hash matching the script it actually shipped', () => {
    const script = /<script data-theme-preload[^>]*>([\s\S]*?)<\/script>/.exec(html!)?.[1]
    expect(script).toBeDefined()
    const actual = `sha256-${createHash('sha256').update(script!, 'utf8').digest('base64')}`
    const advertised = /script-src 'self' '([^']+)'/.exec(decode(html!))?.[1]
    expect(advertised, 'CSP hash must match the shipped bytes exactly').toBe(actual)
  })

  it.runIf(html !== null)('runs the script before the stylesheet, so the theme is settled at first paint', () => {
    const scriptAt = html!.indexOf('data-theme-preload')
    const cssAt = html!.indexOf('rel="stylesheet"')
    expect(cssAt).toBeGreaterThan(-1)
    expect(scriptAt).toBeLessThan(cssAt)
  })

  it.runIf(html !== null)('keeps font-src at self, which is the whole of DEC-010', () => {
    expect(decode(html!)).toContain("font-src 'self'")
  })

  it.runIf(html !== null)('allows the Auth0 tenant in connect-src for the login token exchange', () => {
    // A regression to `connect-src 'self'` breaks login in production only (the CSP is build-only) and silently
    // — the browser refuses the token XHR with a console message and the app just never signs in.
    expect(decode(html!)).toContain("connect-src 'self' https://usualexpat.uk.auth0.com")
  })
})
