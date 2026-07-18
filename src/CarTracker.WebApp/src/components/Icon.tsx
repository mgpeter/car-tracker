export const ICON_NAMES = [
  'arrow-right',
  'plus',
  'check',
  'caret-down',
  'home',
  'mirror',
  'grip',
  'gear',
  'warning',
] as const

export type IconName = (typeof ICON_NAMES)[number]

interface IconProps {
  name: IconName
  /**
   * The accessible name.
   *
   * Omit it and the icon is `aria-hidden` — correct for the overwhelming majority, where adjacent text
   * already names the thing (`＋ Fuel`, `More ▾`, `⌂ Garage`, `⇄ mirror`) or the control carries its own
   * `aria-label` (every FAB in the design does — it is unusually disciplined about this, and the port must
   * not regress it).
   *
   * Pass it only where the glyph carries information no adjacent text repeats. There are exactly three such
   * sites, and a blind `aria-hidden` sweep would silently destroy each:
   *   - `<s>83,000 mi</s> → 80,300 suggested` — the arrow is the ONLY thing expressing "was → becomes".
   *     Without it a screen reader hears two numbers and no relation. `label="changed to"`.
   *   - Settings' Active column — `✓` is the entire value of the cell and there is no `✗` counterpart, so
   *     nothing distinguishes true from absent. `label="Active"`.
   *   - `sorted · date ↓` — but that one stays text, being prose rather than an icon.
   */
  label?: string
  className?: string
}

/**
 * Renders one symbol from `<IconSprite>`, which must be mounted once at the app root.
 *
 * Same-document `<use>`, so there is no fetch and nothing for the CSP to refuse. Colour comes from
 * `currentColor` — an icon never picks its own, which is what keeps it out of `tokens.test.ts`'s way and
 * lets a status tone flow through it.
 */
export function Icon({ name, label, className }: IconProps) {
  const decorative = label === undefined

  return (
    <svg
      className={className === undefined ? 'icon' : `icon ${className}`}
      // Decorative icons are hidden outright; a labelled one is an image with a name.
      {...(decorative ? { 'aria-hidden': true } : { role: 'img', 'aria-label': label })}
      focusable="false"
    >
      <use href={`#ct-${name}`} />
    </svg>
  )
}
