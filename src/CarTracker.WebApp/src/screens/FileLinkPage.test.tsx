import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation, useNavigationType } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../api/queries'
import { axe } from '../test/axe'
import { DOCUMENT_TYPES } from '../test/documents'
import { ToastProvider } from '../shell/Toast'
import { ThemeProvider } from '../theme/ThemeProvider'
import { FileLinkPage } from './FileLinkPage'

/**
 * Kept byte for byte equal to `FilingLinkTests.SamplePath` on the server, so a change to how either side
 * encodes the link fails a test on that side.
 */
const SERVER_BUILT =
  '/file?vehicle=BT53%20AKJ&type=PartsReceipt&title=Coolant%20%26%20thermostat%20%237%20%2B%20VAT%20%C2%A363.20%20%25off&date=2026-11-14&expenseEntryId=412'

const GARAGE = [
  { registration: 'FR02 OTH', isDefault: false, status: 'Active' },
  { registration: 'BT53 AKJ', isDefault: true, status: 'Active' },
]

function mockApi() {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL) => {
      const href = String(url)
      const json = (body: unknown) =>
        new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })
      if (href.endsWith('/api/document-types')) return json(DOCUMENT_TYPES)
      if (href.endsWith('/api/vehicles')) return json(GARAGE)
      return json({})
    }),
  )
}

/** Where the link landed, and how it got there. */
function Landed({ name }: { name: string }) {
  const location = useLocation()
  const how = useNavigationType()
  return (
    <p>
      landed on {name}: <span data-testid="path">{location.pathname}</span>
      <span data-testid="search">{location.search}</span>
      <span data-testid="how">{how}</span>
    </p>
  )
}

function renderAt(url: string) {
  return render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={['/', url]} initialIndex={1}>
            <Routes>
              <Route path="/file" element={<FileLinkPage />} />
              <Route path="/:reg/photos" element={<Landed name="photos" />} />
              <Route path="/:reg/documents" element={<Landed name="documents" />} />
              <Route path="/" element={<Landed name="garage" />} />
              <Route path="*" element={<Landed name="somewhere else" />} />
            </Routes>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )
}

const query = () => new URLSearchParams(screen.getByTestId('search').textContent ?? '')

beforeEach(() => mockApi())
afterEach(() => vi.unstubAllGlobals())

describe('FileLinkPage', () => {
  it.each(['bt53akj', 'BT53 AKJ', 'BT53AKJ', 'bt53%20akj'])('resolves the vehicle %s to the same car', async (vehicle) => {
    renderAt(`/file?vehicle=${vehicle}&type=CarPhoto&title=After%20the%20wash`)

    await screen.findByText(/landed on photos/)
    expect(screen.getByTestId('path')).toHaveTextContent('/bt53akj/photos')
  })

  it('sends a car photo to Photos with the sheet pre-filled, replacing the link in history', async () => {
    renderAt('/file?vehicle=BT53%20AKJ&type=CarPhoto&title=After%20the%20wash&date=2026-11-26')

    await screen.findByText(/landed on photos/)
    expect(screen.getByTestId('path')).toHaveTextContent('/bt53akj/photos')
    expect(Object.fromEntries(query())).toEqual({
      file: '1', type: 'CarPhoto', title: 'After the wash', date: '2026-11-26',
    })
    expect(screen.getByTestId('how')).toHaveTextContent('REPLACE')
  })

  it('sends a parts receipt to Documents carrying its expense', async () => {
    renderAt('/file?vehicle=bt53akj&type=PartsReceipt&title=Euro%20Car%20Parts&date=2026-11-14&expenseEntryId=412')

    await screen.findByText(/landed on documents/)
    expect(screen.getByTestId('path')).toHaveTextContent('/bt53akj/documents')
    expect(Object.fromEntries(query())).toEqual({
      file: '1', type: 'PartsReceipt', title: 'Euro Car Parts', date: '2026-11-14', expenseEntryId: '412',
    })
  })

  it('reads the server-built path back to the same title', async () => {
    renderAt(SERVER_BUILT)

    await screen.findByText(/landed on documents/)
    expect(query().get('title')).toBe('Coolant & thermostat #7 + VAT £63.20 %off')
    expect(query().get('expenseEntryId')).toBe('412')
  })

  it('sends an unknown category to Documents as Other', async () => {
    renderAt('/file?vehicle=bt53akj&type=Polaroid&title=Something')

    await screen.findByText(/landed on documents/)
    expect(query().get('type')).toBe('Other')
  })

  it('drops a link id that is not a number, and keeps only one link', async () => {
    renderAt('/file?vehicle=bt53akj&type=ConditionPhoto&title=Dent&issueId=4x&expenseEntryId=9&serviceRecordId=7')

    await screen.findByText(/landed on photos/)
    const q = query()
    expect(q.get('issueId')).toBeNull()
    expect([q.get('serviceRecordId'), q.get('expenseEntryId')].filter((v) => v !== null)).toHaveLength(1)
  })

  it('refuses a vehicle outside the garage and goes nowhere', async () => {
    const { container } = renderAt('/file?vehicle=ZZ99%20ZZZ&type=MOT&title=MOT')

    const refusal = await screen.findByText("This link is for a vehicle that isn't in your garage.")
    expect(within(refusal).getByRole('link', { name: /garage/i })).toHaveAttribute('href', '/')
    expect(screen.queryByText(/landed on/)).not.toBeInTheDocument()
    expect(await axe(container)).toHaveNoViolations()
  })

  it('keeps the app shell around a refused link', async () => {
    renderAt('/file?vehicle=ZZ99%20ZZZ&type=MOT&title=MOT')

    await screen.findByText("This link is for a vehicle that isn't in your garage.")
    const nav = screen.getByRole('navigation', { name: 'Primary' })
    expect(within(nav).getByRole('link', { name: 'Garage' })).toHaveAttribute('href', '/')
    expect(screen.getByRole('main')).toHaveTextContent("isn't in your garage")
  })

  it.each(['../admin', '//evil.example', 'https://evil.example', '/\\evil.example', '%2F%2Fevil.example'])(
    'never navigates outside the app for a vehicle of %s',
    async (vehicle) => {
      renderAt(`/file?vehicle=${encodeURIComponent(vehicle)}&type=CarPhoto&title=x`)

      expect(await screen.findByText("This link is for a vehicle that isn't in your garage.")).toBeInTheDocument()
      expect(screen.queryByText(/landed on/)).not.toBeInTheDocument()
    },
  )

  it('refuses a link with no vehicle rather than picking the default', async () => {
    renderAt('/file?type=CarPhoto&title=x')

    expect(await screen.findByText("This link is for a vehicle that isn't in your garage.")).toBeInTheDocument()
    expect(screen.queryByText(/landed on/)).not.toBeInTheDocument()
  })
})
