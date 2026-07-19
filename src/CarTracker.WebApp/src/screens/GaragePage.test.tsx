import { QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { GarageItem } from '../api/client'
import { createQueryClient } from '../api/queries'
import { IconSprite } from '../components/IconSprite'
import { LinkProvider } from '../lib/link'
import { updateSettings } from '../lib/settings'
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

/** A trimmed starter set for the add-vehicle tests — three checks, enough to prove selection. */
const STARTER = [
  { name: 'Walk-around', cadenceLabel: 'Weekly', intervalDays: 7, guidance: null },
  { name: 'Engine oil level', cadenceLabel: 'Monthly', intervalDays: 30, guidance: null },
  { name: 'Air-con run', cadenceLabel: 'Monthly', intervalDays: 30, guidance: null },
]

let posted: Record<string, unknown> | null = null

/** Serves the starter set, an empty garage, and captures the create POST body. */
function mockAddVehicle(starter: unknown[]) {
  posted = null
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL, init?: RequestInit) => {
      const path = String(url)
      if (path.includes('/reference/starter-checks')) {
        return new Response(JSON.stringify(starter), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      if (init?.method === 'POST' && path.endsWith('/api/vehicles')) {
        posted = JSON.parse(String(init.body))
        return new Response(JSON.stringify({ id: 2, registration: posted!['registration'] }), { status: 201, headers: { 'Content-Type': 'application/json' } })
      }
      return new Response(JSON.stringify([]), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }),
  )
}

/** BT53's active checks (plus one retired) — the source when testing copy-from-vehicle. */
const SOURCE_CHECKS = [
  { id: 1, name: 'Oil filler cap', cadenceLabel: 'Weekly', intervalDays: 7, guidance: null, displayOrder: 1, isActive: true },
  { id: 2, name: 'VCU rotation', cadenceLabel: 'Monthly', intervalDays: 30, guidance: null, displayOrder: 2, isActive: true },
  { id: 3, name: 'Retired thing', cadenceLabel: 'Monthly', intervalDays: 30, guidance: null, displayOrder: 3, isActive: false },
]

/** A non-empty garage (BT53 as a copy source), its checks, the starter set, and the create POST capture. */
function mockCopyAddVehicle() {
  posted = null
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL, init?: RequestInit) => {
      const path = String(url)
      if (path.includes('/reference/starter-checks')) {
        return new Response(JSON.stringify(STARTER), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      if (path.includes('/checks/definitions')) {
        return new Response(JSON.stringify(SOURCE_CHECKS), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      if (init?.method === 'POST' && path.endsWith('/api/vehicles')) {
        posted = JSON.parse(String(init.body))
        return new Response(JSON.stringify({ id: 2, registration: posted!['registration'] }), { status: 201, headers: { 'Content-Type': 'application/json' } })
      }
      return new Response(JSON.stringify([BT53]), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }),
  )
}

async function fillRequired(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByPlaceholderText('REG PLATE'), 'AB12CDE')
  await user.type(screen.getByPlaceholderText('Land Rover'), 'Toyota')
  await user.type(screen.getByPlaceholderText('Freelander 1'), 'Yaris')
  await user.type(screen.getByPlaceholderText('2003'), '2015')
  fireEvent.change(screen.getByLabelText('Purchase date'), { target: { value: '2026-07-19' } })
  await user.type(screen.getByPlaceholderText('76632'), '48000')
}

/** 401 until a non-empty X-Api-Key is sent, then the garage — the real shape of a fresh install. */
function mockGarageNeedsKey(items: GarageItem[]) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_url: string | URL, init?: RequestInit) => {
      const key = new Headers(init?.headers).get('X-Api-Key')
      if (key === null || key === '') {
        return new Response(JSON.stringify({ title: 'Unauthorized' }), { status: 401, headers: { 'Content-Type': 'application/json' } })
      }
      return new Response(JSON.stringify(items), { status: 200, headers: { 'Content-Type': 'application/json' } })
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

    await screen.findByRole('link', { name: /open dashboard/i })
    // The card is a container now, not one link — figures are siblings of the stretched primary link, so scope
    // to the card element rather than to the link.
    const c = within(document.querySelector('.car') as HTMLElement)

    expect(c.getByText('80,712 mi')).toBeInTheDocument()
    expect(c.getByText('£0.84/mi')).toBeInTheDocument()
    // 359 days, not the sheet's stale 23. The defect that started the project.
    expect(c.getByText('359 days')).toBeInTheDocument()
    expect(c.getByText('28.7 MPG')).toBeInTheDocument()
  })

  it('shows the latest MPG, not the average, as the latest', async () => {
    mockGarage([BT53])
    renderGarage()

    await screen.findByRole('link', { name: /open dashboard/i })
    const card = within(document.querySelector('.car') as HTMLElement)
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

  it('makes the integrity flag its own link to the queue', async () => {
    mockGarage([BT53])
    renderGarage()
    // The count is a link to the data-integrity queue, distinct from the card's dashboard link. A card cannot
    // be one <a> and hold a second destination — nesting anchors is invalid — so the card is a container with
    // a stretched primary link and the flag floats above it.
    const flag = await screen.findByRole('link', { name: /integrity flag.*open the queue/i })
    expect(flag.getAttribute('href')).toContain('/data-integrity')

    // And the card is no longer itself an anchor: it is a div holding two real, separately-named links.
    expect(document.querySelector('.car')?.tagName).toBe('DIV')
    expect(document.querySelector('.car-primary')?.getAttribute('href')).toContain('/dashboard')
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

    await screen.findByRole('link', { name: /open dashboard/i })
    const card = within(document.querySelector('.car') as HTMLElement)

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

describe('the API key panel', () => {
  // The settings store is a module singleton that survives localStorage.clear(); force it empty so this test
  // starts like a fresh install regardless of what ran before it.
  beforeEach(() => updateSettings({ apiKey: '' }))

  it('offers an input to enter the key when the server rejects the request', async () => {
    mockGarageNeedsKey([BT53])
    renderGarage()
    expect(await screen.findByLabelText('API key')).toBeInTheDocument()
    expect(screen.getByText(/needs its API key/)).toBeInTheDocument()
    // Not the dead-end copy pointing at a Settings screen with no field.
    expect(screen.queryByText(/Check it in Settings/)).not.toBeInTheDocument()
  })

  it('saves the key and refetches the garage, which then loads', async () => {
    mockGarageNeedsKey([BT53])
    const user = userEvent.setup()
    renderGarage()

    await user.type(await screen.findByLabelText('API key'), 'sekret-key')
    await user.click(screen.getByRole('button', { name: /Save key/ }))

    // Persisted through the shared settings store, under the documented localStorage key…
    await waitFor(() =>
      expect(JSON.parse(localStorage.getItem('cartracker.settings') ?? '{}').apiKey).toBe('sekret-key'),
    )
    // …and the garage refetches with it, so the card the 401 hid now renders.
    expect(await screen.findByRole('link', { name: /open dashboard/i })).toBeInTheDocument()
  })

  it('will not save an empty key', async () => {
    mockGarageNeedsKey([BT53])
    renderGarage()
    await screen.findByLabelText('API key')
    expect(screen.getByRole('button', { name: /Save key/ })).toBeDisabled()
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

describe('the starter-check selection', () => {
  it('reveals the starter checks all-on and narrows the count as you deselect', async () => {
    mockAddVehicle(STARTER)
    const user = userEvent.setup()
    renderGarage()
    await user.click(await screen.findByRole('button', { name: /Add a vehicle/ }))

    // The three checks appear, every one selected, with a live count.
    expect(await screen.findByRole('checkbox', { name: /Walk-around/ })).toBeChecked()
    expect(screen.getByText('3 of 3')).toBeInTheDocument()

    // Deselecting one moves the count and leaves it unchecked.
    await user.click(screen.getByRole('checkbox', { name: /Air-con run/ }))
    expect(screen.getByRole('checkbox', { name: /Air-con run/ })).not.toBeChecked()
    expect(screen.getByText('2 of 3')).toBeInTheDocument()
  })

  it('posts only the kept checks when some are deselected', async () => {
    mockAddVehicle(STARTER)
    const user = userEvent.setup()
    renderGarage()
    await user.click(await screen.findByRole('button', { name: /Add a vehicle/ }))
    await screen.findByRole('checkbox', { name: /Walk-around/ })

    await user.click(screen.getByRole('checkbox', { name: /Air-con run/ }))
    await fillRequired(user)
    await user.click(screen.getByRole('button', { name: 'Add vehicle' }))

    await vi.waitFor(() => expect(posted).not.toBeNull())
    expect(posted!['selectedCheckNames']).toEqual(['Walk-around', 'Engine oil level'])
  })

  it('sends no selection when the list is left untouched (byte-for-byte today)', async () => {
    mockAddVehicle(STARTER)
    const user = userEvent.setup()
    renderGarage()
    await user.click(await screen.findByRole('button', { name: /Add a vehicle/ }))
    await screen.findByRole('checkbox', { name: /Walk-around/ })

    await fillRequired(user)
    await user.click(screen.getByRole('button', { name: 'Add vehicle' }))

    await vi.waitFor(() => expect(posted).not.toBeNull())
    // Untouched → the field is omitted, so the server applies the whole set exactly as before.
    expect(posted!).not.toHaveProperty('selectedCheckNames')
    expect(posted!['checkSource']).toBe('GenericStarterSet')
  })

  it('hides the checks and sends no selection when None is chosen', async () => {
    mockAddVehicle(STARTER)
    const user = userEvent.setup()
    renderGarage()
    await user.click(await screen.findByRole('button', { name: /Add a vehicle/ }))
    await screen.findByRole('checkbox', { name: /Walk-around/ })

    await user.selectOptions(screen.getByLabelText('Regular checks'), 'None')
    expect(screen.queryByRole('checkbox', { name: /Walk-around/ })).not.toBeInTheDocument()

    await fillRequired(user)
    await user.click(screen.getByRole('button', { name: 'Add vehicle' }))

    await vi.waitFor(() => expect(posted).not.toBeNull())
    expect(posted!).not.toHaveProperty('selectedCheckNames')
    expect(posted!['checkSource']).toBe('None')
  })

  it('offers copy-from-vehicle only when the garage has a car to copy from', async () => {
    // Empty garage → the option is absent.
    mockAddVehicle(STARTER)
    const user = userEvent.setup()
    renderGarage()
    await user.click(await screen.findByRole('button', { name: /Add a vehicle/ }))
    await screen.findByRole('checkbox', { name: /Walk-around/ })
    expect(screen.queryByRole('option', { name: /Copy from another vehicle/ })).not.toBeInTheDocument()
  })

  it('copies another vehicle: shows its active checks and posts the id + kept names', async () => {
    mockCopyAddVehicle()
    const user = userEvent.setup()
    renderGarage()
    // BT53 is in the garage, so "Add a vehicle" is available and copy is offered.
    await user.click(await screen.findByRole('button', { name: /Add a vehicle/ }))
    await user.selectOptions(screen.getByLabelText('Regular checks'), 'CopyFromVehicle')

    // The source's ACTIVE checks appear; the retired one does not; count is over the two active.
    expect(await screen.findByRole('checkbox', { name: /Oil filler cap/ })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: /VCU rotation/ })).toBeInTheDocument()
    expect(screen.queryByRole('checkbox', { name: /Retired thing/ })).not.toBeInTheDocument()
    expect(screen.getByText('2 of 2')).toBeInTheDocument()

    // Deselect one, then create.
    await user.click(screen.getByRole('checkbox', { name: /VCU rotation/ }))
    await fillRequired(user)
    await user.click(screen.getByRole('button', { name: 'Add vehicle' }))

    await vi.waitFor(() => expect(posted).not.toBeNull())
    expect(posted!['checkSource']).toBe('CopyFromVehicle')
    expect(posted!['copyChecksFromVehicleId']).toBe(BT53.vehicleId)
    expect(posted!['selectedCheckNames']).toEqual(['Oil filler cap'])
  })
})
