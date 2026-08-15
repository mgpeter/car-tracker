import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
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
import { VehicleInfoPage } from './VehicleInfoPage'
import { EDITORS } from './vehicle/VehicleEditSheet'

/**
 * The vehicle screen: the reference card and every editor that used to live on Settings.
 *
 * It runs **two** queries, and the fixture has to serve both. `vedExpiry` and the MOT seed are not on the
 * vehicle detail at all - they come back on the derived summary's renewals - which is exactly why the old
 * Settings screen fetched the summary and the old read-only card did not.
 */

const VEHICLE = {
  registration: 'BT53 AKJ',
  name: 'Land Rover Freelander',
  variant: '1.8 SE Station Wagon',
  year: 2003,
  colour: 'Navy Blue',
  bodyStyle: 'Station Wagon',
  vin: null,
  engineCode: 'K-series',
  engineSizeCc: 1796,
  fuelType: 'Petrol',
  transmission: 'Manual 5-spd',
  drivetrain: 'AWD · VCU',
  purchaseDate: '2026-03-14',
  purchasePrice: 1700,
  purchaseMileage: 76_632,
  seller: null,
  defaultGarage: 'K & P Motors',
  ulezCompliant: true,
  vedAnnualCost: 430,
  fluids: { oilSpec: '10W-40 semi-synthetic', oilCapacityLitres: 4.5, coolantSpec: 'OAT red/pink', coolantCapacityLitres: 7, fuelTankCapacityLitres: null, brakeFluidSpec: null, transmissionOilSpec: null, sparkPlugPart: null, oilFilterPart: null, airFilterPart: null, fuelFilterPart: null, cabinFilterPart: null },
  tyres: { tyreSize: '195/80 R15', pressureFrontPsi: 30, pressureRearPsi: 35, pressureFrontLadenPsi: null, pressureRearLadenPsi: null, minTreadMm: 3 },
  insurance: { insurer: 'Admiral', policyNumber: 'P77904683', periodStart: null, periodEnd: '2027-03-15', coverType: 'Comprehensive', premium: 517.14, excessCompulsory: 250, excessVoluntary: null, ncbYears: 0 },
  breakdown: { provider: null, policyNumber: null, expiry: null },
  notes: null,
}

/** BT53 as it is: policies entered, no MOT record, no checks defined. */
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

const json = (body: unknown) =>
  new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })

/** Returns a reader for whatever the page PATCHed, so a test can assert the block it sent. */
function mockApi(detail: unknown = VEHICLE) {
  let posted: unknown = null
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL, init?: RequestInit) => {
      if (init?.method === 'PATCH') {
        posted = JSON.parse(String(init.body))
        return json(SUMMARY)
      }
      const path = String(url)
      if (path.includes('/reference/')) return json([])
      if (path.endsWith('/checks/definitions')) return json([])
      if (path.endsWith('/summary')) return json(SUMMARY)
      return json(detail)
    }),
  )
  return () => posted
}

beforeEach(() => {
  __resetScrollLock()
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, media: '', addEventListener: () => {}, removeEventListener: () => {} })))
})

afterEach(() => vi.unstubAllGlobals())

const renderPage = () =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={['/bt53akj/vehicle-info']}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <Routes>
                  <Route
                    path="/:reg/vehicle-info"
                    element={
                      <VehicleProvider>
                        <VehicleInfoPage />
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

describe('the vehicle screen - what it says', () => {
  it('is explicit that its dates are inputs, not countdowns', async () => {
    mockApi()
    renderPage()
    expect(await screen.findByText(/the countdowns are on the dashboard/)).toBeInTheDocument()
  })

  it('carries the coolant rule the head gasket depends on', async () => {
    mockApi()
    renderPage()
    // The K-series frailty is why this field is worth a screen: OAT only, never mixed with IAT.
    expect(await screen.findByText(/OAT only, never mixed with IAT/)).toBeInTheDocument()
  })

  it('drops a spec row it has nothing for', async () => {
    mockApi()
    renderPage()
    await screen.findByText('Engine oil')
    // An empty spec row implies the manual said nothing. Absent is the honest rendering - except where the
    // absence is the thing you came to fix, which is why the fuel tank and the policies still render.
    expect(screen.queryByText('Brake fluid')).not.toBeInTheDocument()
    expect(screen.getByText('Fuel tank')).toBeInTheDocument()
  })

  it('puts the real registration on the plate, not the URL slug', async () => {
    mockApi()
    renderPage()
    // The route param is normalised for matching ("bt53akj"), which is right for a URL and wrong on a plate.
    // Two nodes carry it since the merge - the page head's plate and the Identity row - so this asserts the
    // slug never renders rather than counting the honest ones.
    expect((await screen.findAllByText('BT53 AKJ')).length).toBeGreaterThan(0)
    expect(screen.queryByText('BT53AKJ')).not.toBeInTheDocument()
  })

  it('gives the whole-vehicle note its own section, not a row under Policies', async () => {
    mockApi()
    renderPage()
    // `Vehicle.Notes` is a root field, sibling to the insurance block rather than a member of it. It rendered
    // as the last row of a panel whose other seven rows were all insurance, breakdown or VED.
    const heading = await screen.findByRole('heading', { name: 'Notes' })
    expect(heading).toBeInTheDocument()
    const policies = screen.getByRole('heading', { name: 'Statutory & policies' }).closest('section')
    expect(policies).not.toBeNull()
    expect(policies?.textContent).not.toMatch(/anything about this car that is not a field/)
  })
})

describe('the vehicle screen - statutory', () => {
  it('shows the MOT as derived and read-only, with the reason', async () => {
    mockApi()
    renderPage()
    expect(await screen.findByText('Derived · read-only')).toBeInTheDocument()

    // The whole project in one control. A stored MOT expiry is how the spreadsheet came to show a red 23-day
    // countdown for a test that had already passed - so there is no Edit button, and no field in the API.
    expect(screen.queryByRole('button', { name: /Edit the MOT expiry$/ })).not.toBeInTheDocument()
  })

  it('offers the seed only while there is no record to derive from', async () => {
    mockApi()
    renderPage()
    await screen.findByText('Derived · read-only')

    // The narrow escape hatch: RenewalCalculator consults the seed ONLY when there is no MOT record, and a
    // pass record always wins.
    expect(screen.getByText('MOT expiry · seed')).toBeInTheDocument()
    expect(screen.getByText(/a pass record always wins/)).toBeInTheDocument()
  })

  it('renders the live countdowns the PATCH made possible, in one register', async () => {
    mockApi()
    renderPage()
    // Both rows drive the same dashboard panel, so both lead with days left.
    expect(await screen.findByText(/243 days · Admiral/)).toBeInTheDocument()
    expect(screen.getByText(/228 days/)).toBeInTheDocument()
  })

  it('fuses the stored VED cost into the row carrying its derived expiry', async () => {
    mockApi()
    renderPage()
    // Two rows said "Road tax" before the merge: the derived expiry on Settings and the stored annual cost on
    // the reference card. One row, one subject.
    expect(await screen.findByText(/228 days · £430\/yr/)).toBeInTheDocument()
  })
})

describe('the vehicle screen - the editors', () => {
  it('gives every edit control a distinct accessible name', async () => {
    mockApi()
    renderPage()
    await screen.findByText('Engine oil')

    // Eight buttons reading only "Edit" is eight identical announcements. The visible word stays "Edit" -
    // the row's label gives it context on screen - so the distinction lives in the accessible name.
    const names = screen
      .getAllByRole('button')
      .map((b) => b.getAttribute('aria-label') ?? b.textContent ?? '')
      .filter((n) => /^(Edit|Set|Seed|Add)\b/.test(n))

    expect(names.length).toBeGreaterThan(4)
    expect(new Set(names).size).toBe(names.length)
  })

  it('can edit every field the API accepts', () => {
    // Decision: this screen edits everything `UpdateVehicleRequest` takes. That is true on the day it ships
    // and stops being true the moment someone adds a field to the contract, so it is asserted rather than
    // remembered. Status and IsDefault are excluded - they are lifecycle, driven from the garage.
    const declared = new Set(Object.values(EDITORS).flatMap((e) => e.fields.map((f) => f.key)))
    const expected = [
      'colour', 'vin', 'bodyStyle', 'seller', 'defaultGarage', 'notes', 'motExpirySeed', 'vedExpiry',
      'vedAnnualCost', 'ulezCompliant', 'purchasePrice',
      'insurer', 'policyNumber', 'periodStart', 'periodEnd', 'coverType', 'premium', 'excessCompulsory',
      'excessVoluntary', 'ncbYears',
      'oilSpec', 'oilCapacityLitres', 'coolantSpec', 'coolantCapacityLitres', 'fuelTankCapacityLitres',
      'brakeFluidSpec', 'transmissionOilSpec', 'sparkPlugPart', 'oilFilterPart', 'airFilterPart',
      'fuelFilterPart', 'cabinFilterPart',
      'tyreSize', 'pressureFrontPsi', 'pressureRearPsi', 'pressureFrontLadenPsi', 'pressureRearLadenPsi',
      'minTreadMm',
      'provider', 'expiry',
    ]
    expect([...expected].filter((k) => !declared.has(k))).toEqual([])
  })

  it('seeds an editor from the stored values rather than opening blank', async () => {
    mockApi()
    renderPage()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Edit the insurance policy' }))
    // Placeholders would look identical and save nothing. This caught a real bug on the old panel.
    expect(await screen.findByLabelText('Insurer')).toHaveValue('Admiral')
    expect(screen.getByLabelText('Cover type')).toHaveValue('Comprehensive')
    expect(screen.getByLabelText('Premium £/yr')).toHaveValue('517.14')
  })

  it('sends the tank capacity inside the fluids block', async () => {
    const read = mockApi()
    renderPage()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Edit the fluid and parts specs' }))
    await user.type(screen.getByLabelText('Fuel tank L'), '59')
    await user.click(screen.getByRole('button', { name: /^Save$/ }))

    await vi.waitFor(() => expect(read()).not.toBeNull())
    // The whole block goes, because an untouched field sends its stored value - which the server's merge
    // makes harmless, and which is why this is toMatchObject rather than toEqual.
    expect(read()).toMatchObject({ fluids: { fuelTankCapacityLitres: 59, oilSpec: '10W-40 semi-synthetic' } })
  })

  it('sends null for a blank, which the server merge leaves unchanged', async () => {
    const read = mockApi()
    renderPage()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Edit the fluid and parts specs' }))
    await user.clear(screen.getByLabelText('Engine oil'))
    await user.click(screen.getByRole('button', { name: /^Save$/ }))

    await vi.waitFor(() => expect(read()).not.toBeNull())
    // Null, and it does NOT clear: `VehicleUpdateService` merges `patch.X ?? stored.X` on every field. The
    // fuel-tank editor promised the opposite ("leave blank to clear it") for two phases after the server
    // stopped honouring it, and the test that should have caught it asserted only the request body.
    expect(read()).toMatchObject({ fluids: { oilSpec: null } })
  })

  it('edits breakdown cover, which nothing could write before', async () => {
    const read = mockApi()
    renderPage()
    const user = userEvent.setup()

    await user.click(await screen.findByRole('button', { name: 'Edit breakdown cover' }))
    await user.type(screen.getByLabelText('Provider'), 'Green Flag')
    await user.click(screen.getByRole('button', { name: /^Save$/ }))

    await vi.waitFor(() => expect(read()).not.toBeNull())
    expect(read()).toMatchObject({ breakdown: { provider: 'Green Flag' } })
  })

  it('offers no control for the two founding facts', async () => {
    mockApi()
    renderPage()
    await screen.findByText('Odometer at purchase')

    // The purchase date and the odometer at purchase are create-only in the API: the odometer one seeded a
    // MileageReading that every mile-since figure is measured from, so correcting it here would leave that
    // reading behind. The absent control is the statement.
    expect(screen.queryByRole('button', { name: /odometer/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /purchase date/i })).not.toBeInTheDocument()
  })
})

describe('the vehicle screen - accessibility', () => {
  it('has no axe violations', async () => {
    mockApi()
    const { container } = renderPage()
    await screen.findByText('Engine oil')
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no axe violations with an editor open', async () => {
    mockApi()
    const { container } = renderPage()
    const user = userEvent.setup()
    // A sheet's contents are never swept by rendering the page - it is closed. This is the only way the form
    // inside it gets checked at all, and one sheet checks all nine: they share a renderer.
    await user.click(await screen.findByRole('button', { name: 'Edit the identity details' }))
    expect(await axe(container)).toHaveNoViolations()
  })

  it('explains an empty check list rather than showing an empty table', async () => {
    mockApi()
    renderPage()
    // BT53 predates the starter set and has none. The checks screen is empty BECAUSE this list is.
    expect(await screen.findByText(/No checks defined, so the checks screen has nothing to show/)).toBeInTheDocument()
  })
})
