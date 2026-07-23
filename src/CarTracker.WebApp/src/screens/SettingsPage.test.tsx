import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../api/queries'
import { IconSprite } from '../components/IconSprite'
import { LinkProvider } from '../lib/link'
import { __resetScrollLock } from '../lib/useScrollLock'
import { __resetFuelUnit } from '../lib/fuelUnit'
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
  checks: { okCount: 0, dueSoonCount: 0, overdueCount: 0, neverLoggedCount: 0, attentionCount: 0, totalCount: 0, checks: [] },
  integrity: { openCount: 0, highestSeverity: null },
}

function bodyFor(path: string): unknown {
  // The reference lists and the check-definitions editor are their own read paths; default them to empty so
  // the panels render their empty states rather than trying to .map the summary object.
  if (path.includes('/reference/')) return []
  if (path.includes('/assistant/')) return []
  if (path.endsWith('/checks/definitions')) return []
  if (path.endsWith('/checks')) return SUMMARY.checks
  return SUMMARY
}

function mockApi() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL) =>
      new Response(JSON.stringify(bodyFor(String(url))), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    ),
  )
}

beforeEach(() => {
  __resetScrollLock()
  __resetFuelUnit()
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
      vi.fn(async (url: string | URL) => {
        const path = String(url)
        // The definitions editor reads /checks/definitions — the stored definition, with its guidance, order
        // and active flag, which the status summary does not carry.
        if (path.endsWith('/checks/definitions')) {
          return new Response(
            JSON.stringify([
              { id: 1, name: 'Oil filler cap underside', cadenceLabel: 'Weekly', intervalDays: 7, guidance: 'mayo residue = possible head gasket', displayOrder: 10, isActive: true },
            ]),
            { status: 200, headers: { 'Content-Type': 'application/json' } },
          )
        }
        return new Response(JSON.stringify(bodyFor(path)), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }),
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

describe('settings — fuel tank', () => {
  // Captures the PATCH so the test can assert the fluids block it sends.
  function mockTank(capacity: number | null) {
    let posted: unknown = null
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string | URL, init?: RequestInit) => {
        if (init?.method === 'PATCH') {
          posted = JSON.parse(String(init.body))
          return new Response(JSON.stringify(SUMMARY), { status: 200, headers: { 'Content-Type': 'application/json' } })
        }
        const path = String(url)
        const body =
          path.includes('/reference/') || path.includes('/assistant/') || path.endsWith('/checks/definitions')
            ? []
            : path.endsWith('/checks')
              ? SUMMARY.checks
              : { ...SUMMARY, fluids: { fuelTankCapacityLitres: capacity } }
        return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }),
    )
    return () => posted
  }

  it('sets the tank capacity through the vehicle edit path', async () => {
    const read = mockTank(null)
    renderSettings()
    const user = userEvent.setup()

    // Unset, so the affordance invites setting it rather than editing.
    await user.click(await screen.findByRole('button', { name: /^Set$/ }))
    await user.type(screen.getByLabelText(/Capacity/), '59')
    await user.click(screen.getByRole('button', { name: /^Save$/ }))

    await vi.waitFor(() => expect(read()).not.toBeNull())
    // A fluids block with the litres — the derived range recomputes server-side from it.
    expect(read()).toEqual({ fluids: { fuelTankCapacityLitres: 59 } })
  })

  it('clears the capacity by saving it blank, so the range disappears rather than guessing', async () => {
    const read = mockTank(59)
    renderSettings()
    const user = userEvent.setup()

    // Recorded, so it reads back and offers an edit.
    await user.click(await screen.findByRole('button', { name: /^Edit$/ }))
    const input = screen.getByLabelText(/Capacity/)
    await user.clear(input)
    await user.click(screen.getByRole('button', { name: /^Save$/ }))

    await vi.waitFor(() => expect(read()).not.toBeNull())
    // Null, not omitted: the fluids block is authoritative, so a blank actively clears it.
    expect(read()).toEqual({ fluids: { fuelTankCapacityLitres: null } })
  })
})

describe('settings — appearance', () => {
  it('switches the fuel-economy unit and persists the choice', async () => {
    renderSettings()
    const user = userEvent.setup()

    const group = await screen.findByRole('radiogroup', { name: /fuel economy units/i })
    await user.click(within(group).getByRole('radio', { name: 'L/100 km' }))

    // Persisted like the theme — a reload reads it back. No server call: it is display-only.
    expect(localStorage.getItem('ct-fuel-unit')).toBe('l100')
    // The design's toast, which names the equivalence so the change reads as a display choice, not a recompute.
    expect(await screen.findByText(/28.7 MPG renders as 9.8/)).toBeInTheDocument()
  })

  it('has no axe violations with the appearance control', async () => {
    const { container } = renderSettings()
    await screen.findByRole('radiogroup', { name: /fuel economy units/i })
    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('settings — reference lists', () => {
  function mockRefs() {
    const calls: { method: string; url: string }[] = []
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string | URL, init?: RequestInit) => {
        const path = String(url)
        calls.push({ method: init?.method ?? 'GET', url: path })
        if (path.endsWith('/reference/garages')) {
          return json([
            { name: 'K & P Motors', contact: null, address: null, notes: null, referenceCount: 3 },
            { name: 'Spare Garage', contact: null, address: null, notes: null, referenceCount: 0 },
          ])
        }
        if (path.endsWith('/reference/wash-locations')) return json([])
        if (path.endsWith('/reference/expense-categories')) {
          return json([
            { name: 'Fuel', isMirrorOnly: true, isSystem: true, referenceCount: 13 },
            { name: 'Detailing', isMirrorOnly: false, isSystem: false, referenceCount: 2 },
          ])
        }
        if (path.includes('/assistant/')) return json([])
        if (path.endsWith('/checks/definitions')) return json([])
        if (path.endsWith('/checks')) return json(SUMMARY.checks)
        return json(SUMMARY)
      }),
    )
    return calls
  }
  const json = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })

  it('locks the Fuel category — no delete offered', async () => {
    mockRefs()
    renderSettings()
    // Fuel is system + mirror-only: it shows a lock, not an Edit/Delete affordance. Find it by the lock so the
    // nav's own "Fuel" link does not confuse the query.
    const fuelRow = (await screen.findByText('Locked')).closest('.setrow') as HTMLElement
    expect(within(fuelRow).getByText('Fuel')).toBeInTheDocument()
    expect(within(fuelRow).queryByRole('button', { name: /edit/i })).not.toBeInTheDocument()
  })

  it('requires a re-home target before deleting a referenced garage', async () => {
    mockRefs()
    renderSettings()
    const user = userEvent.setup()

    // K & P Motors has 3 records — opening it offers "Re-home & delete" and a picker, not a bare delete.
    const row = (await screen.findByText('K & P Motors')).closest('.setrow') as HTMLElement
    await user.click(within(row).getByRole('button', { name: /edit/i }))
    expect(await screen.findByText(/Re-home to/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Re-home & delete/i })).toBeInTheDocument()

    // Deleting without picking a target is refused client-side with the count (in the alert, distinct from the
    // field hint that also mentions the count).
    await user.click(screen.getByRole('button', { name: /Re-home & delete/i }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/3 records use this/)
  })
})
