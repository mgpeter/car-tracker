import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LinkProvider } from '../../lib/link'
import { __resetScrollLock } from '../../lib/useScrollLock'
import { axe } from '../../test/axe'
import { QuickAddSheet } from './QuickAddSheet'

/**
 * The mobile quick-add chooser.
 *
 * It exists because the desktop band hides below 900px on the grounds that the bottom bar's + is the mobile
 * quick add - which was untrue on the dashboard, whose centre slot held an inert warning tell-tale. So the
 * property worth pinning is that this offers *everything the band offers*, from the same list, and that its
 * links carry the param that makes each one a single press.
 */

const renderSheet = (onAddFuel = vi.fn(), onClose = vi.fn()) => {
  const result = render(
    <MemoryRouter initialEntries={['/bt53akj/dashboard']}>
      <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
        <div id="root">
          <QuickAddSheet open onClose={onClose} reg="BT53 AKJ" onAddFuel={onAddFuel} />
        </div>
      </LinkProvider>
    </MemoryRouter>,
  )
  return { ...result, onAddFuel, onClose }
}

beforeEach(() => __resetScrollLock())

describe('the mobile quick-add sheet', () => {
  it('offers the same seven logs as the desktop band, in the same order', () => {
    renderSheet()

    expect(screen.getByRole('button', { name: '+ Fuel' })).toBeInTheDocument()
    expect(screen.getAllByRole('link').map((a) => a.textContent)).toEqual([
      'Service',
      'Wash',
      'Equipment',
      'Expense',
      'Mileage',
      'Log a check',
    ])
  })

  it('carries the add param, so each one is a single press', () => {
    renderSheet()

    expect(screen.getAllByRole('link').map((a) => a.getAttribute('href'))).toEqual([
      '/bt53akj/service?add=1',
      '/bt53akj/wash?add=1',
      '/bt53akj/equipment?add=1',
      '/bt53akj/expenses?add=1',
      '/bt53akj/mileage?add=1',
      '/bt53akj/checks?add=1',
    ])
  })

  /**
   * Fuel is the one that does not navigate, so the sheet has to get out of the way itself - otherwise the fill
   * sheet opens underneath this one and the scroll lock and focus trap fight over which is on top.
   */
  it('closes itself before handing over to the fuel sheet', async () => {
    const { onAddFuel, onClose } = renderSheet()
    const user = userEvent.setup()

    await user.click(screen.getByRole('button', { name: '+ Fuel' }))

    expect(onClose).toHaveBeenCalled()
    expect(onAddFuel).toHaveBeenCalled()
  })

  it('has no accessibility violations', async () => {
    const { container } = renderSheet()

    expect(await axe(container)).toHaveNoViolations()
  })
})
