import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import type { ReactElement } from 'react'
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
import { BudgetPage } from './BudgetPage'
import { EquipmentPage } from './EquipmentPage'
import { IssuesPage } from './IssuesPage'
import { TasksPage } from './TasksPage'
import { TyresPage } from './TyresPage'
import { VehicleInfoPage } from './VehicleInfoPage'
import { WashPage } from './WashPage'

/**
 * The seven Phase 3 screens.
 *
 * One file because they share a harness and each has one decision worth pinning; splitting them into seven
 * would be seven copies of the same forty-line provider stack.
 */

function mockApi(body: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_url: string | URL, init?: RequestInit) =>
      init?.method !== undefined && init.method !== 'GET'
        ? new Response(JSON.stringify({ id: 1 }), { status: 201, headers: { 'Content-Type': 'application/json' } })
        : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    ),
  )
}

beforeEach(() => {
  __resetScrollLock()
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, media: '', addEventListener: () => {}, removeEventListener: () => {} })))
})

afterEach(() => vi.unstubAllGlobals())

const renderAt = (path: string, element: ReactElement) =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={[`/bt53akj/${path}`]}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <Routes>
                  <Route path={`/:reg/${path}`} element={<VehicleProvider>{element}</VehicleProvider>} />
                </Routes>
              </div>
            </LinkProvider>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )

// ---- tasks ---------------------------------------------------------------------------------------------

const TASKS = {
  tasks: [
    { id: 1, kind: 'Workshop', priority: 'Medium', title: 'Back window sensor', description: null, estimatedCost: 150, status: 'Open', targetDate: null, targetService: null, completedDate: null, assignedGarage: 'K & P Motors', serviceRecordId: null, notes: null },
    { id: 2, kind: 'DIY', priority: 'Low', title: 'Clean brake pipes', description: null, estimatedCost: 50, status: 'Open', targetDate: '2026-07-31', targetService: null, completedDate: null, assignedGarage: null, serviceRecordId: null, notes: null },
    { id: 3, kind: 'DIY', priority: 'High', title: 'Petrol additive', description: null, estimatedCost: 20, status: 'Done', targetDate: null, targetService: null, completedDate: '2026-07-01', assignedGarage: null, serviceRecordId: null, notes: null },
  ],
  bundleCost: 150,
  bundleCount: 1,
  openEstimateTotal: 200,
}

describe('tasks', () => {
  it('derives the bundle from the open workshop jobs only', async () => {
    mockApi(TASKS)
    renderAt('tasks', <TasksPage />)
    // The design hardcodes "Bundle for next garage visit → £150 · 1 job". It is the sum of the open Workshop
    // estimates, and its value is that it moves when you add one — the question is "is it worth booking yet".
    await screen.findByText('1 workshop job waiting')
    const stats = document.querySelector('.stats') as HTMLElement
    expect(within(stats).getByText('£150')).toBeInTheDocument()
    // The DIY £50 is open but not bundled; the Done £20 counts to neither.
    expect(within(stats).getByText('£200')).toBeInTheDocument()
  })

  it('renders High priority, which the design has no rule for', async () => {
    mockApi(TASKS)
    renderAt('tasks', <TasksPage />)
    // The design renders only Medium and Low and ships a `.prio.crit` rule nothing uses — so the domain's most
    // important priority has no representation in it at all. Scoped to the board: "High" is now also a filter
    // select option, so the assertion targets the card pill, which is what "renders High priority" means.
    const board = (await screen.findByText('Petrol additive')).closest('.board') as HTMLElement
    expect(within(board).getByText('High')).toBeInTheDocument()
  })

  it('makes a card a button, not a clickable div', async () => {
    mockApi(TASKS)
    renderAt('tasks', <TasksPage />)
    await screen.findByText('Back window sensor')
    // The design's cards are divs: unreachable by keyboard, announced as nothing, on the screen whose whole job
    // is picking one thing out of four columns.
    const card = screen.getByText('Back window sensor').closest('button')
    expect(card).not.toBeNull()
  })

  it('has no axe violations', async () => {
    mockApi(TASKS)
    const { container } = renderAt('tasks', <TasksPage />)
    await screen.findByText('Back window sensor')
    expect(await axe(container)).toHaveNoViolations()
  })
})

// ---- issues --------------------------------------------------------------------------------------------

const ISSUES = {
  issues: [
    { id: 1, title: 'Brake pipe corrosion', severity: 'Medium', firstNoted: '2024-05-01', lastChecked: '2026-07-08', currentObservation: 'surface rust, no flaking', actionIfWorsens: 'replace both pipes before the next MOT', estimatedFixCost: 150, status: 'Monitoring', resolvedDate: null, notes: null },
  ],
  monitoringCount: 1,
  resolvedCount: 0,
  worstCaseCost: 150,
}

describe('issues', () => {
  it('derives how long a thing has been watched', async () => {
    mockApi(ISSUES)
    renderAt('issues', <IssuesPage />)
    // "Advisory since 2024" is only useful if it says how long that has been — and a stored "26 months" is
    // wrong by next month.
    expect(await screen.findByText(/watched \d+ months/)).toBeInTheDocument()
  })

  it('shows the decision made in advance', async () => {
    mockApi(ISSUES)
    renderAt('issues', <IssuesPage />)
    // The most valuable field on the screen: decided calmly, so it is not decided in a hurry at the roadside.
    expect(await screen.findByText(/replace both pipes before the next MOT/)).toBeInTheDocument()
    expect(screen.getByText('If it worsens:')).toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    mockApi(ISSUES)
    const { container } = renderAt('issues', <IssuesPage />)
    await screen.findByText('Brake pipe corrosion')
    expect(await axe(container)).toHaveNoViolations()
  })
})

// ---- tyres ---------------------------------------------------------------------------------------------

const TYRES = [
  { id: 1, readingDate: '2026-05-23', mileage: 79_316, psiFrontLeft: 35, psiFrontRight: 35, psiRearLeft: 35, psiRearRight: 35, psiSpare: null, treadFrontLeft: 6, treadFrontRight: 6, treadRearLeft: 5.5, treadRearRight: 5.5, location: 'Home driveway', tool: 'Digital gauge', notes: null },
]

describe('tyres', () => {
  it('says the spare was never checked rather than showing zero', async () => {
    mockApi(TYRES)
    renderAt('tyres', <TyresPage />)
    await screen.findByText('Home driveway')
    // The workbook's eighteenth check is "Spare tyre pressure", never logged — which is why its dashboard
    // counts 17 of 18. Not measured is not flat.
    const row = document.querySelector('.dt-row:not(.dt-head)') as HTMLElement
    expect(within(row).getByText('never')).toBeInTheDocument()
    expect(within(row).queryByText('0')).not.toBeInTheDocument()
  })

  it('states the MOT tread limit next to the lowest tread', async () => {
    mockApi(TYRES)
    renderAt('tyres', <TyresPage />)
    expect(await screen.findByText(/lowest tread 5.5 mm · MOT limit 1.6 mm/)).toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    mockApi(TYRES)
    const { container } = renderAt('tyres', <TyresPage />)
    await screen.findByText('Home driveway')
    expect(await axe(container)).toHaveNoViolations()
  })
})

// ---- wash ----------------------------------------------------------------------------------------------

const WASHES = [
  { id: 1, washDate: '2026-06-02', location: 'Home driveway', washType: 'Two bucket', cost: null, mileage: null, notes: null },
  { id: 2, washDate: '2026-06-30', location: 'Home driveway', washType: 'Two bucket', cost: 8, mileage: null, notes: null },
]

describe('wash', () => {
  it('derives the cadence from the gaps', async () => {
    mockApi(WASHES)
    renderAt('wash', <WashPage />)
    // A list of dates says nothing. The gap between them is whether the 3-4 week target is being met, which on
    // a salted-road Freelander is a rust question.
    await screen.findByText('Average gap')
    const stats = document.querySelector('.stats') as HTMLElement
    expect(within(stats).getByText('28 days')).toBeInTheDocument()
  })

  it('refuses to invent a cadence from one wash', async () => {
    mockApi([WASHES[0]])
    renderAt('wash', <WashPage />)
    await screen.findByText('Average gap')
    // "0 days" from a single row would be a made-up figure.
    expect(screen.getByText('needs a second wash')).toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    mockApi(WASHES)
    const { container } = renderAt('wash', <WashPage />)
    await screen.findByText('Cadence')
    expect(await axe(container)).toHaveNoViolations()
  })
})

// ---- budget --------------------------------------------------------------------------------------------

const BUDGET = {
  totalBudget: 1000,
  totalActual: 1580,
  lines: [
    { category: 'Tools/Equipment', annualBudget: 100, actualSpend: 158, remaining: -58, percentUsed: 158, isOverBudget: true },
    { category: 'Service', annualBudget: 900, actualSpend: 604, remaining: 296, percentUsed: 67.1, isOverBudget: false },
    { category: 'Fuel', annualBudget: null, actualSpend: 818, remaining: null, percentUsed: null, isOverBudget: false },
  ],
}

describe('budget', () => {
  it('caps an over-budget bar but never the figure beside it', async () => {
    mockApi(BUDGET)
    renderAt('budget', <BudgetPage />)
    await screen.findByText(/158% of £100.00/)
    // A bar at 158% draws outside its track; a bar clamped to 100% with no figure says "at the limit" when the
    // truth is half as much again. The percentage carries it.
    expect(screen.getByText(/158% of £100.00 · £58.00 over/)).toBeInTheDocument()
    const bar = document.querySelector('.track i.over') as HTMLElement
    expect(bar.style.width).toBe('100%')
  })

  it('does not render a missing target as a target of zero', async () => {
    mockApi(BUDGET)
    renderAt('budget', <BudgetPage />)
    await screen.findByText(/· no target/)
    // The dashboard left its budget figures out rather than derive them from nothing. Same rule: absent is not
    // zero, and a category nobody budgeted gets no bar at all.
    expect(screen.getByText(/· no target/)).toBeInTheDocument()
    const rows = [...document.querySelectorAll('.bars-list li')]
    const fuel = rows.find((r) => r.textContent?.startsWith('Fuel'))!
    expect(fuel.querySelector('.track')).toBeNull()
  })

  it('has no axe violations', async () => {
    mockApi(BUDGET)
    const { container } = renderAt('budget', <BudgetPage />)
    await screen.findByText(/158% of £100.00/)
    expect(await axe(container)).toHaveNoViolations()
  })
})

// ---- equipment -----------------------------------------------------------------------------------------

const EQUIPMENT = [
  { id: 1, name: 'Scissor jack', category: 'Recovery', purchasedDate: '2026-04-02', sourceVendor: 'Halfords', cost: 24.99, storedAt: 'Boot floor', status: 'Owned', notes: null },
  { id: 2, name: 'Tow rope', category: 'Recovery', purchasedDate: null, sourceVendor: null, cost: null, storedAt: null, status: 'ToOrder', notes: null },
]

describe('equipment', () => {
  it('separates owning from ordering', async () => {
    mockApi(EQUIPMENT)
    renderAt('equipment', <EquipmentPage />)
    await screen.findByText('Scissor jack')
    // A fourth axis: existence, not urgency and not integrity. "To order" is a shopping list, not a task.
    // Scoped to the list — the stat tiles use the same words as labels, which is the point of them.
    const list = document.querySelector('.eqlist') as HTMLElement
    expect(within(list).getByText('Owned')).toBeInTheDocument()
    expect(within(list).getByText('To order')).toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    mockApi(EQUIPMENT)
    const { container } = renderAt('equipment', <EquipmentPage />)
    await screen.findByText('Scissor jack')
    expect(await axe(container)).toHaveNoViolations()
  })
})

// ---- vehicle info --------------------------------------------------------------------------------------

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
  fluids: { oilSpec: '10W-40 semi-synthetic', oilCapacityLitres: 4.5, coolantSpec: 'OAT red/pink', coolantCapacityLitres: 7, brakeFluidSpec: null, transmissionOilSpec: null, sparkPlugPart: null, oilFilterPart: null, airFilterPart: null, fuelFilterPart: null, cabinFilterPart: null },
  tyres: { tyreSize: '195/80 R15', pressureFrontPsi: 30, pressureRearPsi: 35, pressureFrontLadenPsi: null, pressureRearLadenPsi: null, minTreadMm: 3 },
  insurance: { insurer: 'Admiral', policyNumber: 'P77904683', periodStart: null, periodEnd: null, coverType: 'Comprehensive', premium: 517.14, excessCompulsory: 250, excessVoluntary: null, ncbYears: 0 },
  breakdown: { provider: null, policyNumber: null, expiry: null },
  notes: null,
}

describe('vehicle info', () => {
  it('is explicit that its dates are inputs, not countdowns', async () => {
    mockApi(VEHICLE)
    renderAt('vehicle-info', <VehicleInfoPage />)
    // Two places showing "243 days" is two places to disagree. The countdown lives on the dashboard, derived.
    expect(await screen.findByText(/Their countdowns are not/)).toBeInTheDocument()
    expect(screen.getByText('Inputs only')).toBeInTheDocument()
  })

  it('carries the coolant rule the head gasket depends on', async () => {
    mockApi(VEHICLE)
    renderAt('vehicle-info', <VehicleInfoPage />)
    // The K-series frailty is why this field is worth a screen: OAT only, never mixed with IAT.
    expect(await screen.findByText(/OAT only, never mixed with IAT/)).toBeInTheDocument()
  })

  it('drops a spec row it has nothing for', async () => {
    mockApi(VEHICLE)
    renderAt('vehicle-info', <VehicleInfoPage />)
    await screen.findByText('Engine oil')
    // An empty spec row implies the manual said nothing. Absent is the honest rendering.
    expect(screen.queryByText('VIN')).not.toBeInTheDocument()
    expect(screen.queryByText('Brake fluid')).not.toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    mockApi(VEHICLE)
    const { container } = renderAt('vehicle-info', <VehicleInfoPage />)
    await screen.findByText('Engine oil')
    expect(await axe(container)).toHaveNoViolations()
  })
})
