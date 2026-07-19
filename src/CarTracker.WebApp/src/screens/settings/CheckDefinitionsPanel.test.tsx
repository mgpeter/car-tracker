import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../../api/queries'
import { IconSprite } from '../../components/IconSprite'
import { LinkProvider } from '../../lib/link'
import { __resetScrollLock } from '../../lib/useScrollLock'
import { ToastProvider } from '../../shell/Toast'
import { ThemeProvider } from '../../theme/ThemeProvider'
import { CheckDefinitionsPanel } from './CheckDefinitionsPanel'

/** The vehicle already has "Engine oil level" — one of the generic names, so it must lock. */
const EXISTING = [
  { id: 1, name: 'Engine oil level', cadenceLabel: 'Monthly', intervalDays: 30, guidance: null, displayOrder: 1, isActive: true },
]

const STARTER = [
  { name: 'Walk-around', cadenceLabel: 'Weekly', intervalDays: 7, guidance: null },
  { name: 'Engine oil level', cadenceLabel: 'Monthly', intervalDays: 30, guidance: null },
  { name: 'Air-con run', cadenceLabel: 'Monthly', intervalDays: 30, guidance: null },
]

let posted: Record<string, unknown> | null = null

function mockPanel() {
  posted = null
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL, init?: RequestInit) => {
      const path = String(url)
      if (path.includes('/reference/starter-checks')) {
        return new Response(JSON.stringify(STARTER), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      if (init?.method === 'POST' && path.includes('/checks/definitions/add-set')) {
        posted = JSON.parse(String(init.body))
        return new Response(JSON.stringify({ added: [{ id: 9 }, { id: 10 }], skipped: ['Engine oil level'] }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      }
      if (path.includes('/checks/definitions')) {
        return new Response(JSON.stringify(EXISTING), { status: 200, headers: { 'Content-Type': 'application/json' } })
      }
      // GET /api/vehicles (garage) — just this car, so no copy source.
      return new Response(JSON.stringify([]), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }),
  )
}

beforeEach(() => {
  __resetScrollLock()
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, media: '', addEventListener: () => {}, removeEventListener: () => {} })))
})

afterEach(() => vi.unstubAllGlobals())

const renderPanel = () =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
            <IconSprite />
            <div id="root">
              <CheckDefinitionsPanel reg="bt53akj" />
            </div>
          </LinkProvider>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )

describe('CheckDefinitionsPanel — add a set of checks', () => {
  it('adds the generic checks the vehicle lacks, locking the ones it already has', async () => {
    mockPanel()
    const user = userEvent.setup()
    renderPanel()

    // Wait for the existing definition to load, then open the bulk-add sheet.
    await screen.findByText('Engine oil level')
    await user.click(screen.getByRole('button', { name: /Add checks…/ }))

    // The generic set appears; the already-present one is locked; the count is over the selectable two.
    expect(await screen.findByRole('checkbox', { name: /Walk-around/ })).toBeChecked()
    expect(screen.getByRole('checkbox', { name: /Engine oil level/ })).toBeDisabled()
    expect(screen.getByText('already added')).toBeInTheDocument()
    expect(screen.getByText('2 of 2')).toBeInTheDocument()

    // Submit → posts the generic source with exactly the two missing names.
    await user.click(screen.getByRole('button', { name: /Add 2 checks/ }))
    await vi.waitFor(() => expect(posted).not.toBeNull())
    expect(posted!['source']).toBe('GenericStarterSet')
    expect(posted!['selectedCheckNames']).toEqual(['Walk-around', 'Air-con run'])
    // No copy source when adding the generic set.
    expect(posted!['copyFromVehicleId']).toBeUndefined()
  })

  it('does not offer copy-from-vehicle when there is no other vehicle', async () => {
    mockPanel()
    const user = userEvent.setup()
    renderPanel()
    await screen.findByText('Engine oil level')
    await user.click(screen.getByRole('button', { name: /Add checks…/ }))
    await screen.findByRole('checkbox', { name: /Walk-around/ })
    // The "From" select has only the generic option — the garage holds no other car to copy from.
    expect(screen.queryByRole('option', { name: /Copy from another vehicle/ })).not.toBeInTheDocument()
  })
})
