import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../api/queries'
import { LinkProvider } from '../lib/link'
import { axe } from '../test/axe'
import { ReminderBadge } from './ReminderBadge'

function mockReminders(body: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } })),
  )
}

beforeEach(() => {
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, media: '', addEventListener: () => {}, removeEventListener: () => {} })))
})

afterEach(() => vi.unstubAllGlobals())

const renderBadge = () =>
  render(
    <QueryClientProvider client={createQueryClient()}>
      <MemoryRouter initialEntries={['/bt53akj/fuel']}>
        <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
          <ReminderBadge reg="bt53akj" />
        </LinkProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )

describe('ReminderBadge', () => {
  it('shows the firing count and links to the dashboard', async () => {
    mockReminders({ firingCount: 8, items: [] })
    renderBadge()

    // Named for what it is and where it goes — not "8", which reads as nothing to a screen reader.
    const link = await screen.findByRole('link', { name: /8 reminders need attention — open the dashboard/i })
    expect(link).toBeInTheDocument()
    expect(link).toHaveTextContent('8 due')
  })

  it('disappears at zero rather than assert an all-clear', async () => {
    mockReminders({ firingCount: 0, items: [] })
    const { container } = renderBadge()

    // Give the query a tick; the badge must render nothing.
    await new Promise((r) => setTimeout(r, 20))
    expect(container.querySelector('.rem-badge')).toBeNull()
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
  })

  it('says "1 reminder", singular', async () => {
    mockReminders({ firingCount: 1, items: [] })
    renderBadge()
    expect(await screen.findByRole('link', { name: /1 reminder needs? attention/i })).toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    mockReminders({ firingCount: 3, items: [] })
    const { container } = renderBadge()
    await screen.findByRole('link')
    expect(await axe(container)).toHaveNoViolations()
  })
})
