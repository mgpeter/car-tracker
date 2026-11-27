import { QueryClientProvider } from '@tanstack/react-query'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { resetRenditionBackfill } from '../../api/documents'
import { createQueryClient } from '../../api/queries'
import { IconSprite } from '../../components/IconSprite'
import { makeRenditions } from '../../lib/renditions'
import { LinkProvider } from '../../lib/link'
import { __resetScrollLock } from '../../lib/useScrollLock'
import { VehicleProvider } from '../../routes'
import { ToastProvider } from '../../shell/Toast'
import { axe } from '../../test/axe'
import { DOCUMENT_TYPES, doc, logOf, paper } from '../../test/documents'
import { ThemeProvider } from '../../theme/ThemeProvider'
import { PhotosPage } from './PhotosPage'

vi.mock('../../lib/renditions', () => ({ makeRenditions: vi.fn() }))

const LOG = logOf([
  doc({ id: 1, title: 'After the wash', documentDate: '2026-11-20', hasThumbnail: true, hasDisplay: true }),
  doc({ id: 2, title: 'Car park at Snowdon', documentDate: '2026-11-02', hasThumbnail: true, hasDisplay: true }),
  doc({
    id: 3, type: 'ConditionPhoto', title: 'Rear tyre cracking', documentDate: '2026-10-02', hasThumbnail: true,
    hasDisplay: true, linkedTo: { kind: 'Issue', id: 4, label: 'Rear tyre cracking' }, issueId: 4,
  }),
  doc({ id: 4, type: 'ConditionPhoto', title: 'Sill baseline', documentDate: null, createdAt: '2026-09-30T23:30:00Z', hasThumbnail: true, hasDisplay: true }),
  paper({ id: 5, title: 'V5C registration certificate' }),
])

/** jsdom has no IntersectionObserver; this one lets a test say when a tile scrolls into view. */
class FakeObserver {
  static all: FakeObserver[] = []
  target: Element | null = null
  readonly callback: IntersectionObserverCallback
  constructor(callback: IntersectionObserverCallback) {
    this.callback = callback
    FakeObserver.all.push(this)
  }
  observe(target: Element) {
    this.target = target
  }
  unobserve() {}
  disconnect() {
    this.target = null
  }
  static showAll() {
    act(() => {
      for (const o of FakeObserver.all) {
        if (o.target !== null) {
          o.callback([{ isIntersecting: true, target: o.target } as IntersectionObserverEntry], o as unknown as IntersectionObserver)
        }
      }
    })
  }
}

type Call = { url: string; init: RequestInit | undefined }
let calls: Call[] = []
type Answer = (url: string, init: RequestInit | undefined) => Response | Promise<Response> | undefined

function mockApi(log: unknown = LOG, answer?: Answer) {
  calls = []
  vi.stubGlobal('fetch', vi.fn(async (url: string | URL, init?: RequestInit) => {
    const href = String(url)
    calls.push({ url: href, init })
    const special = answer?.(href, init)
    if (special !== undefined) return special
    const json = (body: unknown, status = 200) =>
      new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
    if (href.includes('/file')) return new Response('bytes', { status: 200, headers: { 'Content-Type': 'image/jpeg' } })
    if (href.endsWith('/api/document-types')) return json(DOCUMENT_TYPES)
    if (init?.method === 'POST') return json(doc({ id: 90 }), href.endsWith('/renditions') ? 200 : 201)
    return json(log)
  }))
}

const fileRequests = () =>
  calls.filter((c) => c.url.includes('/file')).map((c) => c.url.replace('/api/vehicles/bt53akj/documents/', ''))
const uploads = () => calls.filter((c) => c.init?.method === 'POST' && c.url.endsWith('/documents'))

function renderPage() {
  return render(
    <ThemeProvider>
      <QueryClientProvider client={createQueryClient()}>
        <ToastProvider>
          <MemoryRouter initialEntries={['/bt53akj/photos']}>
            <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
              <IconSprite />
              <div id="root">
                <Routes>
                  <Route path="/:reg/photos" element={<VehicleProvider><PhotosPage /></VehicleProvider>} />
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
  __resetScrollLock()
  localStorage.clear()
  resetRenditionBackfill()
  FakeObserver.all = []
  vi.stubGlobal('IntersectionObserver', FakeObserver)
  vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false, media: '', addEventListener: () => {}, removeEventListener: () => {} })))
  let n = 0
  URL.createObjectURL = vi.fn(() => `blob:mock-${++n}`)
  URL.revokeObjectURL = vi.fn()
  vi.mocked(makeRenditions).mockReset()
  vi.mocked(makeRenditions).mockResolvedValue(null)
})

afterEach(() => vi.unstubAllGlobals())

const tileTitles = () => screen.getAllByRole('button', { name: /^Open / }).map((b) => b.getAttribute('aria-label'))

describe('the Photos screen', () => {
  it('shows photo-kind documents only', async () => {
    mockApi()
    renderPage()

    expect(await screen.findByRole('button', { name: 'Open After the wash' })).toBeInTheDocument()
    expect(screen.queryByText('V5C registration certificate')).not.toBeInTheDocument()
    expect(tileTitles()).toHaveLength(4)
  })

  it('filters to car photos or condition photos', async () => {
    mockApi()
    renderPage()
    await screen.findByRole('button', { name: 'Open After the wash' })

    await userEvent.click(screen.getByRole('radio', { name: 'Condition' }))
    expect(tileTitles()).toEqual(['Open Rear tyre cracking', 'Open Sill baseline'])

    await userEvent.click(screen.getByRole('radio', { name: 'Car photos' }))
    expect(tileTitles()).toEqual(['Open After the wash', 'Open Car park at Snowdon'])

    await userEvent.click(screen.getByRole('radio', { name: 'All' }))
    expect(tileTitles()).toHaveLength(4)
  })

  it('groups tiles by month, newest first, dating an undated photo by its upload in London', async () => {
    mockApi()
    renderPage()
    await screen.findByRole('button', { name: 'Open After the wash' })

    const months = screen.getAllByRole('heading', { level: 3 }).map((h) => h.textContent)
    // 23:30 UTC on 30 Sep is 00:30 on 1 Oct in London (BST).
    expect(months).toEqual(['November 2026', 'October 2026'])
    const october = screen.getByRole('region', { name: 'October 2026' })
    expect(within(october).getByText(/uploaded 1 Oct 2026/)).toBeInTheDocument()
  })

  it('requests a thumbnail only once the tile scrolls into view', async () => {
    mockApi()
    renderPage()
    await screen.findByRole('button', { name: 'Open After the wash' })

    expect(fileRequests()).toEqual([])
    FakeObserver.showAll()

    await waitFor(() => expect(fileRequests()).toHaveLength(4))
    expect(fileRequests().every((u) => u.endsWith('?variant=Thumbnail'))).toBe(true)
    const tile = screen.getByRole('button', { name: 'Open After the wash' })
    await waitFor(() => expect(within(tile).getByRole('img').getAttribute('src')).toMatch(/^blob:/))
  })

  it('never asks for a thumbnail a photo does not have, and says when this browser cannot preview it', async () => {
    mockApi(logOf([doc({ id: 9, title: 'IMG_0412', contentType: 'image/heic' })]))
    renderPage()
    await screen.findByRole('button', { name: 'Open IMG_0412' })
    FakeObserver.showAll()

    expect(await screen.findByText('No preview in this browser')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
    // The backfill fetched the original to try, found it could not decode it, and posted nothing.
    await waitFor(() => expect(fileRequests()).toEqual(['9/file']))
    expect(calls.some((c) => c.url.endsWith('/renditions'))).toBe(false)
  })

  it('backfills previews for a photo this browser can decode, once', async () => {
    vi.mocked(makeRenditions).mockResolvedValue({
      thumbnail: new Blob(['t'], { type: 'image/webp' }), display: new Blob(['d'], { type: 'image/webp' }),
      width: 4032, height: 3024,
    })
    mockApi(logOf([doc({ id: 10, title: 'Old JPEG', contentType: 'image/jpeg' })]))
    renderPage()
    await screen.findByRole('button', { name: 'Open Old JPEG' })

    await waitFor(() => expect(calls.filter((c) => c.url.endsWith('/renditions'))).toHaveLength(1))
    expect(calls.find((c) => c.url.endsWith('/renditions'))!.url).toBe('/api/vehicles/bt53akj/documents/10/renditions')
  })

  it('opens the viewer from a tile, and its Re-tag opens the RetagSheet', async () => {
    mockApi()
    renderPage()
    await userEvent.click(await screen.findByRole('button', { name: 'Open Car park at Snowdon' }))

    const viewer = await screen.findByRole('dialog', { name: 'Car park at Snowdon' })
    await userEvent.keyboard('{ArrowRight}')
    // Grid order: the next tile on screen.
    expect(viewer).toHaveAccessibleName('Rear tyre cracking')

    await userEvent.click(within(viewer).getByRole('button', { name: 'Re-tag' }))
    expect(await screen.findByRole('dialog', { name: 'Re-tag document' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog', { name: 'Rear tyre cracking' })).not.toBeInTheDocument()
  })

  it('has no axe violations with the PhotoThumb tiles loaded', async () => {
    mockApi()
    const { container } = renderPage()
    await screen.findByRole('button', { name: 'Open After the wash' })
    FakeObserver.showAll()
    await waitFor(() => expect(fileRequests()).toHaveLength(4))

    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('uploading several photos', () => {
  const lastModified = (iso: string) => new Date(`${iso}T12:00:00`).getTime()
  const FILES = [
    new File(['one'], 'IMG_0101.HEIC', { type: 'image/heic', lastModified: lastModified('2026-11-01') }),
    new File(['two'], 'IMG_0102.jpg', { type: 'image/jpeg', lastModified: lastModified('2026-11-02') }),
    new File(['three'], 'IMG_0103.jpeg', { type: 'image/jpeg', lastModified: lastModified('2026-11-03') }),
  ]

  async function chooseFiles() {
    await screen.findByRole('button', { name: 'Open After the wash' })
    await userEvent.click(screen.getAllByRole('button', { name: 'Upload' })[0]!)
    const sheet = await screen.findByRole('dialog', { name: 'Upload photos' })
    fireEvent.change(within(sheet).getByLabelText('Files'), { target: { files: FILES } })
    return sheet
  }

  it('defaults the category to Car photo and each file to its own date and name', async () => {
    mockApi()
    renderPage()
    const sheet = await chooseFiles()

    await waitFor(() => expect(within(sheet).getByRole('combobox', { name: 'Category' })).toHaveValue('CarPhoto'))
    expect(within(sheet).getByRole('textbox', { name: 'Title for IMG_0101.HEIC' })).toHaveValue('IMG_0101')
    expect(within(sheet).getByRole('textbox', { name: 'Title for IMG_0103.jpeg' })).toHaveValue('IMG_0103')
    expect(within(sheet).getByLabelText('Date for IMG_0102.jpg')).toHaveValue('2026-11-02')
  })

  it('posts the files one after another, each with its own status', async () => {
    let inFlight = 0
    let most = 0
    mockApi(LOG, (url, init) => {
      if (init?.method !== 'POST' || !url.endsWith('/documents')) return undefined
      inFlight += 1
      most = Math.max(most, inFlight)
      return new Promise((resolve) => setTimeout(() => {
        inFlight -= 1
        resolve(new Response(JSON.stringify(doc({ id: 70 })), { status: 201, headers: { 'Content-Type': 'application/json' } }))
      }, 5))
    })
    renderPage()
    const sheet = await chooseFiles()
    await waitFor(() => expect(within(sheet).getByRole('combobox', { name: 'Category' })).toHaveValue('CarPhoto'))
    await userEvent.clear(within(sheet).getByRole('textbox', { name: 'Title for IMG_0102.jpg' }))
    await userEvent.type(within(sheet).getByRole('textbox', { name: 'Title for IMG_0102.jpg' }), 'Snowdon car park')

    await userEvent.click(within(sheet).getByRole('button', { name: 'Upload 3 photos' }))

    await waitFor(() => expect(uploads()).toHaveLength(3))
    expect(most).toBe(1)
    const bodies = uploads().map((c) => c.init!.body as FormData)
    expect(bodies.map((b) => b.get('title'))).toEqual(['IMG_0101', 'Snowdon car park', 'IMG_0103'])
    expect(bodies.map((b) => b.get('type'))).toEqual(['CarPhoto', 'CarPhoto', 'CarPhoto'])
    expect(bodies.map((b) => b.get('documentDate'))).toEqual(['2026-11-01', '2026-11-02', '2026-11-03'])
    const status = within(sheet).getByRole('list', { name: 'Upload progress' })
    await waitFor(() => expect(within(status).getAllByText('Filed')).toHaveLength(3))
  })

  it('reports a duplicate against its file and carries on with the rest', async () => {
    mockApi(LOG, (url, init) => {
      if (init?.method !== 'POST' || !url.endsWith('/documents')) return undefined
      const title = (init.body as FormData).get('title')
      if (title === 'IMG_0102') {
        return new Response(JSON.stringify({
          title: 'One or more validation errors occurred.',
          errors: { File: ["This file is already filed as 'Snowdon'. The bytes are identical, so it would be the same document twice."] },
        }), { status: 400, headers: { 'Content-Type': 'application/problem+json' } })
      }
      return new Response(JSON.stringify(doc({ id: 71 })), { status: 201, headers: { 'Content-Type': 'application/json' } })
    })
    renderPage()
    const sheet = await chooseFiles()
    await waitFor(() => expect(within(sheet).getByRole('combobox', { name: 'Category' })).toHaveValue('CarPhoto'))

    await userEvent.click(within(sheet).getByRole('button', { name: 'Upload 3 photos' }))

    await waitFor(() => expect(uploads()).toHaveLength(3))
    const rows = within(within(sheet).getByRole('list', { name: 'Upload progress' })).getAllByRole('listitem')
    await waitFor(() => expect(rows[2]).toHaveTextContent('Filed'))
    expect(rows[0]).toHaveTextContent('Filed')
    expect(rows[1]).toHaveTextContent("already filed as 'Snowdon'")
    expect(within(sheet).getByRole('status')).toHaveTextContent('Filed 2 of 3')
  })

  it('stops at the allowance and says how many were filed', async () => {
    mockApi(LOG, (url, init) => {
      if (init?.method !== 'POST' || !url.endsWith('/documents')) return undefined
      const title = (init.body as FormData).get('title')
      if (title === 'IMG_0102') {
        return new Response(JSON.stringify({
          title: 'The upload was refused',
          detail: 'This account is holding 100 of its 100 documents. Remove one to file another.',
        }), { status: 400, headers: { 'Content-Type': 'application/problem+json' } })
      }
      return new Response(JSON.stringify(doc({ id: 72 })), { status: 201, headers: { 'Content-Type': 'application/json' } })
    })
    renderPage()
    const sheet = await chooseFiles()
    await waitFor(() => expect(within(sheet).getByRole('combobox', { name: 'Category' })).toHaveValue('CarPhoto'))

    await userEvent.click(within(sheet).getByRole('button', { name: 'Upload 3 photos' }))

    await waitFor(() => expect(within(sheet).getByRole('status')).toHaveTextContent(/1 photo was filed/))
    expect(uploads()).toHaveLength(2)
    const rows = within(within(sheet).getByRole('list', { name: 'Upload progress' })).getAllByRole('listitem')
    expect(rows[1]).toHaveTextContent('holding 100 of its 100 documents')
    expect(rows[2]).toHaveTextContent('Not sent')
  })

  it('has no axe violations with the multi-file sheet open', async () => {
    mockApi()
    renderPage()
    await chooseFiles()
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Category' })).toHaveValue('CarPhoto'))

    expect(await axe(document.body)).toHaveNoViolations()
  })
})
