import { readFile } from 'node:fs/promises'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

/**
 * Guards against the one layout bug jsdom can never see: a row of controls widening the document.
 *
 * The suite renders into jsdom, which has no layout engine — it computes no widths and no overflow, so a
 * strip that runs off a phone screen passes every existing test. This one is therefore a source assertion,
 * the same shape as the colour-literal guard in `tokens.test.ts`.
 *
 * The bug it exists for (2026-08-08): `.tctl-chips` was `display: inline-flex` with no `flex-wrap`. Its
 * parent `.tctl` wraps, but that only breaks BETWEEN groups, and a flex item's default `min-width: auto`
 * refuses to shrink below its content — so a category list longer than the viewport pushed the page wider
 * than the screen. The whole document scrolled sideways and carried the fixed bottom nav off with it.
 * The dossier's `.chips` had `flex-wrap: wrap` all along; the control-strip variant was the one that didn't.
 */
const COMPONENTS = join(process.cwd(), 'src/styles/components.css')

/** The declaration block for a top-level rule, or null when the selector is absent. */
function declarations(css: string, selector: string): string | null {
  // Deliberately simple: these are flat, hand-written rules, not nested or generated ones.
  const start = css.indexOf(`\n${selector} {`)
  if (start === -1) return null
  const open = css.indexOf('{', start)
  const close = css.indexOf('}', open)
  return close === -1 ? null : css.slice(open + 1, close)
}

describe('control rows cannot widen the document', () => {
  /**
   * Every row that lays out a list whose length comes from DATA, not from the design. Each is a flex
   * container holding one item per category, status or kind — so its width is set by how much the owner has
   * logged, and on a phone that is unbounded.
   */
  const DATA_LENGTH_ROWS = ['.tctl-chips', '.chips']

  it.each(DATA_LENGTH_ROWS)('%s wraps rather than overflowing', async (selector) => {
    const css = await readFile(COMPONENTS, 'utf8')
    const rule = declarations(css, selector)

    expect(rule, `${selector} must exist for this guard to mean anything`).not.toBeNull()
    expect(rule, `${selector} holds a data-length list, so it must wrap`).toMatch(
      /flex-wrap:\s*wrap/,
    )
  })

  it('.tctl-chips can shrink below its content width', async () => {
    const css = await readFile(COMPONENTS, 'utf8')
    const rule = declarations(css, '.tctl-chips')

    // flex-wrap alone is not enough: as a flex ITEM of .tctl it still gets min-width: auto, which floors it
    // at min-content and re-creates the overflow on a narrow screen.
    expect(rule, 'a flex item needs min-width: 0 to be allowed to shrink').toMatch(/min-width:\s*0/)
  })
})
