import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { axe } from '../test/axe'
import { ThemeProvider } from './ThemeProvider'
import { ThemeToggle } from './ThemeToggle'

beforeEach(() => {
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => ({
      matches: false,
      media: '(prefers-color-scheme: dark)',
      addEventListener: () => {},
      removeEventListener: () => {},
    })),
  )
})

afterEach(() => vi.unstubAllGlobals())

const renderToggle = () =>
  render(
    <ThemeProvider>
      <ThemeToggle />
    </ThemeProvider>,
  )

describe('ThemeToggle', () => {
  it('presents one choice among three, not three toggles', () => {
    renderToggle()
    const group = screen.getByRole('radiogroup', { name: 'Theme' })
    expect(group).toBeInTheDocument()
    expect(screen.getAllByRole('radio')).toHaveLength(3)
    expect(screen.getByRole('radio', { name: 'System' })).toBeChecked()
  })

  it('applies and persists a choice', async () => {
    const user = userEvent.setup()
    renderToggle()

    await user.click(screen.getByRole('radio', { name: 'Dark' }))

    expect(screen.getByRole('radio', { name: 'Dark' })).toBeChecked()
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark')
    expect(localStorage.getItem('ct-theme')).toBe('dark')
  })

  it('is one tab stop, with arrows moving within it', async () => {
    const user = userEvent.setup()
    renderToggle()

    // System is selected, so it is the group's tab stop; the other two are removed from the tab order.
    expect(screen.getByRole('radio', { name: 'System' })).toHaveAttribute('tabindex', '0')
    expect(screen.getByRole('radio', { name: 'Light' })).toHaveAttribute('tabindex', '-1')

    await user.tab()
    expect(screen.getByRole('radio', { name: 'System' })).toHaveFocus()

    await user.keyboard('{ArrowRight}')
    expect(screen.getByRole('radio', { name: 'Light' })).toBeChecked()
    expect(screen.getByRole('radio', { name: 'Light' })).toHaveFocus()

    await user.keyboard('{ArrowLeft}')
    expect(screen.getByRole('radio', { name: 'System' })).toBeChecked()
  })

  it('has no axe violations', async () => {
    const { container } = renderToggle()
    expect(await axe(container)).toHaveNoViolations()
  })
})
