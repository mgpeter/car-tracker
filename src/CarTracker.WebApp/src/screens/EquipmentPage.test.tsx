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
import { ThemeProvider } from '../theme/ThemeProvider'
import { EquipmentPage } from './EquipmentPage'

const item = (over: Record<string, unknown> = {}) => ({
  id: 1,
  name: 'Scissor jack',
  category: 'Recovery',
  purchasedDate: '2026-03-20',
  sourceVendor: 'Halfords',
  cost: 24.99,
  storedAt: 'Boot floor',
  status: 'Owned',
  notes: null,
  ...over,
})

const JACK = item()
const ROPE = item({ id: 2, name: 'Tow rope', status: 'ToOrder', cost: null })
const READER = item({ id: 3, name: 'OBD reader', category: 'Diagnostics' })

function mockApi(items: unknown[]) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify(items), { status: 200, headers: { 'Content-Type': 'application/json' } })),
  )
}

beforeEach(() => {
  __resetScrollLock()
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, media: '', addEventListener: () => {}, removeEventListener: () => {} })))
})

afterEach(() => vi.unstubAllGlobals())

const renderEquipment = () =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={['/bt53akj/equipment']}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <Routes>
                  <Route path="/:reg/equipment" element={<VehicleProvider><EquipmentPage /></VehicleProvider>} />
                </Routes>
              </div>
            </LinkProvider>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )

describe('inventory filter', () => {
  const count = () => document.querySelector('.tctl-count')?.textContent

  it('filters by status chip and moves the live count', async () => {
    mockApi([JACK, ROPE, READER])
    const user = userEvent.setup()
    renderEquipment()
    expect(await screen.findByText('Scissor jack')).toBeInTheDocument()
    expect(count()).toMatch(/^\s*3 items\s*$/)

    // To order only → the two owned items drop, leaving the rope.
    await user.click(screen.getByRole('button', { name: 'To order' }))
    expect(screen.getByText('Tow rope')).toBeInTheDocument()
    expect(screen.queryByText('Scissor jack')).not.toBeInTheDocument()
    expect(screen.queryByText('OBD reader')).not.toBeInTheDocument()
    expect(count()).toMatch(/1 of 3/)
  })

  it('filters by category through the select, narrowing to one group', async () => {
    mockApi([JACK, ROPE, READER])
    const user = userEvent.setup()
    renderEquipment()
    await screen.findByText('OBD reader')
    await user.selectOptions(screen.getByRole('combobox', { name: /Category/i }), 'Diagnostics')
    expect(screen.getByText('OBD reader')).toBeInTheDocument()
    expect(screen.queryByText('Scissor jack')).not.toBeInTheDocument()
    expect(screen.queryByText('Tow rope')).not.toBeInTheDocument()
    // Only the matching group heading renders — Recovery's is gone with its items (it survives as a select
    // option, which is why we check the heading, not any occurrence of the word).
    const headings = [...document.querySelectorAll('.eqhead')].map((n) => n.textContent)
    expect(headings).toEqual(['Diagnostics'])
    expect(count()).toMatch(/1 of 3/)
  })

  it('shows a filter-empty message distinct from the empty inventory', async () => {
    // Nothing is On order → filtering to it matches no rows.
    mockApi([JACK, ROPE, READER])
    const user = userEvent.setup()
    renderEquipment()
    await screen.findByText('Scissor jack')
    await user.click(screen.getByRole('button', { name: 'On order' }))
    expect(screen.getByText(/No items match this filter/)).toBeInTheDocument()
    expect(count()).toMatch(/0 of 3/)
  })

  it('searches by name across categories, collapsing the emptied headings', async () => {
    mockApi([JACK, ROPE, READER])
    const user = userEvent.setup()
    renderEquipment()
    await screen.findByText('Scissor jack')

    await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'rope')

    expect(screen.getByText('Tow rope')).toBeInTheDocument()
    expect(screen.queryByText('Scissor jack')).not.toBeInTheDocument()
    // Diagnostics loses its only item, so its heading goes with it — visibleCategories reads view.rows, and
    // that keeps working only because the search lives in the hook rather than beside it.
    expect([...document.querySelectorAll('.eqhead')].map((n) => n.textContent)).toEqual(['Recovery'])
    expect(count()).toMatch(/1 of 3/)
  })

  it('finds an item by where it is stored, though no column shows that', async () => {
    // "What's in the boot?" — storedAt is searched even though the list never renders it.
    mockApi([JACK, ROPE, READER])
    const user = userEvent.setup()
    renderEquipment()
    await screen.findByText('Scissor jack')

    await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'boot floor')

    // All three share the fixture's storedAt, so this proves the field is searched at all, not that it is rare.
    expect(count()).toMatch(/3 of 3/)
    expect(screen.getByText('Scissor jack')).toBeInTheDocument()
  })

  it('keeps the inventory stats on the full set while a search narrows the list', async () => {
    mockApi([JACK, ROPE, READER])
    const user = userEvent.setup()
    renderEquipment()
    await screen.findByText('Scissor jack')
    const owned = document.querySelector('.stats')?.textContent

    await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'rope')

    // The band above the list answers "what do I own", not "what am I looking at".
    expect(document.querySelector('.stats')?.textContent).toBe(owned)
    expect(count()).toMatch(/1 of 3/)
  })
})
