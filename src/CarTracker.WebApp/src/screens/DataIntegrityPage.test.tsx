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
import { DataIntegrityPage } from './DataIntegrityPage'

/** The 83,000 mi row, as the detector raises it on BT53's real history. */
const MILEAGE_FLAG = {
  id: 1,
  kind: 'MileageNonMonotonic',
  severity: 'Error',
  // `nameof(MileageReading)` — the detector flags the reading, not the service record that wrote it. This
  // fixture said `ServiceRecord`, which no detector has ever produced, and the fix link checks the type.
  entityType: 'MileageReading',
  entityId: 4,
  // The real shapes, checked against a live flag. Message is the detector's prose and names both figures;
  // Detail is the machine-readable pair, for tooling and MCP. The first version of this fixture had prose in
  // Detail, so the screen rendered raw JSON in the browser and this suite stayed green.
  message: 'Reading of 83,000 mi on 27 Jun 2026 is above the current 80,712 mi from 10 Jul 2026. An odometer only advances, so this reading cannot be right.',
  detail: '{"mileage": 83000, "currentMileage": 80712}',
  status: 'Open',
  resolvedAt: null,
  resolutionNote: null,
  createdAt: '2026-07-16T09:00:00Z',
}

/**
 * The fourth detector, which shipped 2026-08-07 with no entry in the screen's `KIND` map — so its rows
 * rendered the raw message as their title, printed it again below, and left the explanation blank. This is
 * BT53's real one.
 */
const EQUIPMENT_FLAG = {
  id: 3,
  kind: 'EquipmentCostWithoutDate',
  severity: 'Warning',
  entityType: 'EquipmentItem',
  entityId: 14,
  message:
    "'Engine oil (5 L, 10W-40)' has a cost of £30.00 but no purchase date, so it counts toward no total — not spend, not cost-per-mile, not the Equipment & Tools budget. Add the date it was bought and the money lands where it belongs.",
  detail: '{"cost":30.00}',
  status: 'Open',
  resolvedAt: null,
  resolutionNote: null,
  createdAt: '2026-08-12T09:00:00Z',
}

const RESOLVED = {
  ...MILEAGE_FLAG,
  id: 2,
  status: 'Accepted',
  resolvedAt: '2026-07-16T10:00:00Z',
  resolutionNote: '80,300 mistyped',
}

let patched: unknown = null

function mockApi(items: unknown[] = [MILEAGE_FLAG]) {
  patched = null
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL, init?: RequestInit) => {
      if (init?.method === 'PATCH') {
        patched = JSON.parse(String(init.body))
        return new Response(JSON.stringify({ ...MILEAGE_FLAG, status: 'Accepted' }), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      const all = String(url).includes('status=all')
      return new Response(JSON.stringify(all ? items : items.filter((i) => (i as { status: string }).status === 'Open')), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
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

/** The row's action. The sheet has its own control matching /resolve/i, so this is scoped to the list. */
const findRowResolve = async () => {
  const list = await screen.findByRole('list')
  return within(list).getByRole('button', { name: /resolve/i })
}

const renderPage = () =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={['/bt53akj/data-integrity']}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <Routes>
                  <Route path="/:reg/data-integrity" element={<VehicleProvider><DataIntegrityPage /></VehicleProvider>} />
                </Routes>
              </div>
            </LinkProvider>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )

describe('a flag', () => {
  it('shows the two figures that disagree, not just its message', async () => {
    renderPage()
    // The detector's prose names both figures. Detail is JSON and must never reach the page.
    expect(await screen.findByText(/Reading of 83,000 mi on 27 Jun 2026 is above the current 80,712 mi/)).toBeInTheDocument()
    expect(screen.getByText('A reading is above a later one')).toBeInTheDocument()
    expect(document.body.textContent).not.toMatch(/\{"mileage"/)
  })

  it('says the app has not changed anything', async () => {
    renderPage()
    // Flag, never block, and never silently correct: which of the two figures is wrong is not ours to guess.
    expect(await screen.findByText(/not ours to guess/)).toBeInTheDocument()
    expect(screen.getByText(/nothing has been changed/)).toBeInTheDocument()
  })

  it('stays on the blue axis, never a due tone', async () => {
    renderPage()
    await screen.findByText('A reading is above a later one')
    // Integrity is its own axis. The design's DETECTORS panel conflates it with due-status by listing "Check
    // never logged" here; that one is CheckStatus.NeverLogged and stays on the due axis.
    expect(document.querySelector('.pill.info')).not.toBeNull()
    expect(document.querySelector('.pill.due')).toBeNull()
    expect(document.querySelector('.pill.soon')).toBeNull()
    expect(document.querySelector('.pill.ok')).toBeNull()
  })
})

describe('resolving', () => {
  it('distinguishes Corrected from Accepted, because only one re-raises', async () => {
    renderPage()
    const user = userEvent.setup()
    await user.click(await findRowResolve())

    const options = [...screen.getByLabelText(/Resolution/).querySelectorAll('option')].map((o) => o.textContent)
    expect(options).toEqual(['Corrected', 'Accepted', 'Dismissed'])
    // The distinction is the lifecycle: "I fixed it" is a claim the detector re-checks; "I know, and it is
    // fine" is a decision, and re-asking would make the queue a nag.
    await user.selectOptions(screen.getByLabelText(/Resolution/), 'Corrected')
    expect(screen.getByText(/retracts its own flag on the next write/)).toBeInTheDocument()
    await user.selectOptions(screen.getByLabelText(/Resolution/), 'Accepted')
    expect(screen.getByText(/It stays down/)).toBeInTheDocument()
  })

  it('never offers Open as a resolution', async () => {
    renderPage()
    const user = userEvent.setup()
    await user.click(await findRowResolve())
    const options = [...screen.getByLabelText(/Resolution/).querySelectorAll('option')].map((o) => o.textContent)
    // The API rejects it and the check constraint would reject the row. Offering it would be a 400 by design.
    expect(options).not.toContain('Open')
  })

  it('posts the decision and the reason', async () => {
    renderPage()
    const user = userEvent.setup()
    await user.click(await findRowResolve())
    await user.type(screen.getByLabelText(/Note/), '80,300 mistyped')
    await user.click(screen.getByRole('button', { name: /mark accepted/i }))

    await vi.waitFor(() => expect(patched).not.toBeNull())
    expect(patched).toEqual({ status: 'Accepted', resolutionNote: '80,300 mistyped' })
  })
})

describe('the queue', () => {
  it('shows work to do, not history, by default', async () => {
    mockApi([MILEAGE_FLAG, RESOLVED])
    renderPage()
    await screen.findByText('Open flags')
    // Resolved flags are an audit question, asked deliberately via ?status=all.
    expect(document.querySelectorAll('.ilist li')).toHaveLength(1)
  })

  it('keeps a resolved flag and its reason rather than deleting it', async () => {
    mockApi([MILEAGE_FLAG, RESOLVED])
    renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /show resolved/i }))

    expect(await screen.findByText(/Accepted · 16 Jul 2026 · "80,300 mistyped"/)).toBeInTheDocument()
    expect(document.querySelector('.ilist li.is-resolved')).not.toBeNull()
  })

  it('says nothing is flagged rather than showing an empty list', async () => {
    mockApi([])
    renderPage()
    expect(await screen.findByText(/Nothing flagged\. The four detectors run on every write/)).toBeInTheDocument()
  })
})

describe('the fourth detector', () => {
  it('reads as prose, not as its own message twice', async () => {
    mockApi([EQUIPMENT_FLAG])
    renderPage()

    // The title is the kind's own words. Before the KIND map gained this entry it fell back to `message`,
    // which then appeared a second time in the comparison block below it.
    expect(await screen.findByText('Money the app holds and counts nowhere')).toBeInTheDocument()
    expect(screen.getAllByText(EQUIPMENT_FLAG.message)).toHaveLength(1)
    // And the "why" paragraph rendered empty, because `KIND[kind]?.why ?? ''` has no complaint to make.
    expect(screen.getByText(/Adding the date is the whole fix/)).toBeInTheDocument()
  })
})

describe('fixing a flag', () => {
  it('links to the screen that owns the row, carrying the flag id', async () => {
    mockApi([EQUIPMENT_FLAG])
    renderPage()

    // The flag's id, not the row's: the target screen checks the flag's entityType against its own before
    // opening anything, so a link that points at the wrong kind of row opens nothing rather than the wrong one.
    const fix = await screen.findByRole('link', { name: /fix this/i })
    expect(fix).toHaveAttribute('href', '/bt53akj/equipment?flag=3')
  })

  it('sends a mileage flag to the mileage log', async () => {
    renderPage()
    expect(await screen.findByRole('link', { name: /fix this/i })).toHaveAttribute(
      'href',
      '/bt53akj/mileage?flag=1',
    )
  })

  it('offers no fix on a flag that is already resolved', async () => {
    mockApi([RESOLVED])
    renderPage()
    const user = userEvent.setup()
    await user.click(await screen.findByRole('button', { name: /show resolved/i }))

    await screen.findByText(/Accepted · 16 Jul 2026/)
    expect(screen.queryByRole('link', { name: /fix this/i })).toBeNull()
  })
})

describe('accessibility', () => {
  it('has no axe violations', async () => {
    const { container } = renderPage()
    await screen.findByText('A reading is above a later one')
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no axe violations with the sheet open', async () => {
    const { container } = renderPage()
    const user = userEvent.setup()
    await user.click(await findRowResolve())
    expect(await axe(container)).toHaveNoViolations()
  })
})
