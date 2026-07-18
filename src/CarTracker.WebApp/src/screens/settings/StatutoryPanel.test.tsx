import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { VehicleSummary } from '../../api/client'
import { createQueryClient } from '../../api/queries'
import { IconSprite } from '../../components/IconSprite'
import { LinkProvider } from '../../lib/link'
import { __resetScrollLock } from '../../lib/useScrollLock'
import { ToastProvider } from '../../shell/Toast'
import { ThemeProvider } from '../../theme/ThemeProvider'
import { StatutoryPanel } from './StatutoryPanel'

const SUMMARY = {
  registration: 'BT53 AKJ',
  renewals: {
    mot: { name: 'MOT', expiryDate: '2027-07-08', daysRemaining: 359, urgency: 'Ok', source: 'MOT pass 8 Jul 2026' },
    insurance: { name: 'Insurance', expiryDate: '2027-03-15', daysRemaining: 240, urgency: 'Ok', source: 'Admiral' },
    roadTax: { name: 'Road tax', expiryDate: '2027-02-28', daysRemaining: 225, urgency: 'Ok', source: 'VED' },
    nextServiceDate: { name: 'Next service', expiryDate: null, daysRemaining: null, urgency: null, source: null },
    nextServiceMiles: null,
  },
} as unknown as VehicleSummary

/** The vehicle detail the panel fetches to seed the insurance sheet. */
const DETAIL = {
  vedAnnualCost: 430,
  insurance: {
    insurer: 'Admiral',
    policyNumber: 'P77904683',
    periodStart: '2026-03-15',
    periodEnd: '2027-03-15',
    coverType: 'Comprehensive',
    premium: 517.14,
  },
}

beforeEach(() => {
  __resetScrollLock()
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => ({ matches: false, media: '', addEventListener: () => {}, removeEventListener: () => {} })),
  )
  vi.stubGlobal(
    'fetch',
    vi.fn(async () =>
      new Response(JSON.stringify(DETAIL), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    ),
  )
})

afterEach(() => vi.unstubAllGlobals())

const renderPanel = () =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
            <IconSprite />
            <StatutoryPanel reg="bt53akj" summary={SUMMARY} />
          </LinkProvider>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )

describe('StatutoryPanel — edit sheets preload the saved values', () => {
  it('opens the insurance sheet with the stored values in the inputs, not just placeholders', async () => {
    const user = userEvent.setup()
    renderPanel()

    // Two "Edit" buttons — Road tax, then Insurance. Open Insurance.
    await user.click(screen.getAllByRole('button', { name: 'Edit' })[1]!)

    // The insurer input is populated from the vehicle detail — the bug was it showing "Admiral" only as
    // greyed placeholder text while the field itself was empty.
    const insurer = await screen.findByDisplayValue('Admiral')
    expect(insurer).toBeInTheDocument()
    expect(screen.getByDisplayValue('P77904683')).toBeInTheDocument()
    expect(screen.getByDisplayValue('Comprehensive')).toBeInTheDocument()
    expect(screen.getByDisplayValue('517.14')).toBeInTheDocument()
  })
})
