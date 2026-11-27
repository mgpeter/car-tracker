import { QueryClientProvider } from '@tanstack/react-query'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { DocumentItem } from '../../api/documents'
import { createQueryClient } from '../../api/queries'
import { LinkProvider } from '../../lib/link'
import { __resetScrollLock } from '../../lib/useScrollLock'
import { ToastProvider } from '../../shell/Toast'
import { axe } from '../../test/axe'
import { DOCUMENT_TYPES, doc } from '../../test/documents'
import { PhotoViewer } from './PhotoViewer'

const PHOTOS: DocumentItem[] = [
  doc({ id: 1, title: 'Sills after the wash', documentDate: '2026-11-20', hasThumbnail: true, hasDisplay: true, width: 4032, height: 3024 }),
  doc({
    id: 2, type: 'ConditionPhoto', title: 'Rear tyre cracking', documentDate: '2026-10-02', hasThumbnail: true,
    hasDisplay: true, issueId: 4, linkedTo: { kind: 'Issue', id: 4, label: 'Rear tyre cracking' },
  }),
  doc({ id: 3, title: 'Front three-quarter', documentDate: '2026-09-14', hasThumbnail: true, hasDisplay: true }),
]

type Call = { url: string; init: RequestInit | undefined }
let calls: Call[] = []

function mockApi() {
  calls = []
  vi.stubGlobal('fetch', vi.fn(async (url: string | URL, init?: RequestInit) => {
    const href = String(url)
    calls.push({ url: href, init })
    if (href.includes('/file')) return new Response('bytes', { status: 200, headers: { 'Content-Type': 'image/webp' } })
    if (href.endsWith('/api/document-types')) {
      return new Response(JSON.stringify(DOCUMENT_TYPES), { status: 200, headers: { 'Content-Type': 'application/json' } })
    }
    return new Response(null, { status: 204 })
  }))
}

const fileRequests = () => calls.filter((c) => c.url.includes('/file')).map((c) => c.url.replace('/api/vehicles/BT53%20AKJ/documents/', ''))

/** An opener button and the viewer, so focus return has somewhere to go. */
function Harness({ photos = PHOTOS, start = 0, onRetag = () => {}, compare = false }: {
  photos?: DocumentItem[]
  start?: number
  onRetag?: (photo: DocumentItem) => void
  compare?: boolean
}) {
  const [open, setOpen] = useState(false)
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>Open the photo</button>
      {open && (
        <PhotoViewer
          reg="BT53 AKJ"
          photos={photos}
          startIndex={start}
          onClose={() => setOpen(false)}
          onRetag={onRetag}
          compare={compare}
        />
      )}
    </>
  )
}

function renderViewer(props: Parameters<typeof Harness>[0] = {}) {
  const result = render(
    <QueryClientProvider client={createQueryClient()}>
      <ToastProvider>
        <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
          <div id="root">
            <Harness {...props} />
          </div>
        </LinkProvider>
      </ToastProvider>
    </QueryClientProvider>,
  )
  return result
}

async function open(props: Parameters<typeof Harness>[0] = {}) {
  renderViewer(props)
  const opener = screen.getByRole('button', { name: 'Open the photo' })
  await userEvent.click(opener)
  return { opener, dialog: await screen.findByRole('dialog') }
}

beforeEach(() => {
  __resetScrollLock()
  let n = 0
  URL.createObjectURL = vi.fn(() => `blob:mock-${++n}`)
  URL.revokeObjectURL = vi.fn()
  mockApi()
})

afterEach(() => vi.unstubAllGlobals())

describe('the PhotoViewer', () => {
  it('shows the display copy through the authenticated seam', async () => {
    const { dialog } = await open()

    expect(dialog).toHaveAccessibleName('Sills after the wash')
    const img = await within(dialog).findByRole('img', { name: 'Sills after the wash' })
    expect(img.getAttribute('src')).toMatch(/^blob:/)
    expect(fileRequests()).toContain('1/file?variant=Display')
    // Sized by attributes, not a style.
    expect(img).toHaveAttribute('width', '4032')
    expect(img).toHaveAttribute('height', '3024')
  })

  it('falls back to the original for a type the browser can decode', async () => {
    await open({ photos: [doc({ id: 7, title: 'Old JPEG', contentType: 'image/jpeg' })] })

    expect(await screen.findByRole('img', { name: 'Old JPEG' })).toBeInTheDocument()
    expect(fileRequests()).toEqual(['7/file'])
  })

  it('shows a placeholder for an original this browser cannot show, and never fetches it', async () => {
    await open({ photos: [doc({ id: 8, title: 'IMG_0412', contentType: 'image/heic' })] })

    expect(await screen.findByText('No preview in this browser')).toBeInTheDocument()
    expect(screen.queryByRole('img', { name: 'IMG_0412' })).not.toBeInTheDocument()
    expect(fileRequests()).toEqual([])
  })

  it('steps with the arrow keys in the order it was given', async () => {
    const { dialog } = await open()

    await userEvent.keyboard('{ArrowRight}')
    expect(dialog).toHaveAccessibleName('Rear tyre cracking')
    await userEvent.keyboard('{ArrowRight}')
    expect(dialog).toHaveAccessibleName('Front three-quarter')
    // The last one stays put rather than wrapping round.
    await userEvent.keyboard('{ArrowRight}')
    expect(dialog).toHaveAccessibleName('Front three-quarter')
    await userEvent.keyboard('{ArrowLeft}')
    expect(dialog).toHaveAccessibleName('Rear tyre cracking')
  })

  it('steps with a horizontal swipe', async () => {
    const { dialog } = await open()
    const stage = within(dialog).getByTestId('pviewer-stage')

    fireEvent.pointerDown(stage, { clientX: 300, clientY: 200, pointerId: 1 })
    fireEvent.pointerUp(stage, { clientX: 120, clientY: 210, pointerId: 1 })
    expect(dialog).toHaveAccessibleName('Rear tyre cracking')

    fireEvent.pointerDown(stage, { clientX: 100, clientY: 200, pointerId: 1 })
    fireEvent.pointerUp(stage, { clientX: 300, clientY: 190, pointerId: 1 })
    expect(dialog).toHaveAccessibleName('Sills after the wash')

    // A tap is not a swipe.
    fireEvent.pointerDown(stage, { clientX: 200, clientY: 200, pointerId: 1 })
    fireEvent.pointerUp(stage, { clientX: 210, clientY: 200, pointerId: 1 })
    expect(dialog).toHaveAccessibleName('Sills after the wash')
  })

  it('closes on Escape and gives focus back to what opened it', async () => {
    const { opener } = await open()

    await userEvent.keyboard('{Escape}')

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    await waitFor(() => expect(opener).toHaveFocus())
  })

  it('requests both neighbours’ display copies', async () => {
    await open({ start: 1 })

    await waitFor(() => expect(fileRequests()).toEqual(
      expect.arrayContaining(['1/file?variant=Display', '2/file?variant=Display', '3/file?variant=Display']),
    ))
  })

  it('captions the photo with its title, date, category and link', async () => {
    const { dialog } = await open({ start: 1 })

    const caption = within(dialog).getByRole('group', { name: 'About this photo' })
    expect(caption).toHaveTextContent('Rear tyre cracking')
    expect(caption).toHaveTextContent('2 Oct 2026')
    expect(await within(caption).findByText('Condition photo')).toBeInTheDocument()
    expect(within(caption).getByRole('link', { name: /→ issue/ })).toHaveAttribute('href', '/bt53akj/issues')
  })

  it('saves the original, not a rendition', async () => {
    const { dialog } = await open()
    calls = []

    await userEvent.click(within(dialog).getByRole('button', { name: 'Save original' }))

    await waitFor(() => expect(fileRequests()).toEqual(['1/file']))
  })

  it('sets the photo as the car’s cover', async () => {
    const { dialog } = await open({ start: 2 })

    await userEvent.click(within(dialog).getByRole('button', { name: 'Set as cover' }))

    await waitFor(() => expect(calls.some((c) => c.init?.method === 'PUT')).toBe(true))
    const put = calls.find((c) => c.init?.method === 'PUT')!
    expect(put.url).toBe('/api/vehicles/BT53%20AKJ/cover-photo')
    expect(JSON.parse(String(put.init!.body))).toEqual({ documentId: 3 })
    expect(new Headers(put.init!.headers).get('Content-Type')).toBe('application/json')
  })

  it('deletes through the two-step confirm', async () => {
    const { dialog } = await open()

    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))
    expect(calls.some((c) => c.init?.method === 'DELETE')).toBe(false)
    await userEvent.click(within(dialog).getByRole('button', { name: /Confirm delete/ }))

    await waitFor(() => expect(calls.find((c) => c.init?.method === 'DELETE')?.url)
      .toBe('/api/vehicles/BT53%20AKJ/documents/1'))
  })

  it('hands the photo to the re-tag sheet', async () => {
    const onRetag = vi.fn()
    const { dialog } = await open({ onRetag })

    await userEvent.click(within(dialog).getByRole('button', { name: 'Re-tag' }))

    expect(onRetag).toHaveBeenCalledWith(PHOTOS[0])
  })

  it('puts no style attribute on anything, because the CSP would refuse it', async () => {
    const { dialog } = await open()
    await within(dialog).findByRole('img', { name: 'Sills after the wash' })

    // The viewer and everything in it. (The body's overflow is set through CSSOM by the shared scroll lock,
    // which a CSP without 'unsafe-inline' allows; a style attribute in markup it does not.)
    expect(dialog.hasAttribute('style')).toBe(false)
    expect(dialog.querySelectorAll('[style]')).toHaveLength(0)
    expect(document.querySelector('.pviewer-ovl')!.hasAttribute('style')).toBe(false)
  })

  it('compares the first and last side by side, each dated', async () => {
    const { dialog } = await open({
      compare: true,
      photos: [
        doc({ id: 21, title: 'March', documentDate: '2026-03-16', hasThumbnail: true, hasDisplay: true }),
        doc({ id: 22, title: 'July', documentDate: '2026-07-01', hasThumbnail: true, hasDisplay: true }),
        doc({ id: 23, title: 'November', documentDate: '2026-11-20', hasThumbnail: true, hasDisplay: true }),
      ],
    })

    const panes = within(dialog).getAllByRole('figure')
    expect(panes).toHaveLength(2)
    expect(panes[0]).toHaveTextContent('March')
    expect(panes[0]).toHaveTextContent('16 Mar 2026')
    expect(panes[1]).toHaveTextContent('November')
    expect(panes[1]).toHaveTextContent('20 Nov 2026')
    await waitFor(() => expect(fileRequests()).toEqual(
      expect.arrayContaining(['21/file?variant=Display', '23/file?variant=Display'])))
  })

  it('has no axe violations while open', async () => {
    const { dialog } = await open({ start: 1 })
    await within(dialog).findByText('Condition photo')
    await act(async () => {})

    expect(await axe(document.body)).toHaveNoViolations()
  })
})
