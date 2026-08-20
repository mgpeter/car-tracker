import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../../api/queries'
import { __resetScrollLock } from '../../lib/useScrollLock'
import { ToastProvider } from '../../shell/Toast'
import { axe } from '../../test/axe'

const h = vi.hoisted(() => ({ navigate: vi.fn() }))

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom')
  return { ...actual, useNavigate: () => h.navigate }
})

import { VehicleLifecyclePanel } from './VehicleLifecyclePanel'

/**
 * The vehicle's lifecycle: retire it, or destroy it.
 *
 * Two properties carry the weight here. **The status control must send the enum member name exactly** - the
 * checks screen shipped this bug once, its `<option>`s sending `"Ok"` where the enum name was `"OK"`, working
 * only because the server parses case-insensitively. So the request body is asserted, not the rendered
 * control. And **the delete must arm only on the registration**, matched the way the database matches it, so
 * a correct answer typed in lower case is accepted and a near miss is not.
 *
 * The third is invisible and is the one a test has to pin: after a successful delete the cache is *removed*
 * rather than invalidated, so nothing refetches against a vehicle that has stopped existing. That is asserted
 * as "no further request to this vehicle after the DELETE".
 */

const SUMMARY = {
  registration: 'BT53 AKJ',
  name: 'Land Rover Freelander 1',
  status: 'Active',
  isDefault: true,
  logEntryCount: 214,
  documentCount: 6,
  documentBytes: 4_718_592,
  checkDefinitionCount: 18,
  issueCount: 2,
}

interface Call {
  url: string
  method: string
  body: string | null
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' },
  })

function mockApi(options: { deleteResponse?: () => Response } = {}) {
  const calls: Call[] = []

  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL, init?: RequestInit) => {
      const path = String(url)
      calls.push({
        url: path,
        method: init?.method ?? 'GET',
        body: init?.body === undefined ? null : String(init.body),
      })

      if (path.endsWith('/deletion-summary')) return json(SUMMARY)
      if (init?.method === 'DELETE') {
        return (options.deleteResponse ?? (() => json({ registration: 'BT53 AKJ', promotedRegistration: 'KV02 XYZ' })))()
      }
      if (init?.method === 'PATCH') return json({ registration: 'BT53 AKJ' })
      return json({})
    }),
  )

  return calls
}

const renderPanel = (status: 'Active' | 'Sold' | 'SORN' = 'Active') =>
  render(
    <QueryClientProvider client={createQueryClient()}>
      <ToastProvider>
        <MemoryRouter>
          <div id="root">
            <VehicleLifecyclePanel reg="BT53 AKJ" status={status} />
          </div>
        </MemoryRouter>
      </ToastProvider>
    </QueryClientProvider>,
  )

/** Opens the confirmation, which is only pressable once the counts have arrived. */
const openSheet = async (user: ReturnType<typeof userEvent.setup>) => {
  const trigger = await screen.findByRole('button', { name: /delete vehicle…/i })
  await waitFor(() => expect(trigger).toBeEnabled())
  await user.click(trigger)
}

beforeEach(() => {
  __resetScrollLock()
  h.navigate.mockClear()
})

afterEach(() => vi.unstubAllGlobals())

describe('vehicle status', () => {
  it('shows the stored status as the selected option', async () => {
    mockApi()
    renderPanel('Sold')

    expect(await screen.findByRole('radio', { name: 'Sold' })).toHaveAttribute('aria-checked', 'true')
    expect(screen.getByRole('radio', { name: 'Active' })).toHaveAttribute('aria-checked', 'false')
  })

  it('sends the enum member name exactly', async () => {
    const calls = mockApi()
    renderPanel('Active')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('radio', { name: 'SORN' }))

    await waitFor(() => expect(calls.some((c) => c.method === 'PATCH')).toBe(true))
    const patch = calls.find((c) => c.method === 'PATCH')!

    // "SORN", not "Sorn" - the casing the C# enum member has, which is what the JSON round-trips by.
    expect(JSON.parse(patch.body!)).toEqual({ status: 'SORN' })
  })

  it('does not patch when the stored status is picked again', async () => {
    const calls = mockApi()
    renderPanel('Active')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('radio', { name: 'Active' }))

    expect(calls.some((c) => c.method === 'PATCH')).toBe(false)
  })

  /**
   * Status and default are independent axes. Clearing the flag on a car you marked Sold would leave the
   * account with no default at all - a state it can enter and never leave, because nothing but creating an
   * owner's first vehicle sets it - and this operation is reversible, so setting the status back would not
   * put it right.
   */
  it('never touches the default vehicle', async () => {
    const calls = mockApi()
    renderPanel('Active')
    const user = userEvent.setup()

    await user.click(await screen.findByRole('radio', { name: 'Sold' }))

    await waitFor(() => expect(calls.some((c) => c.method === 'PATCH')).toBe(true))
    expect(JSON.parse(calls.find((c) => c.method === 'PATCH')!.body!)).not.toHaveProperty('isDefault')
  })
})

describe('deleting a vehicle', () => {
  it('will not open the confirmation until it knows what is about to go', async () => {
    mockApi()
    renderPanel()

    // A sheet that opened blank would be asking for consent on nothing.
    expect(screen.getByRole('button', { name: /delete vehicle…/i })).toBeDisabled()
    await waitFor(() => expect(screen.getByRole('button', { name: /delete vehicle…/i })).toBeEnabled())
  })

  it('states the weight before it arms', async () => {
    mockApi()
    renderPanel()
    await openSheet(userEvent.setup())

    expect(screen.getByText(/214 log entries, 18 checks, 2 issues and 6 documents/)).toBeInTheDocument()
    expect(screen.getByText(/default vehicle/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /^delete vehicle$/i })).toBeDisabled()
  })

  it('arms only on the registration', async () => {
    mockApi()
    renderPanel()
    const user = userEvent.setup()
    await openSheet(user)

    const confirm = screen.getByRole('button', { name: /^delete vehicle$/i })
    const field = screen.getByLabelText(/type the registration/i)

    await user.type(field, 'BT53 AK')
    expect(confirm).toBeDisabled()

    await user.type(field, 'J')
    expect(confirm).toBeEnabled()
  })

  /** The database matches plates ignoring case and spacing, and this gate must agree with it. */
  it('accepts the plate in any case or spacing', async () => {
    mockApi()
    renderPanel()
    const user = userEvent.setup()
    await openSheet(user)

    await user.type(screen.getByLabelText(/type the registration/i), 'bt53akj')

    expect(screen.getByRole('button', { name: /^delete vehicle$/i })).toBeEnabled()
  })

  it('leaves for the garage and stops asking about the vehicle', async () => {
    const calls = mockApi()
    renderPanel()
    const user = userEvent.setup()
    await openSheet(user)

    await user.type(screen.getByLabelText(/type the registration/i), 'BT53 AKJ')
    await user.click(screen.getByRole('button', { name: /^delete vehicle$/i }))

    await waitFor(() => expect(h.navigate).toHaveBeenCalledWith('/'))

    // The property the removeQueries-not-invalidateQueries decision exists for: nothing refetches against a
    // vehicle that has stopped existing. Anything after the DELETE must not be about this car.
    const deleteAt = calls.findIndex((c) => c.method === 'DELETE')
    expect(deleteAt).toBeGreaterThanOrEqual(0)
    expect(calls.slice(deleteAt + 1).filter((c) => c.url.includes('/api/vehicles/BT53'))).toEqual([])
  })

  it('marks the field when the server refuses the confirmation', async () => {
    mockApi({
      deleteResponse: () =>
        json(
          {
            title: 'Bad Request',
            status: 400,
            errors: { confirmRegistration: ['Type BT53 AKJ exactly to confirm.'] },
          },
          400,
        ),
    })
    renderPanel()
    const user = userEvent.setup()
    await openSheet(user)

    await user.type(screen.getByLabelText(/type the registration/i), 'BT53 AKJ')
    await user.click(screen.getByRole('button', { name: /^delete vehicle$/i }))

    await waitFor(() =>
      expect(screen.getByLabelText(/type the registration/i)).toHaveAttribute('aria-invalid', 'true'),
    )
    expect(h.navigate).not.toHaveBeenCalled()
  })

  it('has no accessibility violations with the confirmation open', async () => {
    mockApi()
    const { container } = renderPanel()
    await openSheet(userEvent.setup())

    expect(await axe(container)).toHaveNoViolations()
  })
})
