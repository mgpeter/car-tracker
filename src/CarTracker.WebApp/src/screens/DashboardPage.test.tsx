import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../api/queries'
import { IconSprite } from '../components/IconSprite'
import { LinkProvider } from '../lib/link'
import { __resetScrollLock } from '../lib/useScrollLock'
import { __resetFuelUnit, setFuelUnit } from '../lib/fuelUnit'
import { VehicleProvider } from '../routes'
import { ToastProvider } from '../shell/Toast'
import { axe } from '../test/axe'
import { ThemeProvider } from '../theme/ThemeProvider'
import { DashboardPage } from './DashboardPage'

const renewal = (over: Partial<Record<string, unknown>> = {}) => ({
  name: 'MOT',
  expiryDate: null,
  daysRemaining: null,
  urgency: null,
  source: null,
  ...over,
})

/**
 * BT53 as the workbook has it, at the reference date the five defects were verified against — with the real
 * figures, not the Dashboard sheet's stored ones.
 */
const summary = (over: Record<string, unknown> = {}) => ({
  vehicleId: 1,
  registration: 'BT53 AKJ',
  name: 'Land Rover Freelander',
  asOfDate: '2026-07-14',
  identity: {
    variant: '1.8 SE Station Wagon · K-series',
    year: 2003,
    colour: 'Navy Blue',
    drivetrain: 'AWD · VCU',
    transmission: 'manual 5-spd',
    engineCode: 'K-series',
    purchaseDate: '2026-03-14',
    daysOwned: 122,
    milesPerDay: 33.4,
    defaultGarage: 'K & P Motors, Kingston',
  },
  mileage: {
    currentMileage: 80_712,
    asOfDate: '2026-07-10',
    milesSincePurchase: 4080,
    hasNonMonotonicHistory: false,
    highestRecordedMileage: 80_712,
  },
  renewals: {
    mot: renewal({ name: 'MOT', expiryDate: '2027-07-08', daysRemaining: 359, urgency: 'Ok', source: 'K & P Motors · passed 8 Jul 2026' }),
    insurance: renewal({ name: 'Insurance', expiryDate: '2027-03-15', daysRemaining: 244, urgency: 'Ok', source: 'Admiral' }),
    roadTax: renewal({ name: 'Road tax', expiryDate: '2027-02-28', daysRemaining: 229, urgency: 'Ok', source: 'VED' }),
    nextServiceDate: renewal({ name: 'Next service', expiryDate: '2027-05-12', daysRemaining: 302, urgency: 'Ok', source: null }),
    nextServiceMiles: 6788,
  },
  spend: {
    fuelYtd: 888.86,
    serviceAndRepairsYtd: 603.99,
    statutoryYtd: 1005.14,
    totalYtd: 3446.71,
    totalSincePurchase: 5146.71,
    totalSincePurchaseExcludingPurchase: 3446.71,
    monthlyAverage: 860,
    costPerMile: 1.26,
    costPerMileExcludingPurchase: 0.84,
    ytdByCategory: {},
  },
  fuel: {
    averageMpg: 28.7,
    perFillAverageMpg: 28.6,
    bestMpg: 32.2,
    worstMpg: 25.42,
    totalLitres: 556.47,
    totalCost: 888.86,
    averagePricePerLitre: 1.597324,
    lastFillDate: '2026-07-10',
    fillCount: 13,
    // Twelve, not thirteen. The fifth defect.
    measuredIntervalCount: 12,
    implausibleCount: 0,
    entries: [
      { fuelEntryId: 1, entryDate: '2026-04-30', mileage: 77_537, litres: 45, totalCost: 71, milesSinceLast: null, mpg: null, litresPer100Km: null, isReliable: false, isPlausible: true, unreliableReason: 'NoPreviousFill', segmentMiles: null, spannedFillCount: 0 },
      { fuelEntryId: 2, entryDate: '2026-05-13', mileage: 77_861, litres: 45.7, totalCost: 72.9, milesSinceLast: 324, mpg: 32.2, litresPer100Km: 8.8, isReliable: true, isPlausible: true, unreliableReason: null, segmentMiles: 324, spannedFillCount: 1 },
      { fuelEntryId: 3, entryDate: '2026-07-10', mileage: 80_712, litres: 47.03, totalCost: 84.61, milesSinceLast: 263, mpg: 25.42, litresPer100Km: 11.1, isReliable: true, isPlausible: true, unreliableReason: null, segmentMiles: 263, spannedFillCount: 1 },
    ],
    pendingFillCount: 0,
    pendingLitres: 0,
    pendingMiles: null,
  },
  checks: { okCount: 7, dueSoonCount: 3, overdueCount: 7, neverLoggedCount: 1, attentionCount: 0, totalCount: 18, checks: [] },
  integrity: { openCount: 0, highestSeverity: null },
  fullTankRangeMiles: null,
  ...over,
})

function mockApi(body: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })),
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
})

afterEach(() => vi.unstubAllGlobals())

const renderDash = () =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={['/bt53akj/dashboard']}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <Routes>
                  <Route
                    path="/:reg/dashboard"
                    element={
                      <VehicleProvider>
                        <DashboardPage />
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

describe('the odometer', () => {
  it('states the true reading in its accessible name', async () => {
    mockApi(summary())
    renderDash()
    // The design's is `aria-label="Odometer reading in miles"` — it names the control and withholds the value,
    // so the one number the drum exists to convey never reaches anyone not looking at it.
    expect(await screen.findByRole('img', { name: 'Odometer: 80,712 miles' })).toBeInTheDocument()
  })

  it('has no drum claiming to be tenths', async () => {
    mockApi(summary())
    renderDash()
    await screen.findByRole('img', { name: /Odometer/ })
    // 080712 is 80,712 whole miles. A red drum fed the units digit reads as 8,071.2 to anyone fluent in
    // odometers — a factor of ten, in the largest type on the page.
    expect(document.querySelector('.tenths')).toBeNull()
    expect(document.querySelectorAll('.drum i')).toHaveLength(6)
  })

  it('says so rather than reading zero when nothing is logged', async () => {
    mockApi(summary({ mileage: { currentMileage: null, asOfDate: null, milesSincePurchase: null, hasNonMonotonicHistory: false, highestRecordedMileage: null } }))
    renderDash()
    // Six zero drums would be a reading of zero. Every new vehicle starts here.
    expect(await screen.findByText('No mileage logged yet')).toBeInTheDocument()
    expect(document.querySelector('.drum')).toBeNull()
  })
})

describe('renewals', () => {
  it('rewrites the colour-only legend', async () => {
    mockApi(summary())
    renderDash()
    // The design's is "red under 30 days · amber under 60" — its one colour-only status statement, which
    // `.rule i` then paints orange, so "red" renders as neither red nor amber.
    expect(await screen.findByText(/Due under 30 days · due soon under 60/)).toBeInTheDocument()
    expect(screen.queryByText(/red under 30/i)).not.toBeInTheDocument()
  })

  it('distinguishes an expired renewal from an urgent one', async () => {
    mockApi(
      summary({
        renewals: {
          ...summary().renewals,
          mot: renewal({ name: 'MOT', expiryDate: '2026-07-02', daysRemaining: -12, urgency: 'Red', source: 'seed' }),
          roadTax: renewal({ name: 'Road tax', expiryDate: '2026-08-06', daysRemaining: 23, urgency: 'Red', source: 'VED' }),
        },
      }),
    )
    renderDash()
    // Both are Red to the domain. They are not the same fact, and the pills must not say so.
    expect(await screen.findByText('Expired')).toBeInTheDocument()
    expect(screen.getByText('Due')).toBeInTheDocument()
    expect(screen.getByText('12 days ago')).toBeInTheDocument()
  })

  it('does not call a renewal with no date OK', async () => {
    mockApi(summary({ renewals: { mot: renewal(), insurance: renewal({ name: 'Insurance' }), roadTax: renewal({ name: 'Road tax' }), nextServiceDate: renewal({ name: 'Next service' }), nextServiceMiles: null } }))
    renderDash()
    expect(await screen.findAllByText('Not set')).toHaveLength(4)
    // Scoped to the panel: "OK" is also a check tile's label, and that one is legitimately OK.
    const renewals = document.querySelector('.renewals') as HTMLElement
    expect(within(renewals).queryByText('OK')).not.toBeInTheDocument()
  })
})

describe('needs attention', () => {
  it('leads with the expired renewal, not the overdue checks', async () => {
    mockApi(
      summary({
        renewals: { ...summary().renewals, mot: renewal({ name: 'MOT', expiryDate: '2026-07-02', daysRemaining: -12, urgency: 'Red', source: 'seed' }) },
      }),
    )
    renderDash()
    const alerts = await screen.findAllByRole('heading', { level: 3 })
    // An expired MOT is a car that should not be on the road; seven lapsed checks are a look under the bonnet.
    expect(alerts[0]).toHaveTextContent(/MOT expired 12 days ago/)
  })

  it('surfaces the 83,000 mi row as a flag, not a correction', async () => {
    mockApi(
      summary({
        mileage: { currentMileage: 80_712, asOfDate: '2026-07-10', milesSincePurchase: 4080, hasNonMonotonicHistory: true, highestRecordedMileage: 83_000 },
      }),
    )
    renderDash()
    expect(await screen.findByText(/A logged reading is above the current odometer/)).toBeInTheDocument()
    expect(screen.getByText(/83,000 mi, against a latest of 80,712 mi/)).toBeInTheDocument()
    // The odometer does not move. Flagged, never silently accepted — spec §5.3.
    expect(screen.getByRole('img', { name: 'Odometer: 80,712 miles' })).toBeInTheDocument()
  })

  it('says nothing is outstanding only when nothing is', async () => {
    mockApi(summary({ checks: { okCount: 18, dueSoonCount: 0, overdueCount: 0, neverLoggedCount: 0, attentionCount: 0, totalCount: 18, checks: [] } }))
    renderDash()
    expect(await screen.findByText('Nothing is overdue, expired, or flagged')).toBeInTheDocument()
    expect(screen.queryByText(/never been logged/)).not.toBeInTheDocument()
  })

  it('does not read a never-logged check as clear', async () => {
    mockApi(summary({ checks: { okCount: 0, dueSoonCount: 0, overdueCount: 0, neverLoggedCount: 1, attentionCount: 0, totalCount: 1, checks: [] } }))
    renderDash()
    // Never-logged raises no alert — it is not overdue, because it has no interval to be past. But it is not
    // "all clear" either, and reading it as fine is precisely how the workbook came to count 17 of 18.
    await screen.findByText('Nothing is overdue, expired, or flagged')
    expect(screen.getByText(/1 check has never been logged/)).toBeInTheDocument()
    expect(screen.queryByText('All clear')).not.toBeInTheDocument()
  })
})

describe('fuel', () => {
  it('counts measurable intervals, not fills', async () => {
    mockApi(summary())
    renderDash()
    // DEC-012, on screen. Thirteen fills, twelve measurable intervals — the first has no predecessor. The
    // design plots thirteen points and rests two headline figures on the interval that never happened.
    expect(await screen.findByText(/13 fills · 12 measurable intervals/)).toBeInTheDocument()
  })

  it('plots only the intervals that exist', async () => {
    mockApi(summary())
    renderDash()
    const chart = await screen.findByRole('img', { name: /Fuel economy/ })
    // Three entries, one with a null MPG. Two points.
    expect(chart).toHaveAccessibleName(/across 2 measured intervals/)
    expect(chart).toHaveAccessibleName(/ranging 25.4 to 32.2 MPG/)
    expect(chart).toHaveAccessibleName(/Latest 25.4 on 10 Jul/)
  })

  it('withholds MPG on a single fill rather than showing zero', async () => {
    mockApi(
      summary({
        fuel: { ...summary().fuel, averageMpg: null, bestMpg: null, worstMpg: null, fillCount: 1, measuredIntervalCount: 0, entries: [summary().fuel.entries[0]] },
      }),
    )
    renderDash()
    expect(await screen.findByText(/1 fill logged · economy needs a second fill to measure from/)).toBeInTheDocument()
    expect(screen.getByText(/No measurable intervals yet/)).toBeInTheDocument()
  })

  it('renders the panel and chart in L/100 km when that unit is chosen', async () => {
    setFuelUnit('l100')
    mockApi(summary())
    renderDash()

    // 28.7 MPG ≡ 9.8 L/100 km — the headline flips value and unit, recomputing nothing.
    expect(await screen.findByText('9.8')).toBeInTheDocument()
    expect(screen.getByText('L/100km')).toBeInTheDocument()
    expect(screen.queryByText('MPG')).not.toBeInTheDocument()

    // The chart's derived accessible name switches unit and inverts best (lower L/100 km is better): the
    // entries plot 8.8 and 11.1, so best is 8.8, not the highest.
    const chart = screen.getByRole('img', { name: /Fuel economy/ })
    expect(chart).toHaveAccessibleName(/ranging 8.8 to 11.1 L\/100km/)
    expect(chart).toHaveAccessibleName(/Best 8.8/)
  })

  it('shows an estimated full-tank range when the tank capacity is known', async () => {
    mockApi(summary({ fullTankRangeMiles: 379 }))
    renderDash()
    // Labelled as a full-tank estimate, never a live "remaining" gauge.
    expect(await screen.findByText(/Full-tank range/)).toBeInTheDocument()
    expect(screen.getByText(/≈\s*379 mi/)).toBeInTheDocument()
  })

  it('shows no range when the tank capacity is unset — a guess in the same typeface is worse', async () => {
    mockApi(summary({ fullTankRangeMiles: null }))
    renderDash()
    await screen.findByText(/13 fills/)
    expect(screen.queryByText(/Full-tank range/)).not.toBeInTheDocument()
  })

  it('shows a part-tank in progress when the last fill did not close the tank', async () => {
    mockApi(
      summary({
        fuel: {
          ...summary().fuel,
          pendingFillCount: 1,
          pendingLitres: 18,
          pendingMiles: 108,
          entries: [
            ...summary().fuel.entries,
            { fuelEntryId: 4, entryDate: '2026-07-12', mileage: 80_820, litres: 18, totalCost: 30, milesSinceLast: 108, mpg: null, litresPer100Km: null, isReliable: false, isPlausible: true, unreliableReason: 'AwaitingFullTank', segmentMiles: null, spannedFillCount: 0 },
          ],
        },
      }),
    )
    renderDash()
    // The open tank is visible where the owner looks first — a calm line, not a flag.
    expect(await screen.findByText(/Part-tank in progress · 1 fill · 108 mi · 18.00 L — MPG pending next full fill/)).toBeInTheDocument()
    // And the "That tank" tile reads the partial as pending, not as "no previous fill".
    expect(screen.getByText(/partial fill · MPG pending your next full fill/)).toBeInTheDocument()
  })
})

describe('checks', () => {
  it('states the sum, so a 17-of-18 cannot recur silently', async () => {
    mockApi(summary())
    renderDash()
    // The workbook's dashboard counts 17 of 18: "Spare tyre pressure" has never been logged and falls out of
    // its three buckets. Never-logged is the fourth state, and the arithmetic is on screen.
    expect(await screen.findByText(/7 \+ 3 \+ 7 \+ 0 \+ 1 =/)).toBeInTheDocument()
    // The tiles must sum to the definitions, and the sum is on screen rather than implied.
    const foot = document.querySelector('.cfoot') as HTMLElement
    expect(within(foot).getByText('18')).toBeInTheDocument()
  })

  it('explains an empty check list instead of showing four zeros', async () => {
    mockApi(summary({ checks: { okCount: 0, dueSoonCount: 0, overdueCount: 0, neverLoggedCount: 0, attentionCount: 0, totalCount: 0, checks: [] } }))
    renderDash()
    expect(await screen.findByText(/No checks defined for this vehicle, so there is nothing to be due/)).toBeInTheDocument()
  })
})

describe('the dossier', () => {
  it('drops a chip it has nothing to put in', async () => {
    mockApi(summary({ identity: { ...summary().identity, colour: null, defaultGarage: null } }))
    renderDash()
    await screen.findByText('Year')
    // Scoped to the chip row — "Garage" is also a nav destination, and the nav is not what is under test.
    const chips = document.querySelector('.chips') as HTMLElement
    // "Colour —" is a worse answer than no chip at all.
    expect(within(chips).queryByText('Colour')).not.toBeInTheDocument()
    expect(within(chips).queryByText('Garage')).not.toBeInTheDocument()
    expect(within(chips).getByText('Year')).toBeInTheDocument()
  })

  it('derives days owned rather than carrying a stored one', async () => {
    mockApi(summary())
    renderDash()
    const chips = await screen.findByText('Owned')
    expect(within(chips.parentElement as HTMLElement).getByText('122 days')).toBeInTheDocument()
  })
})

describe('data integrity panel', () => {
  it('renders nothing at all when there are no flags', async () => {
    mockApi(summary({ integrity: { openCount: 0, highestSeverity: null } }))
    renderDash()
    await screen.findByRole('img', { name: /Odometer/ })
    // Returning null is the design, not a shortcut: an empty panel headed "Data integrity" implies a question
    // was asked and answered clean, which is a stronger claim than "nothing to say". The clean state is the
    // norm, so the section simply is not there. (Scoped past the nav's own "Data integrity" More-menu link.)
    expect(screen.queryByText(/figures? on this car (is|are) in question/)).not.toBeInTheDocument()
    expect(document.querySelector('.attn-info')).toBeNull()
  })

  it('leads with the worst severity and links to the queue', async () => {
    mockApi(summary({ integrity: { openCount: 2, highestSeverity: 'Error' } }))
    renderDash()
    // The summary carries a headline (count + worst severity); the panel does not load every flag's detail.
    expect(await screen.findByText('2 figures on this car are in question')).toBeInTheDocument()
    expect(screen.getByText(/2 open flags · worst is error/)).toBeInTheDocument()
    const review = screen.getByRole('link', { name: /Review 2 flags/i })
    expect(review.getAttribute('href')).toContain('/data-integrity')
  })
})

describe('accessibility', () => {
  it('has no axe violations', async () => {
    mockApi(summary())
    const { container } = renderDash()
    await screen.findByRole('img', { name: /Odometer/ })
    expect(await axe(container)).toHaveNoViolations()
  })
})
