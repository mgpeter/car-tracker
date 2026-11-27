import { QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { makeRenditions } from '../lib/renditions'
import {
  fileUrl,
  resetRenditionBackfill,
  uploadDocument,
  useRenditionBackfill,
} from './documents'
import { createQueryClient } from './queries'
import { doc } from '../test/documents'

vi.mock('../lib/renditions', () => ({ makeRenditions: vi.fn() }))

const renditions = {
  thumbnail: new Blob(['thumb'], { type: 'image/webp' }),
  display: new Blob(['display'], { type: 'image/webp' }),
  width: 4032,
  height: 3024,
}

type Call = { url: string; init: RequestInit | undefined }
let calls: Call[] = []

function stubFetch(answer?: (url: string, init?: RequestInit) => Promise<Response> | Response) {
  calls = []
  vi.stubGlobal('fetch', vi.fn(async (url: string | URL, init?: RequestInit) => {
    calls.push({ url: String(url), init })
    if (answer !== undefined) return answer(String(url), init)
    return String(url).endsWith('/file')
      ? new Response('bytes', { status: 200, headers: { 'Content-Type': 'image/jpeg' } })
      : new Response(JSON.stringify(doc({ id: 99 })), { status: 201, headers: { 'Content-Type': 'application/json' } })
  }))
}

const posted = () => calls.filter((c) => c.init?.method === 'POST')
const form = (call: Call) => call.init!.body as FormData

beforeEach(() => {
  resetRenditionBackfill()
  vi.mocked(makeRenditions).mockReset()
})

afterEach(() => vi.unstubAllGlobals())

describe('fileUrl', () => {
  it('asks for the original by default and a rendition by the contract enum', () => {
    expect(fileUrl('BT53 AKJ', 4)).toBe('/api/vehicles/BT53%20AKJ/documents/4/file')
    expect(fileUrl('BT53 AKJ', 4, 'Thumbnail')).toBe('/api/vehicles/BT53%20AKJ/documents/4/file?variant=Thumbnail')
    expect(fileUrl('BT53 AKJ', 4, 'Display')).toBe('/api/vehicles/BT53%20AKJ/documents/4/file?variant=Display')
  })
})

describe('uploadDocument', () => {
  it('posts the file, its tags, its link and all four rendition parts for an image', async () => {
    stubFetch()
    vi.mocked(makeRenditions).mockResolvedValue(renditions)
    const file = new File(['jpeg bytes'], 'IMG_0412.jpg', { type: 'image/jpeg' })

    const result = await uploadDocument('BT53 AKJ', file, {
      type: 'ServiceInvoice', title: 'Cambelt invoice', documentDate: '2026-11-20', notes: 'K & P',
      link: { kind: 'ServiceRecord', id: 7 },
    })

    expect(result.ok).toBe(true)
    const [call] = posted()
    expect(call!.url).toBe('/api/vehicles/BT53%20AKJ/documents')
    const body = form(call!)
    expect(body.get('file')).toBeInstanceOf(File)
    expect(body.get('title')).toBe('Cambelt invoice')
    expect(body.get('type')).toBe('ServiceInvoice')
    expect(body.get('documentDate')).toBe('2026-11-20')
    expect(body.get('notes')).toBe('K & P')
    expect(body.get('serviceRecordId')).toBe('7')
    expect(body.get('expenseEntryId')).toBeNull()
    expect((body.get('thumbnail') as Blob).type).toBe('image/webp')
    expect((body.get('display') as Blob).type).toBe('image/webp')
    expect(body.get('width')).toBe('4032')
    expect(body.get('height')).toBe('3024')
  })

  it.each([
    ['Expense', 'expenseEntryId'],
    ['Issue', 'issueId'],
  ] as const)('sends a %s link as %s', async (kind, field) => {
    stubFetch()
    vi.mocked(makeRenditions).mockResolvedValue(null)

    await uploadDocument('BT53 AKJ', new File(['x'], 'a.pdf', { type: 'application/pdf' }), {
      type: 'Receipt', title: 'A receipt', link: { kind, id: 12 },
    })

    expect(form(posted()[0]!).get(field)).toBe('12')
  })

  it('sends no rendition parts for a PDF', async () => {
    stubFetch()
    vi.mocked(makeRenditions).mockResolvedValue(null)

    await uploadDocument('BT53 AKJ', new File(['%PDF'], 'v5c.pdf', { type: 'application/pdf' }), {
      type: 'V5C', title: 'Logbook',
    })

    const body = form(posted()[0]!)
    for (const part of ['thumbnail', 'display', 'width', 'height']) expect(body.get(part)).toBeNull()
  })

  it('still uploads an image this browser cannot decode, without renditions', async () => {
    stubFetch()
    vi.mocked(makeRenditions).mockResolvedValue(null)

    const result = await uploadDocument('BT53 AKJ', new File(['heic'], 'IMG.HEIC', { type: 'image/heic' }), {
      type: 'CarPhoto', title: 'IMG',
    })

    expect(result.ok).toBe(true)
    expect(posted()).toHaveLength(1)
    expect(form(posted()[0]!).get('thumbnail')).toBeNull()
    expect(form(posted()[0]!).get('file')).toBeInstanceOf(File)
  })

  it('returns the server refusal rather than throwing', async () => {
    stubFetch(() => new Response(JSON.stringify({ detail: 'This file is already filed as \'Logbook\'.' }), {
      status: 400, headers: { 'Content-Type': 'application/problem+json' },
    }))
    vi.mocked(makeRenditions).mockResolvedValue(null)

    const result = await uploadDocument('BT53 AKJ', new File(['%PDF'], 'v5c.pdf', { type: 'application/pdf' }), {
      type: 'V5C', title: 'Logbook',
    })

    expect(result.ok).toBe(false)
  })
})

describe('useRenditionBackfill', () => {
  function wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={createQueryClient()}>{children}</QueryClientProvider>
  }

  it('fetches the original, makes renditions and posts them once per document per session', async () => {
    stubFetch()
    vi.mocked(makeRenditions).mockResolvedValue(renditions)
    const documents = [doc({ id: 5 })]

    const first = renderHook(() => useRenditionBackfill('BT53 AKJ', documents), { wrapper })
    await waitFor(() => expect(posted()).toHaveLength(1))
    expect(calls[0]!.url).toBe('/api/vehicles/BT53%20AKJ/documents/5/file')
    expect(posted()[0]!.url).toBe('/api/vehicles/BT53%20AKJ/documents/5/renditions')
    const body = form(posted()[0]!)
    expect(body.get('width')).toBe('4032')
    expect(body.get('thumbnail')).not.toBeNull()
    first.unmount()

    // Remounted (the strip on another record, the screen revisited): already attempted, so nothing goes again.
    renderHook(() => useRenditionBackfill('BT53 AKJ', documents), { wrapper })
    await new Promise((r) => setTimeout(r, 20))
    expect(posted()).toHaveLength(1)
  })

  it('never fetches a PDF or a document that already has renditions', async () => {
    stubFetch()
    vi.mocked(makeRenditions).mockResolvedValue(renditions)

    renderHook(() => useRenditionBackfill('BT53 AKJ', [
      doc({ id: 6, type: 'ServiceInvoice', kind: 'Paper', group: 'WorkAndParts', contentType: 'application/pdf' }),
      doc({ id: 7, hasThumbnail: true, hasDisplay: true, width: 10, height: 10 }),
    ]), { wrapper })

    await new Promise((r) => setTimeout(r, 20))
    expect(calls).toHaveLength(0)
  })

  it('posts nothing when this browser cannot decode the original either', async () => {
    stubFetch()
    vi.mocked(makeRenditions).mockResolvedValue(null)

    renderHook(() => useRenditionBackfill('BT53 AKJ', [doc({ id: 8, contentType: 'image/heic' })]), { wrapper })

    await waitFor(() => expect(calls).toHaveLength(1))
    await new Promise((r) => setTimeout(r, 20))
    expect(posted()).toHaveLength(0)
  })

  it('runs at most two at a time', async () => {
    const pending: Array<() => void> = []
    stubFetch((url) =>
      url.endsWith('/file')
        ? new Promise<Response>((resolve) =>
            pending.push(() => resolve(new Response('bytes', { status: 200, headers: { 'Content-Type': 'image/jpeg' } }))))
        : new Response(JSON.stringify(doc()), { status: 200, headers: { 'Content-Type': 'application/json' } }),
    )
    vi.mocked(makeRenditions).mockResolvedValue(renditions)

    renderHook(() => useRenditionBackfill('BT53 AKJ', [
      doc({ id: 11 }), doc({ id: 12 }), doc({ id: 13 }), doc({ id: 14 }),
    ]), { wrapper })

    await waitFor(() => expect(pending).toHaveLength(2))
    await new Promise((r) => setTimeout(r, 20))
    expect(calls.filter((c) => c.url.endsWith('/file'))).toHaveLength(2)

    pending.shift()!()
    await waitFor(() => expect(calls.filter((c) => c.url.endsWith('/file'))).toHaveLength(3))
    pending.shift()!()
    pending.shift()!()
    await waitFor(() => expect(calls.filter((c) => c.url.endsWith('/file'))).toHaveLength(4))
    pending.shift()!()
    await waitFor(() => expect(posted()).toHaveLength(4))
  })
})
