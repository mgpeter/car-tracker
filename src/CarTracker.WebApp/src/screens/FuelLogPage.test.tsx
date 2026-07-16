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
import { FuelLogPage } from './FuelLogPage'

const entry = (over: Record<string, unknown> = {}) => ({
  fuelEntryId: 1,
  entryDate: '2026-05-13',
  mileage: 77_861,
  litres: 45.7,
  pricePerLitre: 1.595,
  totalCost: 72.9,
  station: 'Tesco Kingston',
  fillLevel: 'Full',
  notes: null,
  milesSinceLast: 324,
  mpg: 32.2,
  litresPer100Km: 8.8,
  isReliable: true,
  isPlausible: true,
  unreliableReason: null,
  ...over,
})

/**
 * BT53's real shape: the first fill has no predecessor, so it has no MPG. The workbook gives it 24.5 from a
 * "334 miles since last" against a reading that exists nowhere (DEC-012), and that figure is what drags its
 * Worst MPG to 24.49 and makes its average a 13-value one.
 */
const FIRST = entry({
  fuelEntryId: 1,
  entryDate: '2026-04-30',
  mileage: 77_537,
  litres: 45,
  milesSinceLast: null,
  mpg: null,
  litresPer100Km: null,
  isReliable: false,
  unreliableReason: 'NoPreviousFill',
  station: null,
  fillLevel: null,
})

const BEST = entry({ fuelEntryId: 2 })

const LAST = entry({
  fuelEntryId: 3,
  entryDate: '2026-07-10',
  mileage: 80_712,
  litres: 47.03,
  pricePerLitre: 1.799,
  totalCost: 84.61,
  station: 'Shell Kingston',
  notes: 'V-Power · premium price',
  milesSinceLast: 263,
  mpg: 25.42,
  fillLevel: 'Quarter',
})

const fuel = (over: Record<string, unknown> = {}) => ({
  averageMpg: 28.7,
  perFillAverageMpg: 28.6,
  bestMpg: 32.2,
  worstMpg: 25.42,
  totalLitres: 137.73,
  totalCost: 228.41,
  averagePricePerLitre: 1.597324,
  lastFillDate: '2026-07-10',
  fillCount: 3,
  measuredIntervalCount: 2,
  implausibleCount: 0,
  entries: [FIRST, BEST, LAST],
  ...over,
})

const SUMMARY = {
  vehicleId: 1,
  registration: 'BT53 AKJ',
  name: 'Land Rover Freelander',
  asOfDate: '2026-07-14',
  identity: { variant: null, year: 2003, colour: null, drivetrain: null, transmission: null, engineCode: null, purchaseDate: '2026-03-14', daysOwned: 122, milesPerDay: 33.4, defaultGarage: null },
  mileage: { currentMileage: 80_712, asOfDate: '2026-07-10', milesSincePurchase: 4080, hasNonMonotonicHistory: false, highestRecordedMileage: 80_712 },
  renewals: { mot: { name: 'MOT', expiryDate: null, daysRemaining: null, urgency: null, source: null }, insurance: { name: 'Insurance', expiryDate: null, daysRemaining: null, urgency: null, source: null }, roadTax: { name: 'Road tax', expiryDate: null, daysRemaining: null, urgency: null, source: null }, nextServiceDate: { name: 'Next service', expiryDate: null, daysRemaining: null, urgency: null, source: null }, nextServiceMiles: null },
  spend: { fuelYtd: 0, serviceAndRepairsYtd: 0, statutoryYtd: 0, totalYtd: 0, totalSincePurchase: 0, totalSincePurchaseExcludingPurchase: 0, monthlyAverage: null, costPerMile: null, costPerMileExcludingPurchase: null, ytdByCategory: {} },
  fuel: fuel(),
  checks: { okCount: 0, dueSoonCount: 0, overdueCount: 0, neverLoggedCount: 0, totalCount: 0, checks: [] },
  integrity: { openCount: 0, highestSeverity: null },
}

let posted: unknown = null

function mockApi(f: unknown = fuel()) {
  posted = null
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL, init?: RequestInit) => {
      const path = String(url)
      if (init?.method === 'POST') {
        posted = JSON.parse(String(init.body))
        return new Response(JSON.stringify({ id: 9, flags: [] }), { status: 201, headers: { 'Content-Type': 'application/json' } })
      }
      const body = path.endsWith('/fuel') ? f : { ...SUMMARY, fuel: f }
      return new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }),
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

const renderFuel = () =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={['/bt53akj/fuel']}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <Routes>
                  <Route path="/:reg/fuel" element={<VehicleProvider><FuelLogPage /></VehicleProvider>} />
                </Routes>
              </div>
            </LinkProvider>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )

const rowFor = (mileage: string) =>
  [...document.querySelectorAll('.dt-row')].find((r) => r.textContent?.includes(mileage)) as HTMLElement

describe('the fills table', () => {
  it('gives the first fill no MPG at all', async () => {
    renderFuel()
    await screen.findByText('80,712')
    const first = rowFor('77,537')
    // The design prints 24.5 here with an "Estimate" pill. A caveated number is still a number, and it still
    // gets averaged — which is exactly how the workbook's Worst MPG became 24.49.
    expect(within(first).queryByText('24.5')).not.toBeInTheDocument()
    expect(within(first).getByText('first fill · nothing to measure from')).toBeInTheDocument()
    expect(within(first).queryByText('Estimate')).not.toBeInTheDocument()
  })

  it('shows a partial fill its MPG', async () => {
    renderFuel()
    await screen.findByText('80,712')
    const last = rowFor('80,712')
    // The design withholds MPG on anything but a full tank and prints "· ·". The fuel-basis spec removed that
    // rule: the litres are the same receipt figure either way.
    expect(within(last).getByText('25.4')).toBeInTheDocument()
    expect(within(last).getByText('Quarter')).toBeInTheDocument()
  })

  it('marks an implausible figure without deleting it', async () => {
    mockApi(fuel({ entries: [FIRST, entry({ fuelEntryId: 2, mpg: 272.4, isPlausible: false })], implausibleCount: 1 }))
    renderFuel()
    // Kept and marked: the entry is real even when the figure is not. Blue, because it is a data-integrity
    // flag rather than urgency.
    expect(await screen.findByText('272.4')).toBeInTheDocument()
    expect(screen.getByText('Implausible')).toBeInTheDocument()
    expect(screen.getByText(/1 implausible/)).toBeInTheDocument()
  })

  it('reads newest first', async () => {
    renderFuel()
    await screen.findByText('80,712')
    const dates = [...document.querySelectorAll('.dt-row [data-label="Date"]')].map((c) => c.textContent)
    // The domain returns them oldest-first because MPG measures against the previous fill. A log is read from
    // the top.
    expect(dates[0]).toContain('10 Jul')
    expect(dates[2]).toContain('30 Apr')
  })

  it('says a station was not recorded rather than leaving a hole', async () => {
    renderFuel()
    await screen.findByText('80,712')
    expect(within(rowFor('77,537')).getByText('not recorded')).toBeInTheDocument()
  })
})

describe('fleet stats', () => {
  it('counts measurable intervals separately from fills', async () => {
    renderFuel()
    // Three fills, two measurable intervals. That gap is DEC-012.
    expect(await screen.findByText(/3 fills · 2 measurable/)).toBeInTheDocument()
    expect(screen.getByText('MPG · 2 intervals')).toBeInTheDocument()
  })

  it('says the average price is volume-weighted', async () => {
    renderFuel()
    // DEC-011: SUM(cost)/SUM(litres), not the mean of the price column. Not a defect — a different question,
    // answered correctly — which is why it sits outside the count of five.
    expect(await screen.findByText('£1.597')).toBeInTheDocument()
    expect(screen.getByText(/volume-weighted/)).toBeInTheDocument()
  })

  it('withholds every figure on an empty log', async () => {
    mockApi(fuel({ averageMpg: null, bestMpg: null, worstMpg: null, totalLitres: 0, totalCost: 0, averagePricePerLitre: null, lastFillDate: null, fillCount: 0, measuredIntervalCount: 0, entries: [] }))
    renderFuel()
    expect(await screen.findByText(/No fills logged yet\. The first one records an odometer reading/)).toBeInTheDocument()
    expect(screen.getByText('needs two fills')).toBeInTheDocument()
  })
})

describe('the add-fill sheet', () => {
  it('previews MPG from litres alone, on the domain band', async () => {
    renderFuel()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add fill/i }))

    await user.type(screen.getByLabelText(/Odometer/), '80975')
    await user.type(screen.getByLabelText(/^Litres/), '47')

    // 80,975 − 80,712 = 263 mi on 47 L = 25.4 MPG. Scoped to the preview: the table below has its own 25.4,
    // which is the point — they must agree, and the server computes the one that counts.
    const prev = document.querySelector('.mpgprev') as HTMLElement
    expect(within(prev).getByText('25.4')).toBeInTheDocument()
    expect(within(prev).getByText(/263 mi · -3.3 vs 28.7 average/)).toBeInTheDocument()
  })

  it('does not withhold the preview on a partial fill', async () => {
    renderFuel()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add fill/i }))
    await user.type(screen.getByLabelText(/Odometer/), '80975')
    await user.type(screen.getByLabelText(/^Litres/), '47')
    await user.selectOptions(screen.getByLabelText(/Fill level/), 'Quarter')

    // The design prints "· ·" and "MPG withheld" the moment you say partial.
    // Scoped to the sheet: the page footer legitimately says "never withheld on a partial tank", which is the
    // claim this test exists to hold it to.
    const prev = document.querySelector('.mpgprev') as HTMLElement
    expect(within(prev).getByText('25.4')).toBeInTheDocument()
    expect(within(prev).queryByText(/withheld/i)).not.toBeInTheDocument()
    expect(within(prev).getByText('MPG · this tank')).toBeInTheDocument()
  })

  it('uses the domain band, not the design 18-45', async () => {
    renderFuel()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add fill/i }))
    await user.type(screen.getByLabelText(/Odometer/), '80920')
    await user.type(screen.getByLabelText(/^Litres/), '10')

    // 208 mi on 10 L = 94.6 MPG — implausible on both bands. The interesting case is the next test.
    //
    // Scoped to the preview's note, and asserted on the node: the page footer states the same band, which is
    // the claim the preview is being held to, and the note is several text nodes so a string search would
    // match its parent too.
    const note = document.querySelector('.mpgprev .n') as HTMLElement
    expect(note.textContent).toMatch(/outside 10–70 MPG/)
    expect(note.textContent).toMatch(/flagged, not refused/)
    expect(document.querySelector('.mpgprev.warn')).not.toBeNull()
  })

  it('accepts a figure the design would have called suspect', async () => {
    renderFuel()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add fill/i }))
    await user.type(screen.getByLabelText(/Odometer/), '80862')
    await user.type(screen.getByLabelText(/^Litres/), '13')

    // 150 mi on 13 L = 52.5 MPG. Outside the design's hardcoded 18–45 and inside the domain's 10–70. Two bands
    // means a preview that calls a figure suspect which the server then accepts without comment.
    const prev = document.querySelector('.mpgprev') as HTMLElement
    expect(within(prev).getByText('52.5')).toBeInTheDocument()
    expect(document.querySelector('.mpgprev.warn')).toBeNull()
  })

  it('fills the total from litres times price until it is touched', async () => {
    renderFuel()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add fill/i }))
    await user.type(screen.getByLabelText(/^Litres/), '47.03')
    await user.type(screen.getByLabelText(/per litre/i), '1.799')
    expect(screen.getByLabelText(/Total/)).toHaveValue('84.61')

    // Receipts round, so once it is theirs it stays theirs.
    await user.clear(screen.getByLabelText(/Total/))
    await user.type(screen.getByLabelText(/Total/), '84.60')
    await user.clear(screen.getByLabelText(/^Litres/))
    await user.type(screen.getByLabelText(/^Litres/), '47.04')
    expect(screen.getByLabelText(/Total/)).toHaveValue('84.60')
  })

  it('posts what was typed', async () => {
    renderFuel()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add fill/i }))
    await user.type(screen.getByLabelText(/Odometer/), '80975')
    await user.type(screen.getByLabelText(/^Litres/), '47')
    await user.type(screen.getByLabelText(/per litre/i), '1.5')
    await user.type(screen.getByLabelText(/Station/), 'Shell Kingston')
    await user.click(screen.getByRole('button', { name: /save fill/i }))

    await vi.waitFor(() => expect(posted).not.toBeNull())
    expect(posted).toMatchObject({ mileage: 80_975, litres: 47, pricePerLitre: 1.5, station: 'Shell Kingston' })
  })

  it('previews no MPG for the first fill, exactly as the server will', async () => {
    mockApi(fuel({ averageMpg: null, bestMpg: null, worstMpg: null, totalLitres: 0, totalCost: 0, averagePricePerLitre: null, lastFillDate: null, fillCount: 0, measuredIntervalCount: 0, entries: [] }))
    renderFuel()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add fill/i }))
    await user.type(screen.getByLabelText(/Odometer/), '77537')
    await user.type(screen.getByLabelText(/^Litres/), '62')

    // Caught in the browser, against the real BT53. The sheet fell back to the current odometer reading when
    // there were no fills, so this previewed 66.4 MPG — measured from the purchase reading — while the server
    // returned no MPG, because MPG measures fuel burned between two FILLS and there was no previous fill.
    // The reading exists; the interval does not. A preview that contradicts the server is worse than none.
    const prev = document.querySelector('.mpgprev') as HTMLElement
    expect(within(prev).getByText('—')).toBeInTheDocument()
    expect(within(prev).getByText(/the first fill has no previous reading to measure from/)).toBeInTheDocument()
    expect(within(prev).queryByText('66.4')).not.toBeInTheDocument()
  })

  it('has no axe violations with the sheet open', async () => {
    const { container } = renderFuel()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add fill/i }))
    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('accessibility', () => {
  it('has no axe violations', async () => {
    const { container } = renderFuel()
    await screen.findByText('80,712')
    expect(await axe(container)).toHaveNoViolations()
  })
})
