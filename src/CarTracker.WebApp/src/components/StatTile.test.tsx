import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { DUE_STATUS, type DueStatus } from '../lib/status'
import { axe } from '../test/axe'
import { expectStateIsReadable } from '../test/greyscale'
import { StatTile, StatTiles } from './StatTile'

const DUE_STATES = Object.keys(DUE_STATUS) as DueStatus[]

describe('StatTile', () => {
  it.each(DUE_STATES)('%s labels itself from the union, not a prop', (due) => {
    const result = render(<StatTile due={due} count={3} />)
    expectStateIsReadable(result, DUE_STATUS[due].label)
    expect(screen.getByText('3')).toBeInTheDocument()
  })

  it('is a plain div without a target, and a link with one', () => {
    const { container, unmount } = render(<StatTile due="Ok" count={11} />)
    expect(container.querySelector('a')).toBeNull()
    unmount()

    render(<StatTile due="Overdue" count={7} href="#g-overdue" />)
    // Queried by accessible name: the tile's name is its count plus its label, both real text.
    expect(screen.getByRole('link', { name: /Overdue/ })).toHaveAttribute('href', '#g-overdue')
  })

  it('renders never-logged neutral, not on the integrity axis', () => {
    const { container } = render(<StatTile due="NeverLogged" count={1} />)
    const tile = container.querySelector('.tile')
    expect(tile).toHaveClass('never')
    // The design used .tile.info here, borrowing blue for a due state.
    expect(tile).not.toHaveClass('info')
    expect(screen.getByText('Never logged')).toBeInTheDocument()
  })

  // The workbook's actual numbers: 18 definitions, and the Dashboard says 17 because the never-logged one
  // falls out of every bucket. The five tiles (Attention is the outcome axis, folded in) must account for all 18.
  it('can represent all five buckets so the counts sum to the real total', () => {
    render(
      <StatTiles cols={5}>
        <StatTile due="Overdue" count={7} />
        <StatTile due="DueSoon" count={3} />
        <StatTile due="Ok" count={6} />
        <StatTile due="Attention" count={1} />
        <StatTile due="NeverLogged" count={1} />
      </StatTiles>,
    )
    const counts = [7, 3, 6, 1, 1]
    expect(counts.reduce((a, b) => a + b)).toBe(18)
    for (const label of DUE_STATES.map((d) => DUE_STATUS[d].label)) {
      expect(screen.getByText(label)).toBeInTheDocument()
    }
  })

  it.each(DUE_STATES)('%s has no axe violations', async (due) => {
    const { container } = render(
      <StatTiles>
        <StatTile due={due} count={2} href="#x" />
      </StatTiles>,
    )
    expect(await axe(container)).toHaveNoViolations()
  })
})
