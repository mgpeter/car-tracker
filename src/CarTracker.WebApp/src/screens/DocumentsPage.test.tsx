import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../api/queries'
import { IconSprite } from '../components/IconSprite'
import { LinkProvider } from '../lib/link'
import { VehicleProvider } from '../routes'
import { ToastProvider } from '../shell/Toast'
import { axe } from '../test/axe'
import { ThemeProvider } from '../theme/ThemeProvider'
import { DocumentsPage } from './DocumentsPage'

/**
 * BT53's real papers and the March 2026 condition set — the design's own fixture, which is what makes the
 * papers/photos split visible: seven files, two of them evidence attached to issues.
 */
const DOCUMENTS = {
  papers: [
    {
      id: 1, type: 'MOT', title: 'MOT certificate — pass', documentDate: '2026-07-08',
      contentType: 'application/pdf', sizeBytes: 184_320, sha256: 'a'.repeat(64),
      serviceRecordId: 9, expenseEntryId: null, issueId: null,
      notes: '8 Jul 2026 · 80,705 mi · 2 advisories',
      linkedTo: { kind: 'ServiceRecord', id: 9, label: 'MOT · 8 Jul 2026' },
    },
    {
      id: 2, type: 'V5C', title: 'V5C registration certificate', documentDate: '2026-03-14',
      contentType: 'application/pdf', sizeBytes: 96_000, sha256: 'b'.repeat(64),
      serviceRecordId: null, expenseEntryId: null, issueId: null,
      notes: 'logbook · keeper since 14 Mar 2026', linkedTo: null,
    },
  ],
  photos: [
    {
      id: 3, type: 'Photo', title: 'Rear tyre cracking', documentDate: '2026-03-16',
      contentType: 'image/jpeg', sizeBytes: 1_400_000, sha256: 'c'.repeat(64),
      serviceRecordId: null, expenseEntryId: null, issueId: 4,
      notes: null, linkedTo: { kind: 'Issue', id: 4, label: 'Rear tyre cracking' },
    },
    {
      id: 4, type: 'Photo', title: 'Front ¾ · baseline', documentDate: '2026-03-16',
      contentType: 'image/jpeg', sizeBytes: 1_200_000, sha256: 'd'.repeat(64),
      serviceRecordId: null, expenseEntryId: null, issueId: null, notes: null, linkedTo: null,
    },
  ],
  totalCount: 4,
  totalSizeBytes: 2_880_320,
}

function mockApi(body: unknown) {
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL) =>
      // The photo tiles fetch their bytes through the authenticated seam, so the mock has to answer those too
      // or every tile hangs on a pending promise. Built from a string rather than a Blob: jsdom's Blob is not
      // the one undici's Response accepts, and passing it fails with "object.stream is not a function".
      String(url).includes('/file')
        ? new Response('bytes', { status: 200, headers: { 'Content-Type': 'image/jpeg' } })
        : new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    ),
  )
}

/** The same provider stack the other screen tests use — `VehicleProvider` wraps the screen, `/:reg/<screen>`. */
function renderPage() {
  return render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={['/bt53akj/documents']}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
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
  document.documentElement.removeAttribute('data-theme')
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, media: '', addEventListener: () => {}, removeEventListener: () => {} })))
  // jsdom has no object-URL implementation; the photo tiles need one to render at all.
  URL.createObjectURL = vi.fn(() => 'blob:mock')
  URL.revokeObjectURL = vi.fn()
})

afterEach(() => vi.unstubAllGlobals())

describe('documents', () => {
  it('lists papers with their kind and what they are attached to', async () => {
    mockApi(DOCUMENTS)
    renderPage()

    expect(await screen.findByText('MOT certificate — pass')).toBeInTheDocument()
    expect(screen.getByText('V5C registration certificate')).toBeInTheDocument()
    // The chip is the DocumentType and the link, which is what the schema actually models — the design's
    // `identity` / `statutory` chips look like free-form tags and there is no tags table behind them.
    expect(screen.getByText('MOT')).toBeInTheDocument()
    expect(screen.getByText('→ service record')).toBeInTheDocument()
  })

  it('says so when a paper is attached to nothing', async () => {
    mockApi(DOCUMENTS)
    renderPage()

    await screen.findByText('V5C registration certificate')
    // Never an empty cell, which reads as a loading failure.
    expect(screen.getByText('not attached')).toBeInTheDocument()
  })

  it('grids the photos rather than listing them, and marks the evidence', async () => {
    mockApi(DOCUMENTS)
    renderPage()

    // A photo is an <img> with the title as its alt text, not a table row.
    expect(await screen.findByAltText('Rear tyre cracking')).toBeInTheDocument()
    expect(screen.getByAltText('Front ¾ · baseline')).toBeInTheDocument()
    expect(screen.getByText('→ issue')).toBeInTheDocument()
  })

  it('explains that the unlinked photos are the baseline', async () => {
    mockApi(DOCUMENTS)
    renderPage()

    await screen.findByAltText('Front ¾ · baseline')
    expect(screen.getByText(/Photos are evidence, not decoration/)).toBeInTheDocument()
  })

  it('offers view and save on every paper', async () => {
    mockApi(DOCUMENTS)
    renderPage()

    await screen.findByText('MOT certificate — pass')
    // Buttons rather than anchors: the bytes need the Authorization header, so they come through fetch.
    expect(screen.getAllByRole('button', { name: 'View' })).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: 'Save' })).toHaveLength(2)
  })

  it('invites the first upload when nothing is filed', async () => {
    mockApi({ papers: [], photos: [], totalCount: 0, totalSizeBytes: 0 })
    renderPage()

    expect(await screen.findByText(/No papers filed/)).toBeInTheDocument()
    expect(screen.getByText(/No photos yet/)).toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    mockApi(DOCUMENTS)
    const { container } = renderPage()
    await screen.findByText('MOT certificate — pass')
    expect(await axe(container)).toHaveNoViolations()
  })

  it('scopes the papers table with an accessible name', async () => {
    mockApi(DOCUMENTS)
    renderPage()

    const table = await screen.findByRole('table', { name: 'Filed papers' })
    expect(within(table).getByText('MOT certificate — pass')).toBeInTheDocument()
  })
})
