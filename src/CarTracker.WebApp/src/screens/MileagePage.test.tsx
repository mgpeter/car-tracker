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
import { MileagePage } from './MileagePage'

const CLEAN = {
  derived: { currentMileage: 80_712, asOfDate: '2026-07-10', milesSincePurchase: 4080, hasNonMonotonicHistory: false, highestRecordedMileage: 80_712 },
  readings: [
    { id: 1, readingDate: '2026-03-14', mileage: 76_632, origin: 'Purchase', notes: null },
    { id: 2, readingDate: '2026-07-10', mileage: 80_712, origin: 'Fuel', notes: null },
  ],
}

/**
 * The 83,000 mi row. The workbook's Service History dates it 27 Jun 2026, above a current 80,712 — almost
 * certainly 80,300 mistyped. `MAX(mileage)` would make the typo the odometer forever.
 */
const FLAGGED = {
  derived: { currentMileage: 80_712, asOfDate: '2026-07-10', milesSincePurchase: 4080, hasNonMonotonicHistory: true, highestRecordedMileage: 83_000 },
  readings: [
    ...CLEAN.readings,
    { id: 3, readingDate: '2026-06-27', mileage: 83_000, origin: 'Service', notes: 'cambelt' },
  ],
}

function mockApi(body: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })),
  )
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
          <MemoryRouter initialEntries={['/bt53akj/mileage']}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <Routes>
                  <Route path="/:reg/mileage" element={<VehicleProvider><MileagePage /></VehicleProvider>} />
                </Routes>
              </div>
            </LinkProvider>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )

describe('the 83,000 mi row', () => {
  it('does not become the odometer', async () => {
    mockApi(FLAGGED)
    renderPage()
    await screen.findByText('Highest recorded')
    // The sharpest rule in the project: current mileage is the newest reading BY DATE, not the largest. The
    // 83,000 is dated 27 Jun, before the 10 Jul reading of 80,712 — so it is on record and it is not current.
    // Scoped to each figure's own tile: both numbers also appear in the flag's prose below, which is the
    // point of the flag.
    const kvs = [...document.querySelectorAll('.stats .kv')] as HTMLElement[]
    const current = kvs.find((k) => /^Current/.test(k.textContent ?? ''))!
    const highest = kvs.find((k) => /^Highest/.test(k.textContent ?? ''))!
    expect(within(current).getByText('80,712')).toBeInTheDocument()
    expect(within(highest).getByText('83,000')).toBeInTheDocument()
  })

  it('flags it rather than deleting it', async () => {
    mockApi(FLAGGED)
    renderPage()
    expect(await screen.findByText('A reading is above the current odometer')).toBeInTheDocument()
    expect(screen.getByText(/flagged and kept/)).toBeInTheDocument()
    // Which of the two is wrong is not ours to guess — §5.3 says flag, never silently accept, and never
    // silently drop either.
    expect(screen.getByText(/not ours to guess/)).toBeInTheDocument()
    expect(screen.getByText('Above current')).toBeInTheDocument()
  })

  it('says nothing when the history is clean', async () => {
    mockApi(CLEAN)
    renderPage()
    await screen.findByText('76,632')
    expect(screen.queryByText('A reading is above the current odometer')).not.toBeInTheDocument()
    expect(screen.queryByText('Above current')).not.toBeInTheDocument()
    expect(screen.getByText('agrees with the current reading')).toBeInTheDocument()
  })
})

describe('readings', () => {
  it('names where each came from', async () => {
    mockApi(CLEAN)
    renderPage()
    await screen.findByText('76,632')
    // Most readings are written by another log rather than typed — a fill, a service, an expense. Saying so
    // is what makes the log legible: 13 rows nobody typed would otherwise look like an import.
    expect(screen.getByText('from a fill')).toBeInTheDocument()
    // The founding reading, and distinct from a typed one: BT53's rendered the raw enum name until the live
    // data showed it, because the map was hand-guessed and had no Purchase in it.
    expect(screen.getByText('bought at')).toBeInTheDocument()
  })

  it('warns without blocking a backdated reading', async () => {
    mockApi(CLEAN)
    renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /add reading/i }))
    await user.type(screen.getByLabelText(/Odometer/), '80000')

    // Not a validation error. A reading below the current odometer is often perfectly correct — a backdated
    // entry — which is exactly why the app must not decide it is wrong.
    expect(screen.getByText(/If this is a backdated reading that is fine/)).toBeInTheDocument()
    expect(screen.getByText(/Either way it saves/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /save reading/i })).toBeEnabled()
  })
})

describe('accessibility', () => {
  it('has no axe violations', async () => {
    mockApi(FLAGGED)
    const { container } = renderPage()
    await screen.findByText('Highest recorded')
    expect(await axe(container)).toHaveNoViolations()
  })
})
