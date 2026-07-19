import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { axe } from '../test/axe'
import { CheckSelectList } from './CheckSelectList'

const CHECKS = [
  { name: 'Oil level', cadenceLabel: 'Monthly' },
  { name: 'Brake fluid', cadenceLabel: 'Monthly' },
  { name: 'Coolant colour', cadenceLabel: 'Weekly' },
]

describe('CheckSelectList', () => {
  it('counts the selectable, non-deselected rows', () => {
    render(<CheckSelectList checks={CHECKS} deselected={new Set(['Brake fluid'])} onToggle={() => {}} />)
    expect(screen.getByText('2 of 3')).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: /Brake fluid/ })).not.toBeChecked()
    expect(screen.getByRole('checkbox', { name: /Oil level/ })).toBeChecked()
  })

  it('locks already-present checks — disabled, out of the count, labelled "already added"', () => {
    render(
      <CheckSelectList checks={CHECKS} deselected={new Set()} onToggle={() => {}} locked={new Set(['Coolant colour'])} />,
    )
    // Coolant is locked: not part of the two selectable, disabled, and shows "already added" for its cadence.
    expect(screen.getByText('2 of 2')).toBeInTheDocument()
    expect(screen.getByRole('checkbox', { name: /Coolant colour/ })).toBeDisabled()
    expect(screen.getByText('already added')).toBeInTheDocument()
  })

  it('reports a toggle through the callback', async () => {
    const onToggle = vi.fn()
    const user = userEvent.setup()
    render(<CheckSelectList checks={CHECKS} deselected={new Set()} onToggle={onToggle} />)
    await user.click(screen.getByRole('checkbox', { name: /Oil level/ }))
    expect(onToggle).toHaveBeenCalledWith('Oil level')
  })

  it('has no axe violations, including a locked row', async () => {
    const { container } = render(
      <CheckSelectList
        checks={CHECKS}
        deselected={new Set(['Brake fluid'])}
        onToggle={() => {}}
        locked={new Set(['Coolant colour'])}
      />,
    )
    expect(await axe(container)).toHaveNoViolations()
  })
})
