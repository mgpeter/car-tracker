import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../api/queries'
import { IconSprite } from '../components/IconSprite'
import { LinkProvider } from '../lib/link'
import { __resetScrollLock } from '../lib/useScrollLock'
import { VehicleProvider } from '../routes'
import { ToastProvider } from '../shell/Toast'
import { axe } from '../test/axe'
import { ThemeProvider } from '../theme/ThemeProvider'
import { ExpensesPage } from './ExpensesPage'

const LOG = {
  rollups: {
    fuelYtd: 888.87,
    serviceAndRepairsYtd: 603.99,
    statutoryYtd: 0,
    totalYtd: 1492.86,
    totalSincePurchase: 3192.86,
    totalSincePurchaseExcludingPurchase: 1492.86,
    monthlyAverage: 373,
    costPerMile: 0.78,
    costPerMileExcludingPurchase: 0.37,
    ytdByCategory: {},
  },
  entries: [
    // A mirrored fill: the shadow of a FuelEntry, not an entry in its own right.
    { id: 1, entryDate: '2026-07-10', category: 'Fuel', subCategory: null, vendor: 'Shell Kingston V-Power', amount: 84.61, mileage: 80_712, paymentMethod: null, fuelEntryId: 7, notes: null },
    { id: 2, entryDate: '2026-05-12', category: 'Service', subCategory: 'Cambelt', vendor: 'K & P Motors', amount: 603.99, mileage: 78_800, paymentMethod: 'Card', fuelEntryId: null, notes: null },
  ],
}

/** The real seeded names. The sheet used to hardcode a guess, and eight of its twelve options were 400s. */
const CATEGORIES = [
  { name: 'Fuel', isMirrorOnly: true },
  { name: 'Service', isMirrorOnly: false },
  { name: 'Repair', isMirrorOnly: false },
  { name: 'Tax', isMirrorOnly: false },
  { name: 'Wash', isMirrorOnly: false },
  { name: 'Tools/Equipment', isMirrorOnly: false },
  { name: 'Misc', isMirrorOnly: false },
]

function mockApi(body: unknown = LOG) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL) =>
      String(url).includes('/reference/expense-categories')
        ? new Response(JSON.stringify(CATEGORIES), { status: 200, headers: { 'Content-Type': 'application/json' } })
        : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    ),
  )
}

beforeEach(() => {
  __resetScrollLock()
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, media: '', addEventListener: () => {}, removeEventListener: () => {} })))
  mockApi()
})

afterEach(() => vi.unstubAllGlobals())

const renderPage = () =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={['/bt53akj/expenses']}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <Routes>
                  <Route path="/:reg/expenses" element={<VehicleProvider><ExpensesPage /></VehicleProvider>} />
                </Routes>
              </div>
            </LinkProvider>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )

describe('the fuel mirror', () => {
  it('marks a mirrored row as coming from a fill', async () => {
    renderPage()
    await screen.findByText('£84.61')
    // §3.2's auto-mirroring is what closes the workbook's £163.16 gap — it carries one lumped "fuel to date"
    // row of £725.70 instead of per-fill entries. The mirror only holds if it cannot drift from its source,
    // so the row says where it came from and the API refuses to edit it here.
    expect(screen.getByText('From fuel')).toBeInTheDocument()
    expect(screen.getByText('1 mirrored from fills')).toBeInTheDocument()
  })

  it('offers no Fuel category, because a fill writes its own row', async () => {
    renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add expense/i }))

    // A hand-typed fuel expense IS the workbook's lumped row. The API refuses it too — this is not the only
    // thing holding the line.
    await vi.waitFor(() =>
      expect([...screen.getByLabelText(/Category/).querySelectorAll('option')].length).toBeGreaterThan(1),
    )
    const options = [...screen.getByLabelText(/Category/).querySelectorAll('option')].map((o) => o.textContent)
    expect(options).not.toContain('Fuel')
    expect(options).toContain('Service')
    expect(screen.getByText(/Fuel is absent — a fill writes its own row/)).toBeInTheDocument()
  })

  it('offers the seeded names, not a hand-typed copy of them', async () => {
    renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add expense/i }))
    await vi.waitFor(() =>
      expect([...screen.getByLabelText(/Category/).querySelectorAll('option')].length).toBeGreaterThan(1),
    )
    const options = [...screen.getByLabelText(/Category/).querySelectorAll('option')].map((o) => o.textContent)

    // The shipped bug: this list was hardcoded from the workbook's wording and the endpoint validates against
    // the seeded table, so every one of these was a 400 nobody would see until they tried to save.
    expect(options).toContain('Repair')
    expect(options).not.toContain('Repairs')
    expect(options).toContain('Tax')
    expect(options).not.toContain('Road tax')
    expect(options).toContain('Wash')
    expect(options).not.toContain('Cleaning')
    expect(options).toContain('Misc')
    expect(options).not.toContain('Other')
  })
})

describe('rollups', () => {
  it('computes them rather than storing a running total', async () => {
    renderPage()
    // The workbook's Expenses sheet carries a running-total formula down ~30 blank rows. A stored total is a
    // total that can disagree with its own rows.
    await screen.findByText('£1,492.86')
    const stats = document.querySelector('.stats') as HTMLElement
    expect(within(stats).getByText('£888.87')).toBeInTheDocument()
    expect(within(stats).getByText('£603.99')).toBeInTheDocument()
  })

  it('says what an empty log means', async () => {
    mockApi({ ...LOG, entries: [] })
    renderPage()
    expect(await screen.findByText(/Fills mirror in here automatically/)).toBeInTheDocument()
  })
})

describe('accessibility', () => {
  it('has no axe violations', async () => {
    const { container } = renderPage()
    await screen.findByText('£84.61')
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no axe violations with the sheet open', async () => {
    const { container } = renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add expense/i }))
    expect(await axe(container)).toHaveNoViolations()
  })
})
