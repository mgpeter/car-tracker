import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { axe } from '../test/axe'
import { Prose } from './Prose'

describe('Prose', () => {
  it('renders emphasis as emphasis rather than as asterisks', () => {
    const { container } = render(<Prose>{'**BT53 AKJ** — bought *14 Mar 2026*.'}</Prose>)

    expect(container.querySelector('strong')).toHaveTextContent('BT53 AKJ')
    expect(container.querySelector('em')).toHaveTextContent('14 Mar 2026')
    expect(container.textContent).not.toContain('**')
  })

  it('renders a list as a list', () => {
    render(<Prose>{'- No service history\n- No fuel fills\n- 15 checks never done'}</Prose>)

    expect(screen.getAllByRole('listitem')).toHaveLength(3)
  })

  it('renders a table, in a box that can scroll', () => {
    // GFM, and the reason it is installed: the assistant answers "list my fills" with a pipe table.
    const { container } = render(
      <Prose>{'| Date | Mileage |\n| --- | --- |\n| 18 Apr | 77,537 |\n| 21 Apr | 78,079 |'}</Prose>,
    )

    expect(screen.getAllByRole('row')).toHaveLength(3)
    expect(screen.getByRole('columnheader', { name: 'Mileage' })).toBeInTheDocument()

    // The panel must never scroll sideways; the table does its scrolling inside its own container.
    expect(container.querySelector('.pr-tablebox')).toBeInTheDocument()
  })

  it('does not render HTML found in the text', () => {
    // No rehype-raw, deliberately: this is model output, and the app injects no HTML from data anywhere.
    const { container } = render(<Prose>{'<button onclick="alert(1)">press</button> and `code`'}</Prose>)

    expect(container.querySelector('button')).toBeNull()
    expect(container.querySelector('.pr-code')).toHaveTextContent('code')
  })

  it('renders an image as its description rather than a broken one', () => {
    // img-src blocks a remote one in production anyway, so the words the model used are the better outcome.
    const { container } = render(<Prose>{'![a receipt](https://example.test/receipt.png)'}</Prose>)

    expect(container.querySelector('img')).toBeNull()
    expect(container.textContent).toContain('a receipt')
  })

  it('opens a link away from the app, and vouches for nothing', () => {
    render(<Prose>{'[the DVLA](https://www.gov.uk/check-mot-history)'}</Prose>)

    const link = screen.getByRole('link', { name: 'the DVLA' })
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', 'noopener noreferrer nofollow')
  })

  it('renders half-typed emphasis as it stands, mid-stream', () => {
    // The panel re-parses every frame. An unclosed marker shows for an instant and then resolves, which is
    // self-healing; buffering until the turn ends would throw the streaming away.
    render(<Prose>{'Your MOT runs out on **8 July'}</Prose>)

    expect(screen.getByText(/\*\*8 July/)).toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    const { container } = render(
      <Prose>{'## Fills\n\n- 18 Apr\n- 21 Apr\n\n| Date | Litres |\n| --- | --- |\n| 18 Apr | 62.00 |'}</Prose>,
    )

    expect(await axe(container)).toHaveNoViolations()
  })
})
