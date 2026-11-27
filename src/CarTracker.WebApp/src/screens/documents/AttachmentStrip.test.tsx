import { QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { resetRenditionBackfill, type DocumentLinkRef } from '../../api/documents'
import { createQueryClient } from '../../api/queries'
import { makeRenditions } from '../../lib/renditions'
import { LinkProvider } from '../../lib/link'
import { __resetScrollLock } from '../../lib/useScrollLock'
import { ToastProvider } from '../../shell/Toast'
import { axe } from '../../test/axe'
import { DOCUMENT_TYPES, doc, logOf, paper } from '../../test/documents'
import { AttachmentStrip } from './AttachmentStrip'

vi.mock('../../lib/renditions', () => ({ makeRenditions: vi.fn() }))

const service7 = { serviceRecordId: 7, linkedTo: { kind: 'ServiceRecord' as const, id: 7, label: 'Cambelt · 12 May 2026' } }

const LOG = logOf([
  paper({ id: 1, type: 'ServiceInvoice', group: 'WorkAndParts', title: 'K & P invoice 4471', ...service7 }),
  doc({ id: 2, type: 'ConditionPhoto', title: 'Old belt, cracked', documentDate: '2026-05-12', hasThumbnail: true, hasDisplay: true, ...service7 }),
  doc({ id: 3, type: 'CarPhoto', title: 'New belt fitted', documentDate: '2026-05-13', hasThumbnail: true, hasDisplay: true, ...service7 }),
  paper({ id: 4, type: 'ServiceInvoice', group: 'WorkAndParts', title: 'Oil change invoice', serviceRecordId: 8, linkedTo: { kind: 'ServiceRecord', id: 8, label: 'Oil · 1 Mar 2026' } }),
  paper({ id: 5, type: 'Receipt', group: 'WorkAndParts', title: 'Halfords receipt', expenseEntryId: 7, linkedTo: { kind: 'Expense', id: 7, label: 'Parts · £24.99' } }),
  doc({ id: 6, type: 'ConditionPhoto', title: 'Tyre sidewall', issueId: 7, linkedTo: { kind: 'Issue', id: 7, label: 'Rear tyre cracking' }, hasThumbnail: true, hasDisplay: true }),
])

type Call = { url: string; init: RequestInit | undefined }
let calls: Call[] = []

function mockApi() {
  calls = []
  vi.stubGlobal('fetch', vi.fn(async (url: string | URL, init?: RequestInit) => {
    const href = String(url)
    calls.push({ url: href, init })
    const json = (body: unknown, status = 200) =>
      new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
    if (href.includes('/file')) return new Response('bytes', { status: 200, headers: { 'Content-Type': 'image/webp' } })
    if (href.endsWith('/api/document-types')) return json(DOCUMENT_TYPES)
    if (init?.method === 'POST') return json(paper({ id: 60 }), 201)
    return json(LOG)
  }))
}

function renderStrips(children: ReactNode) {
  return render(
    <QueryClientProvider client={createQueryClient()}>
      <ToastProvider>
        <LinkProvider render={({ href, children: c, ...rest }) => <a href={href} {...rest}>{c}</a>}>
          <div id="root">{children}</div>
        </LinkProvider>
      </ToastProvider>
    </QueryClientProvider>,
  )
}

const strip = (link: DocumentLinkRef) => <AttachmentStrip reg="BT53 AKJ" link={link} />

beforeEach(() => {
  __resetScrollLock()
  resetRenditionBackfill()
  vi.mocked(makeRenditions).mockResolvedValue(null)
  let n = 0
  URL.createObjectURL = vi.fn(() => `blob:mock-${++n}`)
  URL.revokeObjectURL = vi.fn()
  mockApi()
})

afterEach(() => vi.unstubAllGlobals())

describe('the AttachmentStrip', () => {
  it('shows only the record’s own papers and photos, photos in date order', async () => {
    renderStrips(strip({ kind: 'ServiceRecord', id: 7 }))

    const region = await screen.findByRole('region', { name: 'Papers & photos' })
    await within(region).findByText('K & P invoice 4471')
    expect(within(region).queryByText('Oil change invoice')).not.toBeInTheDocument()
    expect(within(region).queryByText('Halfords receipt')).not.toBeInTheDocument()
    expect(within(region).getAllByRole('button', { name: /^Open / }).map((b) => b.getAttribute('aria-label')))
      .toEqual(['Open Old belt, cracked', 'Open New belt fitted'])
  })

  it('makes one documents request for any number of strips on a screen', async () => {
    renderStrips(
      <>
        {strip({ kind: 'ServiceRecord', id: 7 })}
        {strip({ kind: 'ServiceRecord', id: 8 })}
        {strip({ kind: 'Expense', id: 7 })}
      </>,
    )

    await screen.findByText('Halfords receipt')
    expect(calls.filter((c) => c.url === '/api/vehicles/BT53%20AKJ/documents')).toHaveLength(1)
    // An expense and a service record can share an id; the kind keeps them apart.
    const regions = screen.getAllByRole('region', { name: 'Papers & photos' })
    expect(within(regions[2]!).queryByText('K & P invoice 4471')).not.toBeInTheDocument()
  })

  it('opens the viewer over that record’s photos only', async () => {
    renderStrips(strip({ kind: 'ServiceRecord', id: 7 }))

    await userEvent.click(await screen.findByRole('button', { name: 'Open New belt fitted' }))

    const viewer = await screen.findByRole('dialog', { name: 'New belt fitted' })
    expect(viewer).toHaveTextContent('2 of 2')
    await userEvent.keyboard('{ArrowLeft}')
    expect(viewer).toHaveAccessibleName('Old belt, cracked')
  })

  it('offers View and Save on a paper', async () => {
    renderStrips(strip({ kind: 'ServiceRecord', id: 7 }))

    const item = (await screen.findByText('K & P invoice 4471')).closest('li')!
    expect(within(item).getByRole('button', { name: 'View' })).toBeInTheDocument()
    await userEvent.click(within(item).getByRole('button', { name: 'Save' }))
    await waitFor(() => expect(calls.some((c) => c.url === '/api/vehicles/BT53%20AKJ/documents/1/file')).toBe(true))
  })

  it.each([
    ['ServiceRecord', 'serviceRecordId'],
    ['Expense', 'expenseEntryId'],
    ['Issue', 'issueId'],
  ] as const)('Attach on a %s presets the link and posts it as %s', async (kind, field) => {
    renderStrips(strip({ kind, id: 7 }))

    await userEvent.click(await screen.findByRole('button', { name: 'Attach' }))
    const sheet = await screen.findByRole('dialog', { name: 'Upload a document' })
    expect(within(sheet).getByText(/Attached to/)).toBeInTheDocument()
    fireEvent.change(within(sheet).getByLabelText('File'), {
      target: { files: [new File(['%PDF'], 'receipt.pdf', { type: 'application/pdf' })] },
    })
    await waitFor(() => expect(within(sheet).getByRole('combobox', { name: 'Category' })).toHaveValue('V5C'))
    await userEvent.click(within(sheet).getByRole('button', { name: 'Upload' }))

    await waitFor(() => expect(calls.some((c) => c.init?.method === 'POST')).toBe(true))
    const body = calls.find((c) => c.init?.method === 'POST')!.init!.body as FormData
    expect(body.get(field)).toBe('7')
  })

  it('offers Compare on a timeline with two or more photos, earliest on the left', async () => {
    renderStrips(<AttachmentStrip reg="BT53 AKJ" link={{ kind: 'ServiceRecord', id: 7 }} compare />)

    await userEvent.click(await screen.findByRole('button', { name: 'Compare earliest and latest' }))

    const viewer = await screen.findByRole('dialog', { name: 'Compare Old belt, cracked with New belt fitted' })
    const panes = within(viewer).getAllByRole('figure')
    expect(panes[0]).toHaveTextContent('Old belt, cracked')
    expect(panes[0]).toHaveTextContent('12 May 2026')
    expect(panes[1]).toHaveTextContent('New belt fitted')
    expect(panes[1]).toHaveTextContent('13 May 2026')
  })

  it('has no Compare with fewer than two photos', async () => {
    renderStrips(<AttachmentStrip reg="BT53 AKJ" link={{ kind: 'Issue', id: 7 }} compare />)

    await screen.findByRole('button', { name: 'Open Tyre sidewall' })
    expect(screen.queryByRole('button', { name: 'Compare earliest and latest' })).not.toBeInTheDocument()
  })

  it('says so when nothing is attached', async () => {
    renderStrips(strip({ kind: 'Issue', id: 99 }))

    expect(await screen.findByText(/Nothing attached yet/)).toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    const { container } = renderStrips(strip({ kind: 'ServiceRecord', id: 7 }))
    await screen.findByText('K & P invoice 4471')

    expect(await axe(container)).toHaveNoViolations()
  })
})
