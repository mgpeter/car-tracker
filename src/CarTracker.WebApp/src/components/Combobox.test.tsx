import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { describe, expect, it } from 'vitest'
import { axe } from '../test/axe'
import { Combobox, type Suggestion } from './Combobox'

const SUGGESTIONS: Suggestion[] = [
  { value: 'Shell Kingston', hint: '5×' },
  { value: 'Esso Hampton Court', hint: '3×' },
  { value: 'BP Surbiton', hint: '1×' },
]

/** A controlled host — the real usage keeps the value in the sheet's state. */
function Host({ suggestions = SUGGESTIONS, initial = '' }: { suggestions?: Suggestion[]; initial?: string }) {
  const [value, setValue] = useState(initial)
  return (
    <label>
      Station
      <Combobox value={value} onChange={setValue} suggestions={suggestions} />
    </label>
  )
}

describe('Combobox', () => {
  it('opens the recent list on focus', async () => {
    const user = userEvent.setup()
    render(<Host />)
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()

    await user.click(screen.getByRole('combobox'))
    const list = screen.getByRole('listbox')
    expect(within(list).getAllByRole('option')).toHaveLength(3)
    expect(within(list).getByText('Shell Kingston')).toBeInTheDocument()
  })

  it('filters as you type, case-insensitively', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('combobox'))
    await user.keyboard('esso')

    const options = screen.getAllByRole('option')
    expect(options).toHaveLength(1)
    expect(options[0]).toHaveTextContent('Esso Hampton Court')
  })

  it('selects a suggestion on click', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('combobox'))
    await user.click(screen.getByRole('option', { name: /BP Surbiton/ }))

    expect(screen.getByRole('combobox')).toHaveValue('BP Surbiton')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('selects with the keyboard (ArrowDown + Enter)', async () => {
    const user = userEvent.setup()
    render(<Host />)
    const input = screen.getByRole('combobox')
    await user.click(input)
    await user.keyboard('{ArrowDown}{ArrowDown}{Enter}') // first Arrow highlights index 0, second index 1

    expect(input).toHaveValue('Esso Hampton Court')
  })

  it('accepts a brand-new value that is not in the list', async () => {
    const user = userEvent.setup()
    render(<Host />)
    const input = screen.getByRole('combobox')
    await user.click(input)
    await user.keyboard('Tesco Extra')

    expect(input).toHaveValue('Tesco Extra')
    // Nothing matches, so no list is forced on the user — free text stands.
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('closes on Escape', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('combobox'))
    expect(screen.getByRole('listbox')).toBeInTheDocument()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument()
  })

  it('has no axe violations when open', async () => {
    const user = userEvent.setup()
    const { container } = render(<Host />)
    await user.click(screen.getByRole('combobox'))
    expect(await axe(container)).toHaveNoViolations()
  })
})
