import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../api/queries'
import { IconSprite } from '../components/IconSprite'
import { LinkProvider } from '../lib/link'
import { __resetFuelUnit } from '../lib/fuelUnit'
import { __resetScrollLock } from '../lib/useScrollLock'
import { ToastProvider } from '../shell/Toast'
import { axe } from '../test/axe'
import { ThemeProvider } from '../theme/ThemeProvider'
import { AccountPage } from './AccountPage'

const ACCOUNT = {
  email: 'you@example.test',
  createdAt: '2026-07-24T00:25:36Z',
  vehicleCount: 1,
  logEntryCount: 214,
  documentCount: 6,
  documentBytes: 4_718_592,
  assistantTokenCount: 2,
}

const META = {
  applicationName: 'CarTracker',
  version: '0.15.0',
  environment: 'Test',
  serverTimeUtc: '2026-08-15T09:00:00Z',
  identityDeletionConfigured: true,
  // True throughout, so the plan panel's "not on this plan" row is testing the *plan* rather than an
  // unconfigured deployment. The two produce different sentences and only one of them is about the account.
  chatConfigured: true,
}

/** The paid tier. `PLAN(false)` is the free one, and the two differ on every field. */
const PLAN = (chatEnabled: boolean, reason = chatEnabled ? 'Comped' : 'NotOnCompList') => ({
  authenticated: true,
  plan: chatEnabled ? 'Pro' : 'Free',
  reason,
  allowances: {
    chatEnabled,
    dailyChatTokens: chatEnabled ? 1_000_000 : 0,
    maxDocuments: chatEnabled ? 2000 : 100,
    dailyVehicleLookups: chatEnabled ? 50 : 3,
  },
})

const json = (body: unknown) =>
  new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })

let chatEnabled = true
let planReason: string | undefined

function bodyFor(path: string): unknown {
  if (path.endsWith('/api/account/summary')) return ACCOUNT
  // Before the bare `/api/meta`, which is a prefix of it. Matching the other way round answers both with the
  // deployment's response and the plan panel silently renders as though the call were still in flight.
  if (path.endsWith('/api/meta/authenticated')) {
    return planReason === undefined ? PLAN(chatEnabled) : PLAN(chatEnabled, planReason)
  }
  if (path.endsWith('/api/meta')) return META
  return []
}

beforeEach(() => {
  chatEnabled = true
  planReason = undefined
  __resetScrollLock()
  __resetFuelUnit()
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => ({ matches: false, media: '', addEventListener: () => {}, removeEventListener: () => {} })),
  )
  vi.stubGlobal('fetch', vi.fn(async (url: string | URL) => json(bodyFor(String(url)))))
})

afterEach(() => vi.unstubAllGlobals())

/**
 * Rendered at `/account`, with **no `:reg` route and no `VehicleProvider`**.
 *
 * That absence is itself an assertion. `useVehicleReg()` throws outside a vehicle route and `usePlate()` calls
 * it, so any panel on this page that reached for a registration would take the whole screen down here rather
 * than rendering a wrong label. These four panels were vehicle-scoped by accident of where they lived, never
 * by need.
 */
const renderAccount = () =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={['/account']}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <Routes>
                  <Route path="/account" element={<AccountPage />} />
                </Routes>
              </div>
            </LinkProvider>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )

describe('the account page', () => {
  it('renders all five account sections and no vehicle', async () => {
    renderAccount()

    expect(await screen.findByRole('heading', { name: 'Your account' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Plan' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Assistant access' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Appearance' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Reference lists' })).toBeInTheDocument()
  })

  it('asks for no vehicle at all', async () => {
    renderAccount()
    await screen.findByRole('heading', { name: 'Your account' })

    // Not a stylistic point. Every request this page makes is account-scoped; a `/api/vehicles/...` call here
    // would mean a panel had kept a dependency on a registration that the URL no longer carries.
    const calls = (globalThis.fetch as unknown as { mock: { calls: [string][] } }).mock.calls
    expect(calls.map(([url]) => String(url)).filter((u) => u.includes('/api/vehicles'))).toEqual([])
  })

  it('renders no bottom nav, because there is no vehicle to point it at', async () => {
    renderAccount()
    await screen.findByRole('heading', { name: 'Your account' })

    // Three of the bar's five slots are vehicle-scoped links. The garage reaches the same conclusion.
    expect(screen.queryByRole('navigation', { name: 'Primary mobile' })).not.toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    const { container } = renderAccount()
    await screen.findByRole('heading', { name: 'Your account' })
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no axe violations on the free tier either', async () => {
    // The plan panel renders different text on each tier, and the free one is the tier every new account is
    // on - so it is the one most people see and the one worth sweeping.
    chatEnabled = false
    const { container } = renderAccount()
    await screen.findByText('Not on this plan')
    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('the account page - plan', () => {
  it('names the tier and counts documents against its ceiling', async () => {
    renderAccount()

    // findByText, not getByText after finding the heading: the heading is static markup and resolves a tick
    // before the plan does, so the synchronous read would catch the panel still rendering its em-dash.
    expect(await screen.findByText('Pro')).toBeInTheDocument()
    expect(screen.getByText('Included')).toBeInTheDocument()
    // The used figure comes from the account summary and the ceiling from the plan, which is the whole reason
    // this panel reads two caches: one knows what you have, the other what you may have.
    expect(screen.getByText('6 of 2000')).toBeInTheDocument()
    expect(screen.getByText('50 a day')).toBeInTheDocument()
  })

  it('says the assistant is not on the plan, and says why it is not the deployment', async () => {
    // The distinction the shell cannot show: it hides the entry point for both causes, so this row is the only
    // place somebody can tell "your plan" from "this deployment has no model credential".
    chatEnabled = false
    renderAccount()

    expect(await screen.findByText('Free')).toBeInTheDocument()
    expect(screen.getByText('Not on this plan')).toBeInTheDocument()
    expect(screen.getByText(/ask whoever runs this deployment to add you/i)).toBeInTheDocument()
    expect(screen.getByText('6 of 100')).toBeInTheDocument()
    expect(screen.getByText('3 a day')).toBeInTheDocument()
  })

  it('names the deployment when its comp list is empty, rather than blaming the account', async () => {
    // The regression guard for what actually happened on cambelt.app: 0.24.0 shipped with no comp list, every
    // account was Free, and this screen could say nothing an owner could act on. Asserted by exact text,
    // because that sentence IS the fix - it names the setting to change.
    chatEnabled = false
    planReason = 'NobodyIsComped'
    renderAccount()

    expect(await screen.findByText('Free')).toBeInTheDocument()
    expect(
      screen.getByText('no account on this deployment is on the paid tier - Plans:CompEmails is empty'),
    ).toBeInTheDocument()
  })

  it('sends somebody with an unconfirmed address to their inbox, not to the deployment owner', async () => {
    // The two Free refusals that look identical and are opposite instructions. Getting this wrong sends
    // somebody who has already been invited off to ask for an invitation again.
    chatEnabled = false
    planReason = 'AddressNotVerified'
    renderAccount()

    expect(await screen.findByText(/follow the link the sign-in provider emailed you/i)).toBeInTheDocument()
    expect(screen.queryByText(/ask whoever runs this deployment/i)).not.toBeInTheDocument()
  })

  it('lets the deployment having no model credential outrank the account reason', async () => {
    // With no key, the assistant is off for everybody - so a comp would not help and must not be suggested.
    chatEnabled = false
    planReason = 'NobodyIsComped'
    META.chatConfigured = false
    try {
      renderAccount()
      expect(
        await screen.findByText(/this deployment holds no model credential/i),
      ).toBeInTheDocument()
      expect(screen.queryByText(/Plans:CompEmails is empty/)).not.toBeInTheDocument()
    } finally {
      META.chatConfigured = true
    }
  })
})

describe('the account page - appearance', () => {
  it('switches the fuel-economy unit and persists the choice', async () => {
    renderAccount()
    const user = userEvent.setup()

    const group = await screen.findByRole('radiogroup', { name: /fuel economy units/i })
    await user.click(within(group).getByRole('radio', { name: 'L/100 km' }))

    // Persisted like the theme - a reload reads it back. No server call: it is display-only.
    expect(localStorage.getItem('ct-fuel-unit')).toBe('l100')
    // The design's toast, which names the equivalence so the change reads as a display choice, not a recompute.
    expect(await screen.findByText(/28.7 MPG renders as 9.8/)).toBeInTheDocument()
  })
})

describe('the account page - reference lists', () => {
  function mockRefs() {
    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string | URL) => {
        const path = String(url)
        if (path.endsWith('/reference/garages')) {
          return json([
            { name: 'K & P Motors', contact: null, address: null, notes: null, referenceCount: 3 },
            { name: 'Spare Garage', contact: null, address: null, notes: null, referenceCount: 0 },
          ])
        }
        if (path.endsWith('/reference/expense-categories')) {
          return json([
            { name: 'Fuel', isMirrorOnly: true, isSystem: true, referenceCount: 13 },
            { name: 'Detailing', isMirrorOnly: false, isSystem: false, referenceCount: 2 },
          ])
        }
        return json(bodyFor(path))
      }),
    )
  }

  it('locks the Fuel category - no delete offered', async () => {
    mockRefs()
    renderAccount()
    // Fuel is system + mirror-only: it shows a lock, not an Edit/Delete affordance.
    const fuelRow = (await screen.findByText('Locked')).closest('.setrow') as HTMLElement
    expect(within(fuelRow).getByText('Fuel')).toBeInTheDocument()
    expect(within(fuelRow).queryByRole('button', { name: /edit/i })).not.toBeInTheDocument()
  })

  it('requires a re-home target before deleting a referenced garage', async () => {
    mockRefs()
    renderAccount()
    const user = userEvent.setup()

    // K & P Motors has 3 records - opening it offers "Re-home & delete" and a picker, not a bare delete.
    const row = (await screen.findByText('K & P Motors')).closest('.setrow') as HTMLElement
    await user.click(within(row).getByRole('button', { name: /edit/i }))
    expect(await screen.findByText(/Re-home to/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Re-home & delete/i })).toBeInTheDocument()

    // Deleting without picking a target is refused client-side with the count.
    await user.click(screen.getByRole('button', { name: /Re-home & delete/i }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/3 records use this/)
  })
})
