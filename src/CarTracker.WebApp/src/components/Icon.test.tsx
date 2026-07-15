import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { axe } from '../test/axe'
import { Icon, ICON_NAMES, type IconName } from './Icon'
import { IconSprite } from './IconSprite'

const renderWithSprite = (ui: React.ReactNode) =>
  render(
    <>
      <IconSprite />
      {ui}
    </>,
  )

describe('IconSprite', () => {
  // The drift guard. ICON_NAMES is the type source, the sprite is the render source, and nothing else ties
  // them together — so a name without a symbol would type-check perfectly and render an empty box.
  it('defines a symbol for every name in ICON_NAMES', () => {
    const { container } = render(<IconSprite />)
    const ids = [...container.querySelectorAll('symbol')].map((s) => s.id)
    expect(ids.sort()).toEqual(ICON_NAMES.map((n) => `ct-${n}`).sort())
  })

  it('ships no symbol that no name refers to', () => {
    const { container } = render(<IconSprite />)
    const ids = [...container.querySelectorAll('symbol')].map((s) => s.id.replace(/^ct-/, ''))
    for (const id of ids) expect(ICON_NAMES).toContain(id as IconName)
  })

  // Colour must flow from the consumer. An icon that picks its own would sit outside the token layer, and a
  // status tone could not tint it.
  it('picks no colour of its own', () => {
    const { container } = render(<IconSprite />)
    const html = container.innerHTML
    expect(html).not.toMatch(/#[0-9a-f]{3,8}/i)
    expect(html).not.toMatch(/%23/)
    // Every symbol paints with currentColor, via stroke or fill.
    for (const symbol of container.querySelectorAll('symbol')) {
      const paints = `${symbol.getAttribute('stroke') ?? ''} ${symbol.getAttribute('fill') ?? ''}`
      expect(paints, `${symbol.id} must paint with currentColor`).toContain('currentColor')
    }
  })

  it('is hidden from the accessibility tree', () => {
    const { container } = render(<IconSprite />)
    expect(container.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
  })
})

describe('Icon', () => {
  it('is decorative by default', () => {
    const { container } = renderWithSprite(<Icon name="plus" />)
    const icon = container.querySelector('svg.icon')
    expect(icon).toHaveAttribute('aria-hidden', 'true')
    expect(icon).not.toHaveAttribute('role', 'img')
  })

  it('becomes an image with a name when labelled', () => {
    renderWithSprite(<Icon name="arrow-right" label="changed to" />)
    expect(screen.getByRole('img', { name: 'changed to' })).toBeInTheDocument()
  })

  it('references the sprite symbol', () => {
    const { container } = renderWithSprite(<Icon name="grip" />)
    expect(container.querySelector('use')).toHaveAttribute('href', '#ct-grip')
  })

  it.each(ICON_NAMES)('renders %s without axe violations', async (name) => {
    const { container } = renderWithSprite(<Icon name={name} label={`${name} label`} />)
    expect(await axe(container)).toHaveNoViolations()
  })

  // The pattern the design already gets right: the button carries the name, the glyph is decorative. This is
  // the shape the FABs use, and porting it wrong would make 13 unlabelled controls.
  it('leaves the accessible name to the control when decorative', async () => {
    const { container } = renderWithSprite(
      <button type="button" aria-label="Add fuel">
        <Icon name="plus" />
      </button>,
    )
    expect(screen.getByRole('button', { name: 'Add fuel' })).toBeInTheDocument()
    expect(await axe(container)).toHaveNoViolations()
  })

  it('does not double-announce when adjacent text already names the thing', () => {
    renderWithSprite(
      <button type="button">
        <Icon name="plus" /> Fuel
      </button>,
    )
    // "＋ Fuel" must announce as "Fuel", not "plus Fuel".
    expect(screen.getByRole('button', { name: 'Fuel' })).toBeInTheDocument()
  })
})
