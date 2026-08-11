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
   * Every flex row whose content can exceed a phone's width, so it must be allowed to break.
   *
   * Most are data-length — one item per category, status or kind, so the width is set by how much the owner
   * has logged rather than by the design. `.lp-cta` is the exception and is here anyway: two pill buttons is
   * a fixed count, but it is still a nowrap row of wide items, which is the exact shape that widened the
   * document twice, and it is the first thing a signed-out visitor meets on a phone.
   */
  const MUST_WRAP = ['.tctl-chips', '.chips', '.lp-cta']

  /**
   * Rows that must not floor at min-content. `.tctl-search` grows to take the strip's slack, so it is the
   * widest item on its row and the likeliest to widen the page if it cannot shrink back.
   */
  const MUST_SHRINK = ['.tctl-search', '.lp-cta']

  it.each(MUST_WRAP)('%s wraps rather than overflowing', async (selector) => {
    const css = await readFile(COMPONENTS, 'utf8')
    const rule = declarations(css, selector)

    expect(rule, `${selector} must exist for this guard to mean anything`).not.toBeNull()
    expect(rule, `${selector} can outgrow a phone, so it must wrap`).toMatch(/flex-wrap:\s*wrap/)
  })

  it.each([...new Set([...MUST_WRAP.filter((s) => s.startsWith('.tctl')), ...MUST_SHRINK])])(
    '%s can shrink below its content width',
    async (selector) => {
      const css = await readFile(COMPONENTS, 'utf8')
      const rule = declarations(css, selector)

      // For a wrapping row, flex-wrap alone is not enough: as a flex ITEM of .tctl it still gets
      // min-width: auto, which floors it at min-content and re-creates the overflow on a narrow screen.
      expect(rule, 'a flex item needs min-width: 0 to be allowed to shrink').toMatch(/min-width:\s*0/)
    },
  )
})

/**
 * The same class of invisible failure, one step further: a control the suite can find but nobody can see.
 *
 * Tailwind's preflight sets `border: 0 solid` on every element and clears the background on form controls,
 * and this codebase has no global `input`/`select` rule — every visible field draws its own box. The search
 * input shipped (2026-08-09) with only font, size and width declared, so it rendered as a lone SEARCH label
 * with nothing beside it, and the field appeared only once `:focus-visible` painted a ring around it. Every
 * test stayed green throughout: they address it by role, and jsdom applies no stylesheet at all.
 */
describe('the table search box draws its own field', () => {
  it.each(['background', 'border'])('declares a %s — preflight gives it none', async (property) => {
    const css = await readFile(COMPONENTS, 'utf8')
    const rule = declarations(css, '.tctl-search input')

    expect(rule, '.tctl-search input must exist for this guard to mean anything').not.toBeNull()
    expect(rule, `without a ${property} the search box is invisible until focused`).toMatch(
      new RegExp(`${property}:\\s*\\S`),
    )
  })
})
