import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { axe } from '../test/axe'
import { TimeChart, type ChartSeries } from './TimeChart'

const line: ChartSeries = {
  id: 'mpg',
  label: 'MPG',
  points: [
    { date: '2026-05-13', value: 32.2 },
    { date: '2026-06-10', value: 28.1 },
    { date: '2026-07-10', value: 25.4 },
  ],
}

describe('TimeChart', () => {
  it('is an img with the derived accessible name — the chart, for a screen reader', () => {
    render(<TimeChart series={[line]} unit="MPG" label="Fuel economy across 3 intervals, ranging 25.4 to 32.2 MPG. Latest 25.4." />)
    const chart = screen.getByRole('img')
    expect(chart).toHaveAccessibleName(/ranging 25.4 to 32.2 MPG/)
    expect(chart).toHaveAccessibleName(/Latest 25.4/)
  })

  it('draws the value range and the date range on the axes', () => {
    const { container } = render(<TimeChart series={[line]} unit="MPG" label="x" />)
    const text = [...container.querySelectorAll('.tc-axis')].map((t) => t.textContent)
    expect(text).toContain('32.2') // hi
    expect(text).toContain('25.4') // lo
    expect(text.some((t) => t?.includes('May'))).toBe(true)
    expect(text.some((t) => t?.includes('Jul'))).toBe(true)
  })

  it('names each series directly, not by colour, when there is more than one', () => {
    const { container } = render(
      <TimeChart
        series={[line, { id: 'svc', label: 'Service', points: [{ date: '2026-06-01', value: 10 }, { date: '2026-07-01', value: 20 }] }]}
        unit="£"
        label="two series"
      />,
    )
    const labels = [...container.querySelectorAll('.tc-serieslabel')].map((t) => t.textContent)
    expect(labels).toContain('MPG')
    expect(labels).toContain('Service')
  })

  it('gives a lone series the Spark treatment — area fill and good/bad extreme markers', () => {
    // line: max 32.2, min 25.4. good="higher" → max is good (green), min is bad (rust).
    const { container } = render(<TimeChart series={[line]} unit="MPG" label="x" good="higher" />)
    expect(container.querySelector('.tc-area')).not.toBeNull() // the green area fade
    expect(container.querySelector('.tc-line.solo')).not.toBeNull() // green line, no dash
    expect(container.querySelector('.tc-dot-good')).not.toBeNull() // the good extreme
    expect(container.querySelector('.tc-dot-bad')).not.toBeNull() // the bad extreme
    expect(container.querySelector('.tc-dot-last')).toBeNull() // not the neutral fallback
  })

  it('flips which extreme is good when lower is better', () => {
    // With good="lower", the min value carries the good (green) marker and the max the bad (rust) one — the
    // marker sits on the opposite point from the higher-is-better case, at the same coordinates.
    const { container } = render(<TimeChart series={[line]} unit="£/L" label="x" good="lower" />)
    const good = container.querySelector<SVGCircleElement>('.tc-dot-good')!
    const bad = container.querySelector<SVGCircleElement>('.tc-dot-bad')!
    // min (25.4) is the lowest, so it sits nearer the bottom (larger y) — and it's the good one here.
    expect(Number(good.getAttribute('cy'))).toBeGreaterThan(Number(bad.getAttribute('cy')))
  })

  it('falls back to a neutral latest marker when no good/bad direction is given', () => {
    const { container } = render(<TimeChart series={[line]} unit="MPG" label="x" />)
    expect(container.querySelector('.tc-dot-last')).not.toBeNull()
    expect(container.querySelector('.tc-dot-good')).toBeNull()
    expect(container.querySelector('.tc-dot-bad')).toBeNull()
  })

  it('does not fill or green a multi-series chart — it stays sand, dashed and labelled', () => {
    const { container } = render(
      <TimeChart
        series={[line, { id: 'svc', label: 'Service', points: [{ date: '2026-06-01', value: 10 }, { date: '2026-07-01', value: 20 }] }]}
        unit="£"
        label="two series"
      />,
    )
    expect(container.querySelector('.tc-area')).toBeNull()
    expect(container.querySelector('.tc-line.solo')).toBeNull()
    expect(container.querySelectorAll('.tc-line').length).toBe(2) // both series drawn sand
  })

  it('shows a real empty state, not a blank box', () => {
    render(<TimeChart series={[{ id: 'x', label: 'x', points: [] }]} unit="MPG" label="empty" emptyMessage="Needs two fills." />)
    expect(screen.getByText('Needs two fills.')).toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    const { container } = render(<TimeChart series={[line]} unit="MPG" label="Fuel economy across 3 intervals." />)
    expect(await axe(container)).toHaveNoViolations()
  })
})
