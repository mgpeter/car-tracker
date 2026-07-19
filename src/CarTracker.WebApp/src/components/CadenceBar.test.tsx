import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { axe } from '../test/axe'
import { CadenceBar } from './CadenceBar'

describe('CadenceBar', () => {
  it('reads OK inside the window and marks today', () => {
    render(<CadenceBar sinceLast={14} min={21} max={28} />)
    expect(screen.getByText('OK')).toBeInTheDocument()
    expect(screen.getByText('Today · day 14')).toBeInTheDocument()
    expect(screen.getByRole('img', { name: /Day 14 of the 21 to 28 day wash window/ })).toBeInTheDocument()
  })

  it('reads Due soon inside the 21–28 window', () => {
    render(<CadenceBar sinceLast={24} min={21} max={28} />)
    expect(screen.getByText('Due soon')).toBeInTheDocument()
  })

  it('flips to Overdue past the window — the same rule as the stat note', () => {
    render(<CadenceBar sinceLast={40} min={21} max={28} />)
    expect(screen.getByText('Overdue')).toBeInTheDocument()
    expect(screen.getByRole('img', { name: /overdue/ })).toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    const { container } = render(<CadenceBar sinceLast={30} min={21} max={28} />)
    expect(await axe(container)).toHaveNoViolations()
  })
})
