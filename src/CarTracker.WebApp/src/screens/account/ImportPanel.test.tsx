import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../../api/queries'
import { ToastProvider } from '../../shell/Toast'
import { axe } from '../../test/axe'
import { ImportPanel } from './ImportPanel'

/**
 * Bringing an export back in.
 *
 * The two things worth testing here are the two that are easy to get wrong and invisible when they are.
 * **The headline count leads** - renaming a colliding registration rather than refusing it means importing the
 * same file twice silently succeeds, and the sentence saying so is the only thing standing between an owner
 * and a duplicate garage. **An expired preview degrades to "upload it again"** - the server holds it in
 * memory, so a container restart forgets it, and the failure has to read as a re-upload rather than as a dead
 * button beside a panel that still looks live.
 */

const PREVIEW = {
  importId: 'imp_abc123',
  source: {
    exportedAt: '2026-08-14T19:02:11Z',
    schemaVersion: '0.18.0',
    email: 'seller@example.test',
    displayName: null,
    newerThanThisApp: false,
  },
  reference: {
    garages: { inFile: 3, willCreate: 1, alreadyYours: 2 },
    washLocations: { inFile: 2, willCreate: 0, alreadyYours: 2 },
    expenseCategories: { inFile: 13, willCreate: 0, alreadyYours: 13 },
  },
  vehicles: [
    {
      index: 0,
      registration: 'BT53 AKJ',
      description: '2003 Land Rover Freelander 1',
      collides: true,
      proposedRegistration: 'BT53 AKJ-2',
      rows: {
        mileageReadings: 14,
        fuelEntries: 13,
        expenses: 15,
        serviceRecords: 1,
        tyreReadings: 0,
        washEntries: 0,
        checkDefinitions: 18,
        checkLogs: 4,
        tasks: 2,
        issues: 1,
        issueWatchChecks: 2,
        equipment: 19,
        budgetGroups: 5,
      },
      skipped: { documents: 14, anomalies: 3 },
    },
  ],
  warnings: [
    '1 of 1 vehicle already exists in your garage and will be imported as a copy under a changed registration.',
    '14 document records name files this export does not contain, and will not be imported.',
  ],
}

const REPORT = {
  vehicles: [{ registration: 'BT53 AKJ-2', importedFrom: 'BT53 AKJ', rows: 102, anomaliesRaised: 1 }],
  reference: { garagesCreated: 1, washLocationsCreated: 0, expenseCategoriesCreated: 0 },
  skipped: { documents: 14, anomalies: 3, assistantTokens: 2, auditEntries: 47 },
  totalRows: 102,
}

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' },
  })

interface Call {
  url: string
  method: string
  body: BodyInit | null
}

function mockApi(options: { preview?: () => Response; commit?: () => Response } = {}) {
  const calls: Call[] = []

  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL, init?: RequestInit) => {
      const path = String(url)
      calls.push({ url: path, method: init?.method ?? 'GET', body: init?.body ?? null })

      if (path === '/api/account/import/preview') return (options.preview ?? (() => json(PREVIEW)))()
      if (path.endsWith('/commit')) return (options.commit ?? (() => json(REPORT)))()
      return json({})
    }),
  )

  return calls
}

const renderPanel = () =>
  render(
    <QueryClientProvider client={createQueryClient()}>
      <ToastProvider>
        <div id="root">
          <ImportPanel />
        </div>
      </ToastProvider>
    </QueryClientProvider>,
  )

const exportFile = () =>
  new File(['{"exportedAt":"2026-08-14T19:02:11Z","vehicles":[]}'], 'cartracker-export.json', {
    type: 'application/json',
  })

const chooseFile = async (user: ReturnType<typeof userEvent.setup>) =>
  user.upload(screen.getByLabelText(/choose an export file/i), exportFile())

afterEach(() => vi.unstubAllGlobals())
beforeEach(() => vi.clearAllMocks())

describe('account - bring data in', () => {
  it('describes what the file would do without writing anything', async () => {
    const calls = mockApi()
    renderPanel()
    const user = userEvent.setup()

    await chooseFile(user)

    expect(await screen.findByText(/2003 Land Rover Freelander 1/)).toBeInTheDocument()
    expect(screen.getByText(/seller@example.test/)).toBeInTheDocument()

    // Counted from the file's arrays, per table, so a reader can tell a whole history from an empty shell.
    expect(screen.getByText(/13 fills, 15 expenses, 1 service record/)).toBeInTheDocument()

    // One call, and it is the preview. Nothing is committed by looking.
    const only = calls[0]!
    expect(calls.map((c) => c.url)).toEqual(['/api/account/import/preview'])
    expect(only.method).toBe('POST')
    expect(only.body).toBeInstanceOf(FormData)
  })

  /**
   * The count of what already exists is what stops a second import of a file somebody has already brought in,
   * and it stops being that if it is a detail beside a row. It is asserted as the first warning rendered, not
   * merely as present.
   */
  it('leads with how many vehicles you already have', async () => {
    mockApi()
    renderPanel()
    const user = userEvent.setup()

    await chooseFile(user)
    await screen.findByText(/2003 Land Rover Freelander 1/)

    const warnings = screen.getAllByRole('listitem').map((li) => li.textContent)

    expect(warnings[0]).toMatch(/1 of 1 vehicle already exists/)
  })

  it('offers the proposed registration as an editable field on a colliding car', async () => {
    const calls = mockApi()
    renderPanel()
    const user = userEvent.setup()

    await chooseFile(user)

    const plate = await screen.findByLabelText(/registration for the imported BT53 AKJ/i)
    expect(plate).toHaveValue('BT53 AKJ-2')

    await user.clear(plate)
    await user.type(plate, 'BT53 AKJ SPARE')
    await user.click(screen.getByRole('button', { name: /^import$/i }))

    await waitFor(() => expect(calls.some((c) => c.url.endsWith('/commit'))).toBe(true))

    const commit = calls.find((c) => c.url.endsWith('/commit'))!

    // The id in the path, the decisions in the body, and **no payload**: the server is already holding the
    // file, and a commit that re-sent it would be validating the request against itself.
    expect(commit.url).toBe('/api/account/import/imp_abc123/commit')
    expect(JSON.parse(String(commit.body))).toEqual({
      vehicles: [{ index: 0, include: true, registration: 'BT53 AKJ SPARE' }],
    })
  })

  it('reports what was written, including what it left out', async () => {
    mockApi()
    renderPanel()
    const user = userEvent.setup()

    await chooseFile(user)
    await user.click(await screen.findByRole('button', { name: /^import$/i }))

    // The trailing full stop matters: the toast says "Imported 102 rows into your garage", and a matcher that
    // hits both is a matcher that throws.
    expect(await screen.findByText(/Imported 102 rows\./)).toBeInTheDocument()
    expect(screen.getByText(/was BT53 AKJ/)).toBeInTheDocument()
    expect(screen.getByText(/1 data-integrity flag raised/)).toBeInTheDocument()
    expect(
      screen.getByText(/14 document records, 2 assistant tokens and 47 assistant write-audit entries/),
    ).toBeInTheDocument()
  })

  /**
   * The preview lives in memory for fifteen minutes and a container restart forgets it, so a 404 on the commit
   * is a normal outcome rather than an error. It has to read as "do it again", and the stale panel has to go -
   * an Import button beside a preview the server no longer holds is a button that cannot work.
   */
  it('an expired preview degrades to upload it again, not to a dead button', async () => {
    mockApi({
      commit: () =>
        json(
          {
            title: 'That upload is no longer held',
            detail: 'A preview lasts fifteen minutes; upload the file again.',
            status: 404,
          },
          404,
        ),
    })
    renderPanel()
    const user = userEvent.setup()

    await chooseFile(user)
    await user.click(await screen.findByRole('button', { name: /^import$/i }))

    expect(await screen.findByText(/no longer held/i)).toBeInTheDocument()
    expect(screen.getByText(/Choose the file again/i)).toBeInTheDocument()
    expect(screen.getByText(/nothing was written/i)).toBeInTheDocument()

    // The stale panel is gone, so there is nothing left to press that could not work.
    expect(screen.queryByRole('button', { name: /^import$/i })).not.toBeInTheDocument()
    expect(screen.getByLabelText(/choose an export file/i)).toBeInTheDocument()
  })

  /**
   * A clash is corrected in place. The preview survives it - the server keeps the upload alive precisely so
   * that fixing one plate does not cost a re-upload of the whole file - and the message marks the field it is
   * about rather than folding into the footer banner.
   */
  it('a registration clash marks the field and keeps the preview open', async () => {
    mockApi({
      commit: () =>
        json(
          {
            title: 'That registration is taken',
            detail: "'BT53 AKJ' is already in your garage.",
            status: 409,
            errors: { 'vehicles[0].registration': ["'BT53 AKJ' is already in your garage."] },
          },
          409,
        ),
    })
    renderPanel()
    const user = userEvent.setup()

    await chooseFile(user)
    await user.click(await screen.findByRole('button', { name: /^import$/i }))

    const plate = await screen.findByLabelText(/registration for the imported BT53 AKJ/i)

    await waitFor(() => expect(plate).toHaveAttribute('aria-invalid', 'true'))
    expect(screen.getByRole('button', { name: /^import$/i })).toBeInTheDocument()
  })

  /**
   * A refused file says what is wrong with it. The server's per-item map is keyed into the file
   * (`vehicles[0].expenses[7].fuelEntryId`), which matches no field here, so `reportApiError` folds it into the
   * banner - which is the right place for a statement about a file rather than about a form control.
   */
  it('an unreadable file is refused with the reason, and no preview is offered', async () => {
    mockApi({
      preview: () =>
        json(
          {
            title: 'That file could not be read',
            detail: 'The file is not readable JSON: expected a value at line 4001.',
            status: 400,
          },
          400,
        ),
    })
    renderPanel()
    const user = userEvent.setup()

    await chooseFile(user)

    expect(await screen.findByText(/expected a value at line 4001/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /^import$/i })).not.toBeInTheDocument()
  })

  it('warns when the file was written by a newer version', async () => {
    mockApi({
      preview: () =>
        json({
          ...PREVIEW,
          source: { ...PREVIEW.source, schemaVersion: '99.0.0', newerThanThisApp: true },
          warnings: ['This file was written by version 99.0.0, which is newer than this app (0.18.0).'],
        }),
    })
    renderPanel()
    const user = userEvent.setup()

    await chooseFile(user)

    expect(await screen.findByText(/newer than this app/)).toBeInTheDocument()
  })

  it('has no accessibility violations with a preview open', async () => {
    mockApi()
    const { container } = renderPanel()
    const user = userEvent.setup()

    await chooseFile(user)
    await screen.findByText(/2003 Land Rover Freelander 1/)

    expect(await axe(container)).toHaveNoViolations()
  })
})
