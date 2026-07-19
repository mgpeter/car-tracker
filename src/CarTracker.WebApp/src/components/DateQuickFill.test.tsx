import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { addMonths, todayIso } from '../lib/date'
import { axe } from '../test/axe'
import { DateQuickFill } from './DateQuickFill'

describe('DateQuickFill', () => {
  it('offers +6 months and +1 year from the base date', async () => {
    const user = userEvent.setup()
    const onPick = vi.fn()
    render(<DateQuickFill base="2026-07-19" onPick={onPick} />)

    await user.click(screen.getByRole('button', { name: '+6 months' }))
    expect(onPick).toHaveBeenLastCalledWith(addMonths('2026-07-19', 6)) // 2027-01-19

    await user.click(screen.getByRole('button', { name: '+1 year' }))
    expect(onPick).toHaveBeenLastCalledWith(addMonths('2026-07-19', 12)) // 2027-07-19
  })

  it('measures from today when no base is given', async () => {
    const user = userEvent.setup()
    const onPick = vi.fn()
    render(<DateQuickFill onPick={onPick} />)
    await user.click(screen.getByRole('button', { name: '+6 months' }))
    expect(onPick).toHaveBeenLastCalledWith(addMonths(todayIso(), 6))
  })

  it('has no axe violations', async () => {
    const { container } = render(<DateQuickFill base="2026-07-19" onPick={() => {}} />)
    expect(await axe(container)).toHaveNoViolations()
  })
})
