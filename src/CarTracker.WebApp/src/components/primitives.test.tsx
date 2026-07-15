import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { axe } from '../test/axe'
import { Btn, Mark } from './Btn'
import { Contours, type ContourVariant } from './Contours'
import { Kv, Stats } from './Kv'
import { CFoot, Panel, RuleMark, Section, SectionHead, Wrap } from './layout'
import { RegPlate } from './RegPlate'

describe('Btn / Mark — the element follows what the thing does', () => {
  it('renders a button for an action', async () => {
    const onClick = vi.fn()
    render(<Btn onClick={onClick}>Save wash</Btn>)
    const el = screen.getByRole('button', { name: 'Save wash' })
    await userEvent.click(el)
    expect(onClick).toHaveBeenCalledOnce()
    expect(el).toHaveAttribute('type', 'button')
  })

  it('renders an anchor for a destination', () => {
    render(<Btn href="/bt53akj/fuel">Fuel log</Btn>)
    // A link, so middle-click and "open in new tab" work — which they would not if this were a button.
    expect(screen.getByRole('link', { name: 'Fuel log' })).toHaveAttribute('href', '/bt53akj/fuel')
  })

  it('can be a submit button, which the design has no way to express', () => {
    // The design has zero <form> elements, so every control is type="button" and Enter does nothing.
    render(<Btn onClick={() => {}} type="submit">Save</Btn>)
    expect(screen.getByRole('button', { name: 'Save' })).toHaveAttribute('type', 'submit')
  })

  it('rejects being both a link and an action', () => {
    const rejected = (
      // @ts-expect-error — href and onClick are mutually exclusive: a thing either goes somewhere or does something.
      <Btn href="/x" onClick={() => {}}>
        Both
      </Btn>
    )
    expect(rejected).toBeDefined()
  })

  it('ghost is a variant, not a different component', () => {
    render(<Btn variant="ghost" onClick={() => {}}>Dismiss</Btn>)
    expect(screen.getByRole('button', { name: 'Dismiss' })).toHaveClass('btn', 'ghost')
  })

  it('Mark carries an accessible name from its text', async () => {
    render(<Mark href="/x">Edit fill</Mark>)
    expect(screen.getByRole('link', { name: 'Edit fill' })).toBeInTheDocument()
    expect(await axe(render(<Mark onClick={() => {}}>Mark done today</Mark>).container)).toHaveNoViolations()
  })
})

describe('RegPlate', () => {
  it('renders the registration as real text', () => {
    render(<RegPlate reg="BT53 AKJ" />)
    // Text, not an image: selectable, searchable, announced.
    expect(screen.getByText(/BT53/)).toBeInTheDocument()
  })

  it('keeps the registration on one line', () => {
    const { container } = render(<RegPlate reg="BT53 AKJ" />)
    // A non-breaking space: "BT53" and "AKJ" must never wrap apart.
    expect(container.querySelector('.reg')?.textContent).toBe('BT53 AKJ')
  })

  it('sizes via a prop — which is all the fork drift ever was', () => {
    const { container, unmount } = render(<RegPlate reg="BT53 AKJ" />)
    expect(container.querySelector('.plate')).toHaveClass('sm')
    unmount()
    const { container: lg } = render(<RegPlate reg="BT53 AKJ" size="lg" />)
    expect(lg.querySelector('.plate')).toHaveClass('lg')
  })
})

describe('Contours', () => {
  const VARIANTS: ContourVariant[] = ['phead', 'hero', 'dossier', 'card']

  it.each(VARIANTS)('%s is decorative and hidden from the a11y tree', (variant) => {
    const { container } = render(<Contours variant={variant} />)
    expect(container.querySelector('svg')).toHaveAttribute('aria-hidden', 'true')
  })

  // The finding this component exists for: three of the four variants are slices of ONE path table, so they
  // were never four drawings. If someone edits a curve, all three must move together.
  it('slices one table — dossier is the superset, hero the first four, phead the last three', () => {
    const paths = (v: ContourVariant) =>
      [...render(<Contours variant={v} />).container.querySelectorAll('path')].map((p) => p.getAttribute('d'))

    const dossier = paths('dossier')
    const hero = paths('hero')
    const phead = paths('phead')

    expect(dossier).toHaveLength(5)
    expect(hero).toEqual(dossier.slice(0, 4))
    expect(phead).toEqual(dossier.slice(2))
  })

  it('gives the card its own drawing, which is not a slice', () => {
    const paths = (v: ContourVariant) =>
      [...render(<Contours variant={v} />).container.querySelectorAll('path')].map((p) => p.getAttribute('d'))
    const card = paths('card')
    expect(card).toHaveLength(2)
    expect(card[0]).not.toEqual(paths('dossier')[0])
  })

  it('takes its stroke from the token, not a hardcoded sand', () => {
    const { container } = render(<Contours variant="phead" />)
    // The design hardcodes #C9B588 in all 19 instances — that is --sand's value.
    expect(container.querySelector('g')).toHaveAttribute('stroke', 'var(--sand)')
  })

  it('crops the shorter header to the upper curves', () => {
    const vb = (v: ContourVariant) =>
      render(<Contours variant={v} />).container.querySelector('svg')?.getAttribute('viewBox')
    expect(vb('phead')).toBe('0 0 1200 200')
    expect(vb('dossier')).toBe('0 0 1200 300')
  })
})

describe('layout primitives', () => {
  it('SectionHead names the section with a heading', () => {
    render(<SectionHead title="Regular checks" />)
    expect(screen.getByRole('heading', { name: 'Regular checks', level: 2 })).toBeInTheDocument()
  })

  it('SectionHead takes an optional rule and link', () => {
    render(
      <SectionHead
        title="Fuel"
        rule={<>13 fills · <RuleMark>556.47 L</RuleMark></>}
        link={<a className="sec-link" href="/x">All fills</a>}
      />,
    )
    expect(screen.getByText('556.47 L')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'All fills' })).toBeInTheDocument()
  })

  it('Kv renders label, value and note', () => {
    render(<Kv label="Average MPG" value="28.7" note="12 measurable intervals" />)
    expect(screen.getByText('Average MPG')).toBeInTheDocument()
    expect(screen.getByText('28.7')).toBeInTheDocument()
    expect(screen.getByText('12 measurable intervals')).toBeInTheDocument()
  })

  // A null derived figure must say so out loud. Kv takes a node precisely so it can.
  it('Kv can say a figure is unavailable rather than render blank', () => {
    render(<Kv label="MPG" value={<span>No previous fill</span>} />)
    expect(screen.getByText('No previous fill')).toBeInTheDocument()
  })

  it('Stats sets its column count', () => {
    const { container, unmount } = render(<Stats columns={4}><Kv label="A" value="1" /></Stats>)
    expect(container.querySelector('.stats')).toHaveClass('four')
    unmount()
    const { container: six } = render(<Stats><Kv label="A" value="1" /></Stats>)
    expect(six.querySelector('.stats')).not.toHaveClass('four', 'five')
  })

  it('composes without axe violations', async () => {
    const { container } = render(
      <Wrap>
        <Section last>
          <SectionHead title="Fuel" rule={<>13 fills</>} />
          <Panel>
            <Stats columns={4}>
              <Kv label="Average MPG" value="28.7" />
            </Stats>
            <CFoot>
              <span>
                Computed on read. <b>Never stored.</b>
              </span>
            </CFoot>
          </Panel>
        </Section>
      </Wrap>,
    )
    expect(await axe(container)).toHaveNoViolations()
  })
})
