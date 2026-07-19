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
import { ServiceHistoryPage } from './ServiceHistoryPage'

const MOT_RECORD = {
  id: 1,
  serviceDate: '2026-07-08',
  type: 'MOT',
  mileage: 80_705,
  garage: 'K & P Motors',
  workDone: null,
  partsReplaced: null,
  cost: 54.85,
  nextDueDate: '2027-07-08',
  nextDueMileage: null,
  notes: 'advisories: headlamp lens, rear tyres',
}

/** BT53 with its MOT logged: the countdown the workbook stored as 6 Aug 2026 / 23 days / red. */
const LOG = {
  records: [MOT_RECORD],
  mot: {
    name: 'MOT',
    expiryDate: '2027-07-08',
    daysRemaining: 359,
    urgency: 'Ok',
    source: 'K & P Motors · passed 8 Jul 2026',
  },
  nextServiceDate: { name: 'Next service', expiryDate: null, daysRemaining: null, urgency: null, source: null },
  nextServiceMiles: null,
}

/** Before the pass is logged: the state BT53 is actually in. */
const EMPTY = {
  records: [],
  mot: { name: 'MOT', expiryDate: null, daysRemaining: null, urgency: null, source: null },
  nextServiceDate: { name: 'Next service', expiryDate: null, daysRemaining: null, urgency: null, source: null },
  nextServiceMiles: null,
}

let posted: unknown = null

function mockApi(body: unknown = LOG) {
  posted = null
  vi.stubGlobal(
    'fetch',
    vi.fn(async (_url: string | URL, init?: RequestInit) => {
      if (init?.method === 'POST') {
        posted = JSON.parse(String(init.body))
        return new Response(JSON.stringify({ id: 9, flags: [] }), { status: 201, headers: { 'Content-Type': 'application/json' } })
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
          <MemoryRouter initialEntries={['/bt53akj/service']}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <Routes>
                  <Route path="/:reg/service" element={<VehicleProvider><ServiceHistoryPage /></VehicleProvider>} />
                </Routes>
              </div>
            </LinkProvider>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )

describe('the MOT derivation', () => {
  it('names the record the countdown comes from', async () => {
    renderPage()
    await screen.findByText('The MOT expiry is computed, not stored')
    // The workbook stored 6 Aug 2026 and showed a red 23-day countdown for a test already passed. Pointing at
    // the source record is the difference between a derived figure and a claim.
    expect(screen.getByText(/comes from the MOT record dated 8 Jul 2026 at 80,705 mi/)).toBeInTheDocument()
    expect(screen.getByText('derives the MOT expiry')).toBeInTheDocument()
  })

  it('is honest that there is nothing to derive from yet', async () => {
    mockApi(EMPTY)
    renderPage()
    // BT53's actual state until this screen shipped: the MOT reads "Not set" because nothing could create the
    // record, not because the test never happened.
    expect(await screen.findByText(/until one exists it reads "Not set"/)).toBeInTheDocument()
    expect(screen.queryByText('The MOT expiry is computed, not stored')).not.toBeInTheDocument()
  })
})

describe('the add sheet', () => {
  it('offers MOT as a choice rather than trusting it to be typed', async () => {
    renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add record/i }))

    // ServiceRecord.Type is free text and the derivation matches "MOT" exactly — so "MOT test" or "mot"
    // derives no expiry at all, and the failure is silent. A select removes the trap.
    const type = screen.getByLabelText(/Type/)
    expect(type.tagName).toBe('SELECT')
    const options = [...type.querySelectorAll('option')].map((o) => o.textContent)
    expect(options).toContain('MOT')
  })

  it('renames the next-due field when the type is MOT', async () => {
    renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add record/i }))
    await user.selectOptions(screen.getByLabelText(/Type/), 'MOT')

    // On an MOT the field is not "next due", it is the expiry the dashboard counts to.
    expect(screen.getByLabelText(/MOT expires/)).toBeInTheDocument()
    expect(screen.getByText(/this is the dashboard countdown/)).toBeInTheDocument()
  })

  it('pre-fills next-due date and mileage from a recognised type', async () => {
    renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add record/i }))

    await user.type(screen.getByLabelText(/^Date/), '2026-07-18')
    await user.selectOptions(screen.getByLabelText(/Type/), 'Service')
    await user.type(screen.getByLabelText(/Odometer/), '80000')

    // Service is 12 months / 12,000 mi — so the owner does not retype the interval every time.
    expect(screen.getByLabelText('Next due')).toHaveValue('2027-07-18')
    expect(screen.getByLabelText('Next due at')).toHaveValue('92000')
  })

  it('suggests nothing for a type with no service interval', async () => {
    renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add record/i }))

    await user.type(screen.getByLabelText(/^Date/), '2026-07-18')
    await user.selectOptions(screen.getByLabelText(/Type/), 'Repair')
    await user.type(screen.getByLabelText(/Odometer/), '80000')

    // A repair has no recurring next-due, so the fields stay empty rather than inventing a schedule.
    expect(screen.getByLabelText('Next due')).toHaveValue('')
    expect(screen.getByLabelText('Next due at')).toHaveValue('')
  })

  it('lets the suggestion be overwritten, and stores what was saved not the template', async () => {
    renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add record/i }))

    await user.type(screen.getByLabelText(/^Date/), '2026-07-18')
    await user.selectOptions(screen.getByLabelText(/Type/), 'Service')
    await user.type(screen.getByLabelText(/Odometer/), '80000')

    // Overwrite the suggested mileage; the suggested date is left as-is.
    await user.clear(screen.getByLabelText('Next due at'))
    await user.type(screen.getByLabelText('Next due at'), '95000')

    // Changing the odometer now must NOT reset the next-due back to the template — the owner has taken it over.
    await user.clear(screen.getByLabelText(/Odometer/))
    await user.type(screen.getByLabelText(/Odometer/), '80100')

    await user.click(screen.getByRole('button', { name: /save record/i }))

    await vi.waitFor(() => expect(posted).not.toBeNull())
    expect(posted).toMatchObject({
      type: 'Service',
      nextDueDate: '2027-07-18', // the accepted suggestion
      nextDueMileage: 95_000, // the owner's override, not the 92,100 the template would now give
    })
  })

  it('posts the record with its next-due date', async () => {
    renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add record/i }))
    await user.type(screen.getByLabelText(/^Date/), '2026-07-08')
    await user.selectOptions(screen.getByLabelText(/Type/), 'MOT')
    await user.type(screen.getByLabelText(/Odometer/), '80705')
    // The MOT template (12 months) fills the expiry from the service date, so it need not be retyped — and the
    // saved record still carries it.
    expect(screen.getByLabelText(/MOT expires/)).toHaveValue('2027-07-08')
    await user.click(screen.getByRole('button', { name: /save record/i }))

    await vi.waitFor(() => expect(posted).not.toBeNull())
    expect(posted).toMatchObject({
      serviceDate: '2026-07-08',
      type: 'MOT',
      mileage: 80_705,
      nextDueDate: '2027-07-08',
    })
  })
})

describe('accessibility', () => {
  it('has no axe violations', async () => {
    const { container } = renderPage()
    await screen.findByText('The MOT expiry is computed, not stored')
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no axe violations with the sheet open', async () => {
    const { container } = renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add record/i }))
    expect(await axe(container)).toHaveNoViolations()
  })
})
