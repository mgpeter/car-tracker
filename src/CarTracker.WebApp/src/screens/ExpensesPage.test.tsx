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

/**
 * The rollups describe exactly the two rows below, and that is new.
 *
 * They used to describe a fuller log — `fuelYtd: 888.87`, `totalSincePurchase: 3192.86` — against two entries
 * summing to £688.60. No possible world produces those together, and the reconciliation test passed anyway,
 * because it only ever compared the chart to the rows it was handed. A fixture that contradicts itself by
 * £2,504 is a guard that cannot fire.
 */
const LOG = {
  rollups: {
    fuelYtd: 84.61,
    serviceAndRepairsYtd: 603.99,
    statutoryYtd: 0,
    totalYtd: 688.6,
    totalSincePurchase: 688.6,
    totalSincePurchaseExcludingPurchase: 688.6,
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

/**
 * The rollup panel. Scoped, because the fixture is now coherent: £84.61 is both the Fuel tile and the fill
 * that produced it, and £688.60 is both the year and the since-purchase total. A bare `getByText` matched
 * only while the fixture's tiles described a log its rows did not contain.
 */
const stats = () => document.querySelector('.stats') as HTMLElement

describe('the fuel mirror', () => {
  it('marks a mirrored row as coming from a fill', async () => {
    renderPage()
    await screen.findByText('K & P Motors')
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
      expect([...within(screen.getByRole('dialog')).getByLabelText(/Category/).querySelectorAll('option')].length).toBeGreaterThan(1),
    )
    const options = [...within(screen.getByRole('dialog')).getByLabelText(/Category/).querySelectorAll('option')].map((o) => o.textContent)
    expect(options).not.toContain('Fuel')
    expect(options).toContain('Service')
    expect(screen.getByText(/Fuel is absent — a fill writes its own row/)).toBeInTheDocument()
  })

  it('offers the seeded names, not a hand-typed copy of them', async () => {
    renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add expense/i }))
    await vi.waitFor(() =>
      expect([...within(screen.getByRole('dialog')).getByLabelText(/Category/).querySelectorAll('option')].length).toBeGreaterThan(1),
    )
    const options = [...within(screen.getByRole('dialog')).getByLabelText(/Category/).querySelectorAll('option')].map((o) => o.textContent)

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
    await screen.findByText('K & P Motors')
    const stats = document.querySelector('.stats') as HTMLElement
    expect(within(stats).getByText('£84.61')).toBeInTheDocument()
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
    await screen.findByText('K & P Motors')
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no axe violations with the sheet open', async () => {
    const { container } = renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add expense/i }))
    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('filter, sort and the filtered total', () => {
  it('recomputes a filtered total from the visible rows, distinct from the YTD rollup', async () => {
    renderPage()
    const user = userEvent.setup()
    // The authoritative YTD rollup is on the page and stays put.
    await screen.findByText('K & P Motors')
    expect(within(stats()).getByText('£688.60')).toBeInTheDocument()

    // Filter to Service: the filtered total is that one row's amount, labelled as the filtered view — not YTD.
    await user.click(screen.getByRole('button', { name: 'Service' }))
    const ft = document.querySelector('.filtered-total') as HTMLElement
    expect(ft).toBeTruthy()
    expect(within(ft).getByText('£603.99')).toBeInTheDocument()
    expect(within(ft).getByText(/not the YTD figure/)).toBeInTheDocument()

    // The YTD rollup is untouched — the two figures coexist, neither mistaken for the other.
    expect(within(stats()).getByText('£688.60')).toBeInTheDocument()
    expect(document.querySelector('.tctl-count')?.textContent).toMatch(/1 of 2/)
  })

  it('shows the whole-log total silently (no filtered box) when nothing is filtered', async () => {
    renderPage()
    await screen.findByText('K & P Motors')
    // No filter active → no filtered-total box competing with the rollup.
    expect(document.querySelector('.filtered-total')).toBeNull()
  })
})

describe('search', () => {
  const count = () => document.querySelector('.tctl-count')?.textContent

  it('narrows to a vendor across categories, and the filtered total follows', async () => {
    renderPage()
    const user = userEvent.setup()
    await screen.findByText('K & P Motors')
    expect(within(stats()).getByText('£688.60')).toBeInTheDocument()

    await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'k & p')

    expect(screen.getByText('K & P Motors')).toBeInTheDocument()
    expect(screen.queryByText('Shell Kingston V-Power')).not.toBeInTheDocument()
    expect(count()).toMatch(/1 of 2/)

    // A search narrows exactly as a chip does, so it earns the same filtered-total box…
    const ft = document.querySelector('.filtered-total') as HTMLElement
    expect(within(ft).getByText('£603.99')).toBeInTheDocument()
    // …and the authoritative YTD rollup above is still untouched.
    expect(within(stats()).getByText('£688.60')).toBeInTheDocument()
  })

  it('matches case-insensitively', async () => {
    renderPage()
    const user = userEvent.setup()
    await screen.findByText('K & P Motors')

    await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'SHELL')
    expect(screen.getByText('Shell Kingston V-Power')).toBeInTheDocument()
    expect(count()).toMatch(/1 of 2/)
  })

  it('restores every row and drops the filtered box when cleared', async () => {
    renderPage()
    const user = userEvent.setup()
    await screen.findByText('K & P Motors')
    const box = screen.getByRole('searchbox', { name: 'Search' })

    await user.type(box, 'shell')
    expect(count()).toMatch(/1 of 2/)

    await user.clear(box)
    // Back to the plain total, and no filtered box competing with the rollup.
    expect(count()).toMatch(/2 rows/)
    expect(document.querySelector('.filtered-total')).toBeNull()
  })
})

describe('spend-over-time chart', () => {
  /**
   * The guard this page's own comment claims — "its final Total point equals the recorded total by
   * construction: if they could diverge, that is the drift the whole project exists to prevent".
   *
   * The previous version of this test asserted that the chart equals the sum of the rows it was handed, which
   * is a running sum restated and cannot fail. Both halves have to be in it: the chart is computed on the
   * client from `entries`, the rollup on the server from the same rows through `SpendCalculator`, and it is
   * exactly that seam that let a bill dated after today sit in the chart and in no total — £1,183 the app held
   * and did not count. Comparing the two is the only assertion that would have caught it.
   */
  it('reconciles with the server rollup, not merely with its own inputs', async () => {
    renderPage()

    const chart = await screen.findByRole('img', { name: /Cumulative spend/ })
    const total = LOG.entries.reduce((sum, e) => sum + e.amount, 0)

    expect(total).toBe(LOG.rollups.totalSincePurchase)
    expect(chart).toHaveAccessibleName(new RegExp(`reaching £${total.toFixed(2)} total`))
  })
})
