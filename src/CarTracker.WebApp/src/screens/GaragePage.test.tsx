import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GarageItem } from '../api/client'
import { createQueryClient } from '../api/queries'
import { IconSprite } from '../components/IconSprite'
import { LinkProvider } from '../lib/link'
import { __resetScrollLock } from '../lib/useScrollLock'
import { ToastProvider } from '../shell/Toast'
import { ThemeProvider } from '../theme/ThemeProvider'
import { axe } from '../test/axe'
import { GaragePage } from './GaragePage'

/** BT53 AKJ's real figures — the ones the whole project is calibrated against. */
const BT53: GarageItem = {
  vehicleId: 1,
  registration: 'BT53 AKJ',
  name: 'Land Rover Freelander 1',
  status: 'Active',
  isDefault: true,
  currentMileage: 80_712,
  milesSincePurchase: 4_080,
  costPerMile: 0.84,
  monthlyAverage: 860,
  averageMpg: 28.7,
  latestMpg: 25.4,
  mot: { name: 'MOT', expiryDate: '2027-07-08', daysRemaining: 359, urgency: 'Ok', source: 'derived' },
  overdueCheckCount: 7,
  neverLoggedCheckCount: 1,
  openAnomalyCount: 1,
  renewalsOk: true,
}

function mockGarage(items: GarageItem[], status = 200) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () =>
      new Response(JSON.stringify(items), { status, headers: { 'Content-Type': 'application/json' } }),
    ),
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
})

afterEach(() => vi.unstubAllGlobals())

const renderGarage = () =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter>
            {/* The real link renderer, so hrefs are the ones the app ships. */}
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <GaragePage />
              </div>
            </LinkProvider>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )

describe('the garage', () => {
  it('renders a card with the real derived figures', async () => {
    mockGarage([BT53])
    renderGarage()

    const card = await screen.findByRole('link', { name: /open dashboard/i })
    const c = within(card)

    expect(c.getByText('80,712 mi')).toBeInTheDocument()
    expect(c.getByText('£0.84/mi')).toBeInTheDocument()
    // 359 days, not the sheet's stale 23. The defect that started the project.
    expect(c.getByText('359 days')).toBeInTheDocument()
    expect(c.getByText('28.7 MPG')).toBeInTheDocument()
  })

  it('shows the latest MPG, not the average, as the latest', async () => {
    mockGarage([BT53])
    renderGarage()

    const card = within(await screen.findByRole('link', { name: /open dashboard/i }))
    // 25.4 against an average of 28.7. Showing the average here would tell the owner the car is currently
    // doing better than the last tank did.
    expect(card.getByText('latest 25.4')).toBeInTheDocument()
  })

  it('keeps the integrity flag off the due axis', async () => {
    mockGarage([BT53])
    const { container } = renderGarage()
    await screen.findByRole('link', { name: /open dashboard/i })

    const integrity = screen.getByText(/1 integrity flag/)
    expect(integrity).toHaveClass('pill', 'info')

    // The due pill is a different axis, and must not borrow blue.
    expect(screen.getByText('7 checks overdue')).toHaveClass('due')
    expect(container.querySelector('.pill.due')).not.toHaveClass('info')
  })

  it('links to that car and nowhere else', async () => {
    mockGarage([BT53])
    renderGarage()

    // The registration in the URL (DEC-007). The design has no routing at all and never puts it in one.
    const card = await screen.findByRole('link', { name: /open dashboard/i })
    expect(card).toHaveAttribute('href', '/bt53akj/dashboard')

    // And the card's own name is a name, not a recital: the design makes the whole card one <a>, so without
    // an explicit label a screen reader reads every figure on it as the link's name.
    expect(card).toHaveAccessibleName('BT53 AKJ, Land Rover Freelander 1 — open dashboard')
  })

  it('says what needs attention rather than nothing', async () => {
    mockGarage([BT53])
    renderGarage()
    expect(await screen.findByText('7 checks overdue', { selector: '.car-foot span, span' })).toBeInTheDocument()
  })

  it('reports a car with no readings as unknown, not zero', async () => {
    mockGarage([{
      ...BT53,
      currentMileage: null,
      milesSincePurchase: null,
      costPerMile: null,
      monthlyAverage: null,
      averageMpg: null,
      latestMpg: null,
      mot: { name: 'MOT', expiryDate: null, daysRemaining: null, urgency: null, source: null },
      overdueCheckCount: 0,
      neverLoggedCheckCount: 15,
      openAnomalyCount: 0,
      renewalsOk: false,
    }])
    renderGarage()

    const card = within(await screen.findByRole('link', { name: /open dashboard/i }))

    // A brand-new car. "0 mi" would be a claim about a car nobody has read the odometer of, and "Renewals OK"
    // about one whose dates nobody has entered — the same shape of lie as the sheet's stale countdown.
    expect(card.getByText('no readings yet')).toBeInTheDocument()
    expect(card.getByText('not recorded')).toBeInTheDocument()
    expect(card.queryByText('Renewals OK')).not.toBeInTheDocument()
    expect(card.getByText('15 never logged')).toBeInTheDocument()
  })

  it('does not offer to add a car while it is still loading', () => {
    // Pending is not empty. Telling someone with a car that they have none, for a beat, is worse than a wait.
    mockGarage([BT53])
    renderGarage()
    expect(screen.queryByRole('button', { name: /Add a vehicle/ })).not.toBeInTheDocument()
    expect(screen.getByText(/Loading the garage/)).toBeInTheDocument()
  })

  it('offers the add-car flow once the garage is known to be empty', async () => {
    mockGarage([])
    renderGarage()
    expect(await screen.findByRole('button', { name: /Add a vehicle/ })).toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    mockGarage([BT53])
    const { container } = renderGarage()
    await screen.findByRole('link', { name: /open dashboard/i })
    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('the add-vehicle sheet', () => {
  it('opens, and does not promise a DVLA lookup', async () => {
    mockGarage([])
    const user = userEvent.setup()
    renderGarage()

    await user.click(await screen.findByRole('button', { name: /Add a vehicle/ }))
    const sheet = await screen.findByRole('dialog', { name: 'Add a vehicle' })

    // The design leads with a "Look up" button promising DVLA make/model/MOT. It does not exist (§8,
    // unscheduled), so it is not here: a button that looks like the fast path and does nothing is worse on
    // this screen than anywhere, because it is the first thing anyone does.
    expect(within(sheet).queryByRole('button', { name: /Look up/i })).not.toBeInTheDocument()
    expect(within(sheet).queryByText(/DVLA/i)).not.toBeInTheDocument()
  })

  it('asks for the mileage at purchase, which is what it stores', async () => {
    mockGarage([])
    const user = userEvent.setup()
    renderGarage()

    await user.click(await screen.findByRole('button', { name: /Add a vehicle/ }))

    // The design asks for "Current mileage". For a car bought two years ago that is a different number, and
    // it would land at the bottom of the odometer's history as the founding reading everything is measured
    // from.
    expect(screen.getByLabelText('Mileage at purchase')).toBeInTheDocument()
    expect(screen.getByText(/becomes the opening odometer reading/)).toBeInTheDocument()
  })

  it('refuses to submit without the fields the API needs', async () => {
    mockGarage([])
    const user = userEvent.setup()
    renderGarage()

    await user.click(await screen.findByRole('button', { name: /Add a vehicle/ }))
    await user.click(screen.getByRole('button', { name: 'Add vehicle' }))

    expect(screen.getByText('A car needs its registration.')).toBeInTheDocument()
    expect(screen.getByText('Which make?')).toBeInTheDocument()
  })

  it('defaults to the starter set, and says how many', async () => {
    mockGarage([])
    const user = userEvent.setup()
    renderGarage()

    await user.click(await screen.findByRole('button', { name: /Add a vehicle/ }))

    // CheckDefinition is vehicle-scoped and nothing else creates one, so a car created with none has a
    // permanently empty checks screen. The default matters.
    const select = screen.getByLabelText('Regular checks') as HTMLSelectElement
    expect(select.value).toBe('GenericStarterSet')
    expect(screen.getByText(/starter set is 15 checks that apply to any car/)).toBeInTheDocument()
  })

  it('has no axe violations with the sheet open', async () => {
    mockGarage([])
    const user = userEvent.setup()
    renderGarage()
    await user.click(await screen.findByRole('button', { name: /Add a vehicle/ }))
    await waitFor(() => screen.getByRole('dialog'))
    expect(await axe(document.body)).toHaveNoViolations()
  })
})
