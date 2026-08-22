import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
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
import { AdminPage } from './AdminPage'

const META = {
  applicationName: 'CarTracker',
  version: '0.26.0',
  environment: 'Test',
  serverTimeUtc: '2026-08-22T09:00:00Z',
  identityDeletionConfigured: true,
  vehicleLookupConfigured: false,
  chatConfigured: true,
}

const ACCESS = (canWritePlans: boolean) => ({
  authenticated: true,
  plan: 'Pro',
  reason: 'Comped',
  allowances: { chatEnabled: true, dailyChatTokens: 1_000_000, maxDocuments: 2000, dailyVehicleLookups: 50 },
  admin: { canReadAdmin: true, canWritePlans },
})

/**
 * Two accounts, and the second one is the point: its plate is masked, it holds the spend, and none of it
 * belongs to whoever is reading the screen.
 */
const USERS = {
  totalAccounts: 2,
  returned: 2,
  users: [
    {
      id: 1,
      email: 'owner@example.test',
      emailVerified: true,
      displayName: 'You',
      createdAt: '2026-07-24T00:25:36Z',
      lastSeenAt: '2026-08-22T08:50:00Z',
      plan: 'Pro',
      planReason: 'Comped',
      planOverride: null,
      vehicleCount: 0,
      maskedRegistrations: [],
      chatTokensToday: 0,
      chatTokens30d: 0,
      chatTurns30d: 0,
      documentCount: 0,
      vehicleLookupsToday: 0,
      assistantTokenCount: 0,
      openAnomalyCount: 0,
    },
    {
      id: 2,
      email: 'tester@example.test',
      emailVerified: false,
      displayName: null,
      createdAt: '2026-08-20T10:00:00Z',
      lastSeenAt: null,
      plan: 'Free',
      planReason: 'AddressNotVerified',
      planOverride: null,
      vehicleCount: 1,
      maskedRegistrations: ['BT** **J'],
      chatTokensToday: 1_111,
      chatTokens30d: 1_661,
      chatTurns30d: 5,
      documentCount: 1,
      vehicleLookupsToday: 2,
      assistantTokenCount: 1,
      openAnomalyCount: 1,
    },
  ],
}

const USAGE = {
  usage: {
    today: {
      day: '2026-08-22',
      inputTokens: 1_000,
      outputTokens: 100,
      cacheReadTokens: 10,
      cacheWriteTokens: 1,
      turns: 3,
      totalTokens: 1_111,
    },
    accountsActiveToday: 1,
    byDay: [{ day: '2026-08-22', totalTokens: 1_111, turns: 3, accountsActive: 1 }],
    topAccounts: [{ userId: 2, email: 'tester@example.test', tokens: 1_661 }],
    vehicleLookupsToday: 2,
    totals: { accounts: 2, vehicles: 1, documents: 1, documentBytes: 2_048, assistantTokens: 1 },
  },
  perOwnerTokenCeiling: 500_000,
  dailyTokenCeiling: 2_000_000,
  chatConfigured: true,
}

const DIAGNOSTICS = {
  version: '0.26.0',
  environment: 'Test',
  serverTimeUtc: '2026-08-22T09:00:00Z',
  timeZone: 'Europe/London',
  signup: {
    mode: 'Open',
    allowedEmailCount: 0,
    allowedDomainCount: 0,
    isClosed: false,
    allowlistIsInert: false,
  },
  plans: {
    compEmailCount: 1,
    compDomainCount: 0,
    free: { chatEnabled: false, dailyChatTokens: 0, maxDocuments: 100, dailyVehicleLookups: 3 },
    pro: { chatEnabled: true, dailyChatTokens: 500_000, maxDocuments: 2000, dailyVehicleLookups: 50 },
  },
  chat: {
    configured: true,
    model: 'claude-sonnet-5',
    dailyTokensPerOwner: 500_000,
    dailyTokensGlobal: 2_000_000,
  },
  lookup: { vesConfigured: false, motConfigured: false },
  identity: { managementConfigured: true, pendingIdentityDeletions: 0 },
  ownership: { claimUnownedVehiclesForConfigured: false },
  documents: { rootPath: '/documents', exists: true, writable: true },
  database: { lastAppliedMigration: '20260822175117_AddAdminObservability', pendingMigrationCount: 0 },
}

const DETAIL = {
  account: USERS.users[1],
  vehicles: [
    {
      maskedRegistration: 'BT** **J',
      make: 'Land Rover',
      model: 'Freelander',
      year: 2003,
      status: 'Active',
      isDefault: true,
      createdAt: '2026-08-20T10:05:00Z',
    },
  ],
  chatUsageByDay: [],
  assistantTokens: [],
  documentBytes: 2_048,
  openAnomaliesByKind: { MileageNonMonotonic: 1 },
}

const json = (body: unknown) =>
  new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })

let canWritePlans = true

function bodyFor(path: string): unknown {
  // The longest paths first: /api/admin/users/2 ends with neither /users nor /usage, but /api/meta is a
  // prefix of /api/meta/authenticated and matching that way round is the trap AccountPage.test records.
  if (path.includes('/api/admin/users/')) return DETAIL
  if (path.endsWith('/api/admin/users')) return USERS
  if (path.endsWith('/api/admin/usage')) return USAGE
  if (path.endsWith('/api/admin/diagnostics')) return DIAGNOSTICS
  if (path.endsWith('/api/meta/authenticated')) return ACCESS(canWritePlans)
  if (path.endsWith('/api/meta')) return META
  return []
}

beforeEach(() => {
  canWritePlans = true
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

/** Rendered at `/admin`, with no `:reg` route: a panel reaching for a plate would take the screen down. */
const renderAdmin = () =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={['/admin']}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <Routes>
                  <Route path="/admin" element={<AdminPage />} />
                </Routes>
              </div>
            </LinkProvider>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )

describe('the admin page', () => {
  it('renders its three sections and names no car', async () => {
    const { container } = renderAdmin()

    expect(await screen.findByRole('heading', { name: 'Deployment' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Accounts' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Posture' })).toBeInTheDocument()

    // The page head carries no plate, and nothing on it may call usePlate().
    expect(container.querySelector('.plate')).toBeNull()
  })

  it('shows the deployment token spend against the ceiling, which nothing else in the app does', async () => {
    renderAdmin()

    expect(await screen.findByText('1,111')).toBeInTheDocument()
    expect(screen.getByText(/of 2,000,000 allowed across the deployment/)).toBeInTheDocument()
  })

  it('lists every account and never renders a full registration', async () => {
    const { container } = renderAdmin()

    expect(await screen.findByText('tester@example.test')).toBeInTheDocument()
    expect(screen.getByText('owner@example.test')).toBeInTheDocument()

    // The masking happens on the server; this asserts the client is not somehow reassembling one.
    expect(screen.getByText(/BT\*\* \*\*J/)).toBeInTheDocument()
    expect(container.textContent).not.toContain('BT53 AKJ')
  })

  it('reports the configuration posture the container actually resolved', async () => {
    renderAdmin()

    expect(await screen.findByText('20260822175117_AddAdminObservability')).toBeInTheDocument()
    expect(screen.getByText('claude-sonnet-5', { exact: false })).toBeInTheDocument()
  })

  it('opens an account and offers the plan control to a principal that may write one', async () => {
    renderAdmin()

    await userEvent.click(await screen.findByRole('row', { name: 'Open tester@example.test' }))

    expect(await screen.findByRole('radiogroup', { name: 'Set plan' })).toBeInTheDocument()
    expect(screen.getByText(/Land Rover Freelander/)).toBeInTheDocument()
  })

  it('hides the plan control from a principal holding only admin:read', async () => {
    canWritePlans = false
    renderAdmin()

    await userEvent.click(await screen.findByRole('row', { name: 'Open tester@example.test' }))

    expect(await screen.findByText(/Land Rover Freelander/)).toBeInTheDocument()
    expect(screen.queryByRole('radiogroup', { name: 'Set plan' })).toBeNull()
  })

  it('has no accessibility violations', async () => {
    const { container } = renderAdmin()

    await screen.findByText('tester@example.test')
    expect(await axe(container)).toHaveNoViolations()
  })
})
