import { QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { resetRenditionBackfill } from '../api/documents'
import { createQueryClient } from '../api/queries'
import { IconSprite } from '../components/IconSprite'
import { LinkProvider } from '../lib/link'
import { VehicleProvider } from '../routes'
import { ToastProvider } from '../shell/Toast'
import { axe } from '../test/axe'
import { DOCUMENT_TYPES, doc, logOf, paper } from '../test/documents'
import { ThemeProvider } from '../theme/ThemeProvider'
import { DocumentsPage } from './DocumentsPage'

/**
 * BT53's papers, plus a photo the server put in the log's other half. The Documents screen shows papers only;
 * the photo is there to prove it stays off this screen.
 */
const DOCUMENTS = logOf([
  paper({
    id: 1, type: 'MOT', group: 'LegalAndCover', title: 'MOT certificate - pass', documentDate: '2026-07-08',
    sizeBytes: 184_320, serviceRecordId: 9, notes: '8 Jul 2026 · 80,705 mi · 2 advisories',
    linkedTo: { kind: 'ServiceRecord', id: 9, label: 'MOT · 8 Jul 2026' },
  }),
  paper({
    id: 2, type: 'V5C', group: 'Ownership', title: 'V5C registration certificate', documentDate: '2026-03-14',
    notes: 'keeper since 14 Mar 2026',
  }),
  paper({
    id: 3, type: 'ServiceInvoice', group: 'WorkAndParts', title: 'K & P invoice 4471', documentDate: '2026-10-02',
    contentType: 'image/jpeg', expenseEntryId: 31,
    linkedTo: { kind: 'Expense', id: 31, label: 'Servicing · £412.00' },
  }),
  doc({ id: 4, type: 'CarPhoto', title: 'On the drive after a wash' }),
])

type Call = { url: string; init: RequestInit | undefined }
let calls: Call[] = []

function mockApi(log: unknown = DOCUMENTS, types: unknown = DOCUMENT_TYPES) {
  calls = []
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL, init?: RequestInit) => {
      const href = String(url)
      calls.push({ url: href, init })
      const json = (body: unknown, status = 200) =>
        new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
      if (href.includes('/file')) return new Response('bytes', { status: 200, headers: { 'Content-Type': 'image/jpeg' } })
      if (href.endsWith('/api/document-types')) return json(types)
      if (init?.method === 'POST') return json(paper({ id: 50 }), 201)
      return json(log)
    }),
  )
}

/** The same provider stack the other screen tests use — `VehicleProvider` wraps the screen, `/:reg/<screen>`. */
function renderPage(at = '/bt53akj/documents') {
  return render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={['/bt53akj/documents', at]} initialIndex={1}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <SearchProbe />
                <Routes>
                  <Route
                    path="/:reg/documents"
                    element={
                      <VehicleProvider>
                        <DocumentsPage />
                      </VehicleProvider>
                    }
                  />
                </Routes>
              </div>
            </LinkProvider>
          </MemoryRouter>
        </ToastProvider>
      </QueryClientProvider>
    </ThemeProvider>,
  )
}

beforeEach(() => {
  localStorage.clear()
  resetRenditionBackfill()
  document.documentElement.removeAttribute('data-theme')
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, media: '', addEventListener: () => {}, removeEventListener: () => {} })))
  URL.createObjectURL = vi.fn(() => 'blob:mock')
  URL.revokeObjectURL = vi.fn()
})

afterEach(() => vi.unstubAllGlobals())

const rowTitles = () =>
  within(screen.getByRole('table', { name: 'Filed papers' }))
    .getAllByRole('row')
    .slice(1)
    .map((row) => row.textContent ?? '')

describe('documents', () => {
  it('lists papers only, with their category and what they are attached to', async () => {
    mockApi()
    renderPage()

    expect(await screen.findByText('MOT certificate - pass')).toBeInTheDocument()
    expect(screen.getByText('V5C registration certificate')).toBeInTheDocument()
    // The category's label, from /api/document-types, not the enum name.
    expect(await screen.findByText('Service invoice')).toBeInTheDocument()
    expect(screen.getByText('→ service record')).toBeInTheDocument()
    // The car photo is the Photos screen's.
    expect(screen.queryByText('On the drive after a wash')).not.toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('links to the Photos screen where the photo grid used to be', async () => {
    mockApi()
    renderPage()

    await screen.findByText('MOT certificate - pass')
    // Scoped to the footnote: the nav lists Photos too.
    const note = screen.getByText(/in a gallery rather than a table/)
    expect(within(note).getByRole('link', { name: 'Photos' })).toHaveAttribute('href', '/bt53akj/photos')
    expect(note).toHaveTextContent('1 photo live on Photos')
  })

  it('points the LinkChip at the record screen through hrefFor', async () => {
    mockApi()
    renderPage()

    await screen.findByText('MOT certificate - pass')
    expect(screen.getByRole('link', { name: /service record/ })).toHaveAttribute('href', '/bt53akj/service')
    expect(screen.getByRole('link', { name: /→ expense/ })).toHaveAttribute('href', '/bt53akj/expenses')
  })

  it('says so when a paper is attached to nothing', async () => {
    mockApi()
    renderPage()

    await screen.findByText('V5C registration certificate')
    expect(screen.getByText('not attached')).toBeInTheDocument()
  })

  it.each([
    ['the title', 'invoice 4471', 'K & P invoice 4471'],
    ['the notes', '80,705', 'MOT certificate - pass'],
    ['the category label', 'logbook', 'V5C registration certificate'],
    ['the link label', '£412', 'K & P invoice 4471'],
  ])('search matches %s', async (_what, query, expected) => {
    mockApi()
    renderPage()
    await screen.findByText('MOT certificate - pass')
    await screen.findByText('Service invoice')

    await userEvent.type(screen.getByRole('searchbox', { name: 'Search papers' }), query)

    const rows = rowTitles()
    expect(rows).toHaveLength(1)
    expect(rows[0]).toContain(expected)
  })

  it('the group filter narrows the rows', async () => {
    mockApi()
    renderPage()
    await screen.findByText('MOT certificate - pass')

    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'Group' }), 'Ownership')

    const rows = rowTitles()
    expect(rows).toHaveLength(1)
    expect(rows[0]).toContain('V5C registration certificate')
  })

  it('offers view and save on every paper', async () => {
    mockApi()
    renderPage()

    await screen.findByText('MOT certificate - pass')
    expect(screen.getAllByRole('button', { name: 'View' })).toHaveLength(3)
    expect(screen.getAllByRole('button', { name: 'Save' })).toHaveLength(3)
  })

  it('invites the first upload when nothing is filed', async () => {
    mockApi(logOf([]))
    renderPage()

    expect(await screen.findByText(/No papers filed/)).toBeInTheDocument()
  })

  it('opens the RetagSheet from a row, with the category select grouped, and stays axe-clean', async () => {
    mockApi()
    renderPage()
    await screen.findByText('MOT certificate - pass')

    await userEvent.click(screen.getByRole('row', { name: 'Re-tag V5C registration certificate' }))

    const dialog = await screen.findByRole('dialog', { name: 'Re-tag document' })
    const select = within(dialog).getByRole('combobox', { name: 'Category' })
    await waitFor(() => expect(select.querySelectorAll('optgroup')).toHaveLength(6))
    expect(select).toHaveValue('V5C')
    expect(await axe(document.body)).toHaveNoViolations()
  })

  it('scopes the papers table with an accessible name', async () => {
    mockApi()
    renderPage()

    const table = await screen.findByRole('table', { name: 'Filed papers' })
    expect(within(table).getByText('MOT certificate - pass')).toBeInTheDocument()
  })
})

describe('the UploadSheet and its CategorySelect', () => {
  async function openUpload() {
    await screen.findByText('MOT certificate - pass')
    await userEvent.click(screen.getAllByRole('button', { name: 'Upload' })[0]!)
    return screen.findByRole('dialog', { name: 'Upload a document' })
  }

  it('groups the categories from /api/document-types, one optgroup per group, papers first', async () => {
    mockApi()
    renderPage()
    const dialog = await openUpload()

    const select = within(dialog).getByRole('combobox', { name: 'Category' })
    await waitFor(() => expect(select.querySelectorAll('optgroup')).toHaveLength(6))
    expect([...select.querySelectorAll('optgroup')].map((g) => g.label)).toEqual([
      'Ownership', 'Legal & cover', 'Work & parts', 'Reference', 'Photos', 'Other',
    ])
    // Defaults to the first paper category, not to Other.
    expect(select).toHaveValue('V5C')
  })

  it('still renders a group the client does not know, under its raw name', async () => {
    mockApi(DOCUMENTS, [...DOCUMENT_TYPES, { type: 'Glovebox', group: 'Glovebox', kind: 'Paper', label: 'Glovebox' }])
    renderPage()
    const dialog = await openUpload()

    const select = within(dialog).getByRole('combobox', { name: 'Category' })
    await waitFor(() =>
      expect([...select.querySelectorAll('optgroup')].map((g) => g.label)).toContain('Glovebox'))
  })

  it('posts the file with the chosen category', async () => {
    mockApi()
    renderPage()
    const dialog = await openUpload()

    const file = new File(['%PDF-1.7'], 'cambelt-invoice.pdf', { type: 'application/pdf' })
    fireEvent.change(within(dialog).getByLabelText('File'), { target: { files: [file] } })
    const select = within(dialog).getByRole('combobox', { name: 'Category' })
    await waitFor(() => expect(select.querySelectorAll('option').length).toBeGreaterThan(1))
    await userEvent.selectOptions(select, 'ServiceInvoice')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Upload' }))

    await waitFor(() => expect(calls.some((c) => c.init?.method === 'POST')).toBe(true))
    const body = calls.find((c) => c.init?.method === 'POST')!.init!.body as FormData
    expect(body.get('type')).toBe('ServiceInvoice')
    // The file name is the first guess at a title.
    expect(body.get('title')).toBe('cambelt-invoice')
    expect(body.get('thumbnail')).toBeNull()
  })

  it('has no axe violations with the papers listed and with the upload sheet open', async () => {
    mockApi()
    const { container } = renderPage()
    await screen.findByText('MOT certificate - pass')
    expect(await axe(container)).toHaveNoViolations()

    const dialog = await openUpload()
    await waitFor(() => expect(within(dialog).getByRole('combobox', { name: 'Category' })).toHaveValue('V5C'))
    expect(await axe(document.body)).toHaveNoViolations()
  })
})


describe('arriving from a filing link', () => {
  const PREFILL = '/bt53akj/documents?file=1&type=PartsReceipt&title=Euro+Car+Parts+%26+co&date=2026-11-14&expenseEntryId=412'

  it('opens the upload sheet filled in from the link', async () => {
    mockApi()
    renderPage(PREFILL)

    const sheet = await screen.findByRole('dialog', { name: 'Upload a document' })
    await waitFor(() => expect(within(sheet).getByRole('combobox', { name: 'Category' })).toHaveValue('PartsReceipt'))
    expect(within(sheet).getByRole('textbox', { name: 'Title' })).toHaveValue('Euro Car Parts & co')
    expect(within(sheet).getByLabelText('Dated')).toHaveValue('2026-11-14')
    expect(within(sheet).getByText('Attached to')).toBeInTheDocument()
    expect(within(sheet).getByText(/→ expense/)).toBeInTheDocument()
  })

  it('removes the query when the sheet closes', async () => {
    mockApi()
    renderPage(PREFILL)

    const sheet = await screen.findByRole('dialog', { name: 'Upload a document' })
    await userEvent.click(within(sheet).getByRole('button', { name: 'Close' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(screen.getByTestId('search')).toHaveTextContent('')
  })

  it('opens nothing without file=1', async () => {
    mockApi()
    renderPage('/bt53akj/documents?type=PartsReceipt&title=x')

    await screen.findByText('MOT certificate - pass')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('posts the link id with the chosen file', async () => {
    mockApi()
    renderPage(PREFILL)

    const sheet = await screen.findByRole('dialog', { name: 'Upload a document' })
    await waitFor(() => expect(within(sheet).getByRole('combobox', { name: 'Category' })).toHaveValue('PartsReceipt'))
    fireEvent.change(within(sheet).getByLabelText('File'), {
      target: { files: [new File(['%PDF'], 'receipt.pdf', { type: 'application/pdf' })] },
    })
    await userEvent.click(within(sheet).getByRole('button', { name: 'Upload' }))

    await waitFor(() => expect(calls.some((c) => c.init?.method === 'POST')).toBe(true))
    const body = calls.find((c) => c.init?.method === 'POST')!.init!.body as FormData
    expect(body.get('expenseEntryId')).toBe('412')
    expect(body.get('type')).toBe('PartsReceipt')
    expect(body.get('title')).toBe('Euro Car Parts & co')
    expect(body.get('documentDate')).toBe('2026-11-14')
    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent(''))
  })
})

/** The query string the screen is showing, so a test can see the prefill removed on close. */
function SearchProbe() {
  return <output data-testid="search">{useLocation().search}</output>
}
