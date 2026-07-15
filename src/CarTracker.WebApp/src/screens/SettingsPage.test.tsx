import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../api/queries'
import { IconSprite } from '../components/IconSprite'
import { LinkProvider } from '../lib/link'
import { __resetScrollLock } from '../lib/useScrollLock'
import { ToastProvider } from '../shell/Toast'
import { axe } from '../test/axe'
import { ThemeProvider } from '../theme/ThemeProvider'
import { VehicleProvider } from '../routes'
import { SettingsPage } from './SettingsPage'

/** BT53 as it actually is right now: policies entered, no MOT record, no checks. */
const SUMMARY = {
  vehicleId: 1,
  registration: 'BT53 AKJ',
  name: 'Land Rover Freelander 1',
  asOfDate: '2026-07-15',
  mileage: { currentMileage: 76_632, asOfDate: '2026-03-14', milesSincePurchase: 0, hasNonMonotonicHistory: false, highestRecordedMileage: 76_632 },
  renewals: {
    mot: { name: 'MOT', expiryDate: null, daysRemaining: null, urgency: null, source: null },
    insurance: { name: 'Insurance', expiryDate: '2027-03-15', daysRemaining: 243, urgency: 'Ok', source: 'Admiral' },
    roadTax: { name: 'Road tax', expiryDate: '2027-02-28', daysRemaining: 228, urgency: 'Ok', source: 'VED' },
    nextServiceDate: { name: 'Next service', expiryDate: null, daysRemaining: null, urgency: null, source: null },
    nextServiceMiles: null,
  },
  spend: {
    fuelYtd: 0, serviceAndRepairsYtd: 0, statutoryYtd: 0, totalYtd: 0, totalSincePurchase: 0,
    totalSincePurchaseExcludingPurchase: 0, monthlyAverage: 0, costPerMile: null,
    costPerMileExcludingPurchase: null, ytdByCategory: {},
  },
  fuel: {
    averageMpg: null, perFillAverageMpg: null, bestMpg: null, worstMpg: null, totalLitres: 0, totalCost: 0,
    averagePricePerLitre: null, lastFillDate: null, fillCount: 0, measuredIntervalCount: 0,
    implausibleCount: 0, entries: [],
  },
  checks: { okCount: 0, dueSoonCount: 0, overdueCount: 0, neverLoggedCount: 0, totalCount: 0, checks: [] },
}

function mockApi() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL) => {
      const path = String(url)
      const body = path.endsWith('/checks') ? SUMMARY.checks : SUMMARY
      return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }),
  )
}

beforeEach(() => {
  __resetScrollLock()
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => ({ matches: false, media: '', addEventListener: () => {}, removeEventListener: () => {} })),
  )
  mockApi()
})

afterEach(() => vi.unstubAllGlobals())

const renderSettings = () =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={['/bt53akj/settings']}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <Routes>
                  <Route
                    path="/:reg/settings"
                    element={
                      <VehicleProvider>
                        <SettingsPage />
                      </VehicleProvider>
                    }
                  />
                </Routes>
              </div>
            </LinkProvider>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )

describe('settings — statutory', () => {
  it('shows the MOT as derived and read-only, with the reason', async () => {
    renderSettings()
    expect(await screen.findByText('Derived · read-only')).toBeInTheDocument()

    // The whole project in one control. A stored MOT expiry is how the spreadsheet came to show a red 23-day
    // countdown for a test that had already passed — so there is no Edit button, and no field in the API.
    const rows = document.querySelectorAll('.setrow .sk')
    const labels = [...rows].map((r) => r.textContent)
    expect(labels).not.toContain('MOT expiry')
  })

  it('offers the seed only while there is no record to derive from', async () => {
    renderSettings()
    await screen.findByText('Derived · read-only')

    // The narrow escape hatch: RenewalCalculator consults the seed ONLY when there is no MOT record, and a
    // pass record always wins. That asymmetry is what makes it safe — the failure it guards against is a
    // stored copy OVERRIDING a real record.
    expect(screen.getByText('MOT expiry · seed')).toBeInTheDocument()
    expect(screen.getByText(/a pass record always wins/)).toBeInTheDocument()
  })

  it('renders the live countdowns the PATCH made possible, in one register', async () => {
    renderSettings()
    // Both rows drive the same dashboard panel, so both lead with days left. Naming only the insurer here
    // would read, next to "228 days", as though no countdown were available.
    expect(await screen.findByText(/243 days · Admiral/)).toBeInTheDocument()
    expect(screen.getByText('228 days')).toBeInTheDocument()
  })

  it('puts the real registration on the plate, not the URL slug', async () => {
    renderSettings()
    // The route param is normalised for matching ("bt53akj"), which is right for a URL and wrong on a plate.
    expect(await screen.findByText('BT53 AKJ')).toBeInTheDocument()
    expect(screen.queryByText('BT53AKJ')).not.toBeInTheDocument()
  })
})

describe('settings — check definitions', () => {
  it('explains an empty list rather than showing an empty table', async () => {
    renderSettings()
    // BT53 predates the starter set and has none. The checks screen is empty BECAUSE this list is — saying so
    // beats a blank table that reads as a loading failure.
    expect(await screen.findByText(/No checks defined, so the checks screen has nothing to show/)).toBeInTheDocument()
  })

  it('has no axe violations, empty', async () => {
    const { container } = renderSettings()
    await screen.findByText('Derived · read-only')
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no axe violations with definitions listed', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string | URL) =>
        String(url).endsWith('/checks')
          ? // The real BT53 row, in the real shape: a CheckStatusSummary, not a bare array. Never logged, so
            // no countdown — `daysRemaining` is null rather than 0, which is the distinction the whole
            // fourth state exists to carry.
            new Response(
              JSON.stringify({
                okCount: 0,
                dueSoonCount: 0,
                overdueCount: 0,
                neverLoggedCount: 1,
                totalCount: 1,
                checks: [
                  {
                    checkDefinitionId: 1,
                    name: 'Oil filler cap underside',
                    cadenceLabel: 'Weekly',
                    intervalDays: 7,
                    lastPerformedOn: null,
                    nextDue: null,
                    daysRemaining: null,
                    status: 'NeverLogged',
                  },
                ],
              }),
              { status: 200, headers: { 'Content-Type': 'application/json' } },
            )
          : new Response(JSON.stringify(SUMMARY), { status: 200, headers: { 'Content-Type': 'application/json' } }),
      ),
    )
    const { container } = renderSettings()
    expect(await screen.findByText('Oil filler cap underside')).toBeInTheDocument()
    // The cadence renders too. The first version of this fixture said `frequency` where the wire says
    // `cadenceLabel`, so the column came out blank and the test passed regardless — a JSON literal inside a
    // Response is invisible to the compiler, which is exactly how a fixture drifts from the contract.
    expect(screen.getByText('Weekly')).toBeInTheDocument()
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no axe violations with the sheet open', async () => {
    // A sheet's contents are never swept by rendering the page — it is closed. This is the only way the
    // form inside it gets checked at all.
    const { container } = renderSettings()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add a check/i }))
    expect(await axe(container)).toHaveNoViolations()
  })
})
