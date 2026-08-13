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

/** The 83,000 mi row as the integrity queue flags it. `entityId` is reading #3 — the Service-origin one. */
const MILEAGE_FLAG = {
  id: 5,
  kind: 'MileageNonMonotonic',
  severity: 'Error',
  entityType: 'MileageReading',
  entityId: 3,
  message:
    'Reading of 83,000 mi on 27 Jun 2026 is above the current 80,712 mi from 10 Jul 2026. An odometer only advances, so this reading cannot be right.',
  detail: '{"mileage":83000,"currentMileage":80712}',
  status: 'Open',
  resolvedAt: null,
  resolutionNote: null,
  createdAt: '2026-07-16T09:00:00Z',
}

/** URL-aware: the page reads the log and, when a flag sent the reader here, the integrity queue too. */
function mockApi(body: unknown, anomalies: unknown[] = []) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL) =>
      new Response(JSON.stringify(String(url).includes('/anomalies') ? anomalies : body), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
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

const renderPage = (path = '/bt53akj/mileage') =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={[path]}>
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
    await screen.findByText('Highest recorded')
    expect(screen.queryByText('A reading is above the current odometer')).not.toBeInTheDocument()
    expect(screen.queryByText('Above current')).not.toBeInTheDocument()
    expect(screen.getByText('agrees with the current reading')).toBeInTheDocument()
  })
})

describe('readings', () => {
  it('names where each came from', async () => {
    mockApi(CLEAN)
    renderPage()
    await screen.findByText('Highest recorded')
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

describe('search', () => {
  it('finds a reading by its note and by the origin label the column shows', async () => {
    mockApi(FLAGGED)
    const user = userEvent.setup()
    renderPage()
    await screen.findByText('Highest recorded')

    const box = screen.getByRole('searchbox', { name: 'Search' })
    await user.type(box, 'cambelt')
    expect(document.querySelector('.tctl-count')?.textContent).toMatch(/1 of 3/)

    // The origin is searched by its rendered label, not the wire enum: the column says "from a fill", so
    // typing "fill" must work and typing "Fuel" is not what the reader can see.
    await user.clear(box)
    await user.type(box, 'from a fill')
    expect(document.querySelector('.tctl-count')?.textContent).toMatch(/1 of 3/)
  })
})

describe('arriving from the integrity queue', () => {
  it('does not open a sheet for a mirrored reading, and says where its fix lives', async () => {
    // BT53's real flag. The 83,000 mi row is Service-origin, so it is read-only here — a mirrored reading is
    // corrected at its source or the two disagree. Nothing links a reading back to the record that wrote it,
    // and matching by date and mileage would be a guess, so the screen names the log rather than the row.
    mockApi(FLAGGED, [MILEAGE_FLAG])
    renderPage('/bt53akj/mileage?flag=5')

    expect(await screen.findByText(/was written by a service record and is read-only here/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Service history/ })).toHaveAttribute('href', '/bt53akj/service')
    expect(screen.queryByRole('dialog')).toBeNull()

    // Still marked, so the reader can see which row the queue meant.
    expect(document.querySelector('.dt-row.is-fix')).not.toBeNull()

    // ONE callout carrying the redirect, not two stacked ones. The first cut put the flag's message in a
    // `.fixban` and the "corrected at its source" sentence in a separate `.fixnote` below it — two blue boxes
    // of different widths, the first telling you to correct the row below and the second saying you cannot.
    // The standing "not monotonic" panel is the page's own statement and stays; the arrival banner is one box.
    const callouts = [...document.querySelectorAll('.attn.attn-info')]
    expect(callouts).toHaveLength(2)
    const banner = callouts.find((c) => c.textContent?.includes('Fixing a flagged row'))!
    expect(within(banner as HTMLElement).getByRole('link', { name: /Service history/ })).toBeInTheDocument()
    // The default line would be false here: the row below is exactly what cannot be corrected.
    expect(banner.textContent).not.toMatch(/Correct the row below/)
  })

  it('keeps the above-current flag on the same row as the fix highlight', async () => {
    mockApi(FLAGGED, [MILEAGE_FLAG])
    renderPage('/bt53akj/mileage?flag=5')
    await screen.findByText(/read-only here/)

    // The reading above the odometer IS the one the queue sends you to, so both classes land on one row.
    // `.is-fix` adds an outline on top of the stripe rather than replacing it — colour is never the only
    // carrier, and here it could not tell the two apart at all.
    expect(document.querySelector('.dt-row.is-flagged.is-fix')).not.toBeNull()
  })

  it('opens a typed reading for edit, because that one can be corrected here', async () => {
    const typed = { id: 4, readingDate: '2026-06-28', mileage: 84_000, origin: 'Manual', notes: null }
    mockApi(
      { ...FLAGGED, readings: [...FLAGGED.readings, typed] },
      [{ ...MILEAGE_FLAG, entityId: 4 }],
    )
    renderPage('/bt53akj/mileage?flag=5')

    const sheet = await screen.findByRole('dialog')
    expect(within(sheet).getByLabelText(/Odometer/)).toHaveValue('84000')
    // No redirect notice: this row is not a mirror, so there is nowhere else to send anyone.
    expect(screen.queryByText(/read-only here/)).toBeNull()
  })
})
