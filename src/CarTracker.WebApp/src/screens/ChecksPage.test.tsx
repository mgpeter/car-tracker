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
import { ChecksPage } from './ChecksPage'

const check = (over: Record<string, unknown> = {}) => ({
  checkDefinitionId: 1,
  name: 'Oil filler cap underside',
  cadenceLabel: 'Weekly',
  intervalDays: 7,
  lastPerformedOn: '2026-06-18',
  nextDue: '2026-06-25',
  daysRemaining: -19,
  status: 'Overdue',
  ...over,
})

/** The workbook's shape: 18 defined, and the Dashboard counts 17 because one has never been logged. */
const CHECKS = {
  okCount: 1,
  dueSoonCount: 1,
  overdueCount: 1,
  neverLoggedCount: 1,
  totalCount: 4,
  checks: [
    check({ checkDefinitionId: 4, name: 'Spare tyre pressure', cadenceLabel: 'Monthly', intervalDays: 30, lastPerformedOn: null, nextDue: null, daysRemaining: null, status: 'NeverLogged' }),
    check({ checkDefinitionId: 3, name: 'Engine oil level', cadenceLabel: 'Monthly', intervalDays: 30, daysRemaining: 40, status: 'Ok' }),
    check({ checkDefinitionId: 2, name: 'Battery terminals', cadenceLabel: 'Monthly', intervalDays: 30, daysRemaining: 4, status: 'DueSoon' }),
    check(),
  ],
}

let posted: unknown = null

function mockApi(body: unknown = CHECKS) {
  posted = null
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_url: string | URL, init?: RequestInit) => {
      if (init?.method === 'POST') {
        posted = JSON.parse(String(init.body))
        return new Response(JSON.stringify({ logged: 1 }), { status: 201, headers: { 'Content-Type': 'application/json' } })
      }
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

const renderPage = () =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={['/bt53akj/checks']}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <Routes>
                  <Route path="/:reg/checks" element={<VehicleProvider><ChecksPage /></VehicleProvider>} />
                </Routes>
              </div>
            </LinkProvider>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )

describe('the fourth state', () => {
  it('puts every definition in exactly one bucket', async () => {
    renderPage()
    await screen.findByText('Spare tyre pressure')
    // The workbook's Dashboard counts 17 of 18: "Spare tyre pressure" has never been logged and falls out of
    // its OK/due-soon/overdue buckets. The sum is printed so a recurrence would be visible, not silent.
    const foot = document.querySelector('.cfoot') as HTMLElement
    expect(foot.textContent).toMatch(/1 \+ 1 \+ 1 \+ 1 = 4/)
    expect(foot.textContent).toMatch(/every definition is in exactly one bucket/)
  })

  it('renders never-logged on the due axis, not the integrity one', async () => {
    renderPage()
    await screen.findByText('Spare tyre pressure')
    // The design's DETECTORS panel lists "Check never logged" as a data-integrity flag while its checks screen
    // renders it as a due tile. The tile is right: whether a check has ever been done is a fact about the car,
    // not about the data. Blue stays on the integrity axis.
    const tile = [...document.querySelectorAll('.tile')].find((t) => /Never logged/.test(t.textContent ?? ''))
    expect(tile).toBeDefined()
    expect(tile).not.toHaveClass('info')
    expect(tile).toHaveClass('never')
  })

  it('orders it after due-soon, because absence is not urgency', async () => {
    renderPage()
    await screen.findByText('Spare tyre pressure')
    const names = [...document.querySelectorAll('.clist .cname')].map((n) => n.textContent)
    expect(names[0]).toMatch(/Oil filler cap/) // overdue
    expect(names[1]).toMatch(/Battery terminals/) // due soon
    expect(names[2]).toMatch(/Spare tyre pressure/) // never logged
    expect(names[3]).toMatch(/Engine oil level/) // ok
  })

  it('says a never-logged check has no log rather than a date', async () => {
    renderPage()
    await screen.findByText('Spare tyre pressure')
    expect(screen.getByText('every 30 days · no log yet')).toBeInTheDocument()
    // Two carriers, deliberately: the countdown cell and the badge. Colour is the third carrier of state and
    // never the first, so both say it in words — which is also why this has to be scoped.
    const row = [...document.querySelectorAll('.clist li')].find((l) => /Spare tyre/.test(l.textContent ?? '')) as HTMLElement
    expect(within(row).getByText('never logged')).toBeInTheDocument()
    expect(within(row).getByText('Never logged')).toBeInTheDocument()
  })
})

describe('logging', () => {
  it('logs a batch in one action', async () => {
    renderPage()
    const user = userEvent.setup()
    // Five weekly walk-around checks done in one go is one action; making it five is how a log stops getting
    // kept. The design's "Mark done" fires a toast and writes nothing.
    await user.click(await screen.findByRole('button', { name: /log 2 due/i }))
    await user.clear(screen.getByLabelText(/Performed on/))
    await user.type(screen.getByLabelText(/Performed on/), '2026-07-14')
    await user.click(screen.getByRole('button', { name: /log all 2/i }))

    await vi.waitFor(() => expect(posted).not.toBeNull())
    expect(posted).toMatchObject({ checkDefinitionIds: [1, 2], performedOn: '2026-07-14' })
  })

  it('keeps no-verdict distinct from an explicit OK', async () => {
    renderPage()
    const user = userEvent.setup()
    await user.click((await screen.findAllByRole('button', { name: /^log$/i }))[0]!)
    // Null is the ordinary "did it, all fine" the batch uses. An explicit OK is a verdict someone reached.
    const options = [...screen.getByLabelText(/Result/).querySelectorAll('option')].map((o) => o.textContent)
    expect(options).toEqual(['Logged, no verdict', 'OK', 'Attention', 'Failed'])
  })
})

describe('empty', () => {
  it('explains itself instead of showing four zeros', async () => {
    mockApi({ okCount: 0, dueSoonCount: 0, overdueCount: 0, neverLoggedCount: 0, totalCount: 0, checks: [] })
    renderPage()
    expect(await screen.findByText(/four zero tiles would say "all clear"/)).toBeInTheDocument()
  })
})

describe('accessibility', () => {
  it('has no axe violations', async () => {
    const { container } = renderPage()
    await screen.findByText('Spare tyre pressure')
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no axe violations with the sheet open', async () => {
    const { container } = renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /log 2 due/i }))
    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('the shared table', () => {
  it('is not used here', async () => {
    renderPage()
    await screen.findByText('Spare tyre pressure')
    // Checks are a list, not a log: no dates, no figures, no columns worth aligning. Forcing the third
    // consumer to be a fourth would have been the wrong abstraction spreading.
    expect(document.querySelector('.dt')).toBeNull()
    expect(document.querySelector('.clist')).not.toBeNull()
  })
})
