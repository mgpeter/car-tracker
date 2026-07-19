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
import { TasksPage } from './TasksPage'

const task = (over: Record<string, unknown> = {}) => ({
  id: 1,
  kind: 'Workshop',
  priority: 'Medium',
  title: 'Cambelt and water pump',
  description: null,
  estimatedCost: 603.99,
  status: 'Done',
  targetDate: null,
  targetService: null,
  completedDate: '2026-06-27',
  assignedGarage: 'K & P Motors',
  serviceRecordId: null,
  notes: null,
  ...over,
})

const DONE_WORKSHOP = task()
const DIY_DONE = task({ id: 2, kind: 'DIY', title: 'Oil change', assignedGarage: null })
const OPEN_WORKSHOP = task({ id: 3, status: 'Open', completedDate: null, title: 'Rear brakes' })
const PROMOTED = task({ id: 4, serviceRecordId: 99, title: 'MOT + service', status: 'Done' })

const LOG = (tasks: unknown[]) => ({ tasks, bundleCost: 0, bundleCount: 0, openEstimateTotal: 0 })

let posted: { url: string; body: unknown } | null = null

function mockApi(tasks: unknown[]) {
  posted = null
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL, init?: RequestInit) => {
      const path = String(url)
      if (init?.method === 'POST' && path.includes('/promote')) {
        posted = { url: path, body: JSON.parse(String(init.body)) }
        return new Response(JSON.stringify({ serviceRecordId: 42 }), { status: 201, headers: { 'Content-Type': 'application/json' } })
      }
      return new Response(JSON.stringify(LOG(tasks)), { status: 200, headers: { 'Content-Type': 'application/json' } })
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

const renderTasks = () =>
  render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={['/bt53akj/tasks']}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <Routes>
                  <Route path="/:reg/tasks" element={<VehicleProvider><TasksPage /></VehicleProvider>} />
                </Routes>
              </div>
            </LinkProvider>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )

describe('task → service promotion', () => {
  it('offers convert only on a done Workshop task', async () => {
    mockApi([DONE_WORKSHOP])
    const user = userEvent.setup()
    renderTasks()
    await user.click(await screen.findByText('Cambelt and water pump'))
    expect(screen.getByRole('button', { name: /Convert to service record/i })).toBeInTheDocument()
  })

  it('does not offer convert on a DIY task', async () => {
    mockApi([DIY_DONE])
    const user = userEvent.setup()
    renderTasks()
    await user.click(await screen.findByText('Oil change'))
    expect(screen.queryByRole('button', { name: /Convert to service record/i })).not.toBeInTheDocument()
  })

  it('does not offer convert on an open Workshop task', async () => {
    mockApi([OPEN_WORKSHOP])
    const user = userEvent.setup()
    renderTasks()
    await user.click(await screen.findByText('Rear brakes'))
    expect(screen.queryByRole('button', { name: /Convert to service record/i })).not.toBeInTheDocument()
  })

  it('shows a promoted task as converted, linked to service history', async () => {
    mockApi([PROMOTED])
    const user = userEvent.setup()
    renderTasks()
    await user.click(await screen.findByText('MOT + service'))
    expect(screen.getByText('Converted')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /Open in service history/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Convert to service record/i })).not.toBeInTheDocument()
  })

  it('posts the promotion with the odometer, type and the cost defaulted from the estimate', async () => {
    mockApi([DONE_WORKSHOP])
    const user = userEvent.setup()
    renderTasks()
    await user.click(await screen.findByText('Cambelt and water pump'))
    await user.type(screen.getByPlaceholderText('80,712'), '80712')
    await user.click(screen.getByRole('button', { name: /Convert to service record/i }))

    await vi.waitFor(() => expect(posted).not.toBeNull())
    expect(posted!.url).toContain('/tasks/1/promote')
    // Cost defaults to the task's estimate; type defaults to Service.
    expect(posted!.body).toMatchObject({ mileage: 80712, type: 'Service', cost: 603.99 })
  })

  it('requires the odometer before converting', async () => {
    mockApi([DONE_WORKSHOP])
    const user = userEvent.setup()
    renderTasks()
    await user.click(await screen.findByText('Cambelt and water pump'))
    await user.click(screen.getByRole('button', { name: /Convert to service record/i }))
    expect(await screen.findByRole('alert')).toHaveTextContent(/odometer/i)
    expect(posted).toBeNull()
  })
})
