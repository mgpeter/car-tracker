import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { FilingOffer } from '../api/chat'
import { createQueryClient } from '../api/queries'
import { IconSprite } from '../components/IconSprite'
import { __resetScrollLock } from '../lib/useScrollLock'
import { ToastProvider } from '../shell/Toast'
import { axe } from '../test/axe'
import { DOCUMENT_TYPES, doc } from '../test/documents'
import { ChatPanel } from './ChatPanel'

// The chat's own preparation (canvas, base64) and the gallery's renditions are browser work jsdom cannot do;
// what these tests are about is what happens to the original file once it has been picked.
vi.mock('../lib/attachments', async (original) => ({
  ...(await original<typeof import('../lib/attachments')>()),
  prepare: vi.fn(async (file: File) =>
    file.name.endsWith('.heic')
      ? { ok: false, reason: `${file.name} is in a format this browser cannot open.` }
      : {
          ok: true,
          value: { mediaType: file.type === 'application/pdf' ? 'application/pdf' : 'image/jpeg', data: 'UFJFUEFSRUQ=' },
          preview: file.name,
        },
  ),
}))

vi.mock('../lib/renditions', () => ({
  makeRenditions: vi.fn(async () => ({
    thumbnail: new Blob(['thumb'], { type: 'image/webp' }),
    display: new Blob(['display'], { type: 'image/webp' }),
    width: 4032,
    height: 3024,
  })),
}))

const frame = (event: string, data: unknown) => `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`

const stream = (...frames: string[]) =>
  new Response(frames.join(''), { status: 200, headers: { 'Content-Type': 'text/event-stream' } })

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' },
  })

const day = (iso: string) => new Date(`${iso}T12:00:00`).getTime()

const PHOTO = new File(['the original photo bytes'], 'IMG_2041.JPG', { type: 'image/jpeg', lastModified: day('2026-11-20') })
const RECEIPT = new File(['%PDF the receipt'], 'euro-car-parts.pdf', { type: 'application/pdf', lastModified: day('2026-11-14') })

const offer = (overrides: Partial<FilingOffer> = {}): FilingOffer => ({
  registration: 'BT53 AKJ',
  type: 'PartsReceipt',
  kind: 'Paper',
  group: 'WorkAndParts',
  label: 'Parts receipt',
  title: 'Euro Car Parts - coolant',
  documentDate: '2026-11-14',
  linkedTo: { kind: 'Expense', id: 412, label: 'Parts · £63.20' },
  attachment: 2,
  allowance: { used: 37, limit: 2000 },
  path: '/file?vehicle=BT53%20AKJ&type=PartsReceipt',
  url: null,
  ...overrides,
})

type Call = { url: string; init: RequestInit | undefined }
let calls: Call[] = []

/**
 * The API, by route: the category table, the garage, the chat turns in order, and the documents endpoint.
 * `chat` answers are consumed one per turn; `upload` decides what the documents endpoint says.
 */
function mockApi({
  chat = [],
  upload,
}: {
  chat?: Array<Response | Promise<Response>>
  upload?: (body: FormData) => Response
} = {}) {
  calls = []
  let turn = 0
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL, init?: RequestInit) => {
      const href = String(url)
      calls.push({ url: href, init })
      if (href.endsWith('/api/document-types')) return json(DOCUMENT_TYPES)
      if (href.endsWith('/api/vehicles') && (init?.method ?? 'GET') === 'GET') {
        return json([
          { registration: 'FR02 OTH', isDefault: false, status: 'Active' },
          { registration: 'BT53 AKJ', isDefault: true, status: 'Active' },
        ])
      }
      if (href.includes('/api/chat')) return (await chat[turn++]) ?? stream(frame('done', { messages: [] }))
      if (init?.method === 'POST' && href.endsWith('/documents')) {
        return upload?.(init.body as FormData) ?? json(doc({ id: 90 }), 201)
      }
      return json({})
    }),
  )
}

const uploads = () => calls.filter((c) => c.init?.method === 'POST' && c.url.endsWith('/documents'))

function renderPanel(vehicle: string | null = 'BT53 AKJ') {
  return render(
    <QueryClientProvider client={createQueryClient()}>
      <ToastProvider>
        <IconSprite />
        <div id="root">
          <ChatPanel vehicle={vehicle} variant="dock" onClose={() => {}} />
        </div>
      </ToastProvider>
    </QueryClientProvider>,
  )
}

async function sendWith(files: File[], message = 'Log this receipt') {
  await userEvent.upload(screen.getByLabelText(/Attach a photo or PDF/), files)
  for (const f of files.filter((x) => !x.name.endsWith('.heic'))) await screen.findByText(f.name)
  await userEvent.type(screen.getByRole('textbox', { name: 'Message' }), message)
  await userEvent.click(screen.getByRole('button', { name: 'Send' }))
}

const card = (name: string) => screen.getByRole('region', { name: `Filing card for ${name}` })
const findCard = (name: string) => screen.findByRole('region', { name: `Filing card for ${name}` })

beforeEach(() => {
  __resetScrollLock()
  localStorage.clear()
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('FilingCard, as the chat panel renders it', () => {
  it('appear for every attachment the moment it is sent, with defaults, before any stream event', async () => {
    // A turn that never answers: the cards cannot be waiting on the model.
    mockApi({ chat: [new Promise<Response>(() => {})] })
    renderPanel()

    await sendWith([PHOTO, RECEIPT])

    const photo = await findCard('IMG_2041.JPG')
    expect(within(photo).getByText('Car photo')).toBeInTheDocument()
    expect(within(photo).getByText('IMG_2041')).toBeInTheDocument()
    expect(within(photo).getByText('20 Nov 2026')).toBeInTheDocument()
    expect(within(photo).getByText('Default suggestion')).toBeInTheDocument()
    expect(within(photo).getByText('BT53 AKJ')).toBeInTheDocument()

    const receipt = card('euro-car-parts.pdf')
    expect(within(receipt).getByText('Other')).toBeInTheDocument()
    expect(within(receipt).getByText('euro-car-parts')).toBeInTheDocument()
    expect(within(receipt).getByText('14 Nov 2026')).toBeInTheDocument()
    expect(within(receipt).getByText('Default suggestion')).toBeInTheDocument()
  })

  it('target the garage default vehicle when the panel has none', async () => {
    mockApi({ chat: [new Promise<Response>(() => {})] })
    renderPanel(null)

    await sendWith([PHOTO], 'Look at her')

    const photo = await findCard('IMG_2041.JPG')
    expect(await within(photo).findByText('BT53 AKJ')).toBeInTheDocument()
  })

  it('give a file the browser refused no card', async () => {
    mockApi({ chat: [new Promise<Response>(() => {})] })
    renderPanel()

    await sendWith([new File(['x'], 'IMG_0001.heic', { type: 'image/heic' }), PHOTO])

    await findCard('IMG_2041.JPG')
    expect(screen.queryByRole('region', { name: 'Filing card for IMG_0001.heic' })).not.toBeInTheDocument()
  })

  it('take an offer for the second attachment and leave the first alone', async () => {
    mockApi({ chat: [stream(frame('filing_offer', { offer: offer({ attachment: 2 }) }), frame('done', { messages: [] }))] })
    renderPanel()

    await sendWith([PHOTO, RECEIPT])

    const receipt = await findCard('euro-car-parts.pdf')
    await within(receipt).findByText('Suggested by the assistant')
    expect(within(receipt).getByText('Parts receipt')).toBeInTheDocument()
    expect(within(receipt).getByText('Euro Car Parts - coolant')).toBeInTheDocument()
    expect(within(receipt).getByText('14 Nov 2026')).toBeInTheDocument()
    expect(within(receipt).getByText(/expense · Parts · £63.20/)).toBeInTheDocument()

    const photo = card('IMG_2041.JPG')
    expect(within(photo).getByText('Car photo')).toBeInTheDocument()
    expect(within(photo).getByText('Default suggestion')).toBeInTheDocument()
  })

  it('fill the first card without an offer when the offer names no attachment', async () => {
    mockApi({
      chat: [stream(frame('filing_offer', { offer: offer({ attachment: null, type: 'CarPhoto', kind: 'Photo', group: 'Photos', label: 'Car photo', title: 'After the wash', linkedTo: null }) }), frame('done', { messages: [] }))],
    })
    renderPanel()

    await sendWith([PHOTO, RECEIPT])

    const photo = await findCard('IMG_2041.JPG')
    await within(photo).findByText('Suggested by the assistant')
    expect(within(photo).getByText('After the wash')).toBeInTheDocument()
    expect(within(card('euro-car-parts.pdf')).getByText('Default suggestion')).toBeInTheDocument()
  })

  it('ignore an offer for a position that does not exist', async () => {
    mockApi({ chat: [stream(frame('filing_offer', { offer: offer({ attachment: 3 }) }), frame('text', { delta: 'Done.' }), frame('done', { messages: [] }))] })
    renderPanel()

    await sendWith([PHOTO, RECEIPT])

    await screen.findByText('Done.')
    expect(screen.queryByText('Suggested by the assistant')).not.toBeInTheDocument()
    expect(screen.queryByText('Euro Car Parts - coolant')).not.toBeInTheDocument()
  })

  it('ignore an offer for a card already dismissed or filed', async () => {
    let release: (r: Response) => void = () => {}
    const second = new Promise<Response>((resolve) => (release = resolve))
    mockApi({ chat: [stream(frame('done', { messages: [] })), second] })
    renderPanel()

    await sendWith([PHOTO, RECEIPT])
    await userEvent.click(within(await findCard('euro-car-parts.pdf')).getByRole('button', { name: 'Not now' }))
    await userEvent.click(within(card('IMG_2041.JPG')).getByRole('button', { name: 'File' }))
    await within(card('IMG_2041.JPG')).findByText('Filed')

    await userEvent.type(screen.getByRole('textbox', { name: 'Message' }), 'Thanks')
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))
    release(stream(
      frame('filing_offer', { offer: offer({ attachment: 2 }) }),
      frame('filing_offer', { offer: offer({ attachment: 1, title: 'Too late' }) }),
      frame('text', { delta: 'OK.' }),
      frame('done', { messages: [] }),
    ))

    await screen.findByText('OK.')
    expect(screen.queryByRole('region', { name: 'Filing card for euro-car-parts.pdf' })).not.toBeInTheDocument()
    expect(screen.queryByText('Too late')).not.toBeInTheDocument()
  })

  it('take an offer arriving on the confirm stream for the latest message with files', async () => {
    const DRAFT = {
      pendingWriteId: 'pw_1',
      drafts: [{ callId: 'call-1', tool: 'log_expense', title: 'Log expense', arguments: { category: 'Parts', amount: 63.2 }, schema: { properties: { category: { type: 'string' }, amount: { type: 'number' } } } }],
    }
    mockApi({
      chat: [
        stream(frame('pending_write', DRAFT), frame('done', { messages: [] })),
        stream(frame('filing_offer', { offer: offer({ attachment: 1 }) }), frame('done', { messages: [] })),
      ],
    })
    renderPanel()

    await sendWith([RECEIPT])
    await userEvent.click(await screen.findByRole('button', { name: 'Save it' }))

    const receipt = card('euro-car-parts.pdf')
    await within(receipt).findByText('Suggested by the assistant')
    expect(within(receipt).getByText(/expense · Parts · £63.20/)).toBeInTheDocument()
    expect(calls.some((c) => c.url.endsWith('/api/chat/confirm'))).toBe(true)
  })
})

describe('File', () => {
  it('posts the original file, its renditions and the offer, then links to where it went', async () => {
    mockApi({ chat: [stream(frame('filing_offer', { offer: offer({ attachment: 1 }) }), frame('done', { messages: [] }))] })
    renderPanel()

    await sendWith([PHOTO])
    const photo = await findCard('IMG_2041.JPG')
    await within(photo).findByText('Suggested by the assistant')

    await userEvent.click(within(photo).getByRole('button', { name: 'File' }))

    await waitFor(() => expect(uploads()).toHaveLength(1))
    const request = uploads()[0]!
    expect(request.url).toBe('/api/vehicles/BT53%20AKJ/documents')
    const body = request.init!.body as FormData
    const sent = body.get('file') as File
    // The original, not the JPEG the chat prepared for the model.
    expect(sent.name).toBe('IMG_2041.JPG')
    expect(await sent.text()).toBe('the original photo bytes')
    expect(body.get('thumbnail')).toBeInstanceOf(Blob)
    expect(body.get('display')).toBeInstanceOf(Blob)
    expect(body.get('width')).toBe('4032')
    expect(body.get('type')).toBe('PartsReceipt')
    expect(body.get('title')).toBe('Euro Car Parts - coolant')
    expect(body.get('documentDate')).toBe('2026-11-14')
    expect(body.get('expenseEntryId')).toBe('412')

    expect(await within(photo).findByText('Filed')).toBeInTheDocument()
    expect(within(photo).getByRole('link', { name: /Documents/ })).toHaveAttribute('href', '/bt53akj/documents')
  })

  it('links a filed car photo to Photos', async () => {
    mockApi({ chat: [stream(frame('done', { messages: [] }))] })
    renderPanel()

    await sendWith([PHOTO])
    const photo = await findCard('IMG_2041.JPG')
    await userEvent.click(within(photo).getByRole('button', { name: 'File' }))

    await waitFor(() => expect(uploads()).toHaveLength(1))
    expect((uploads()[0]!.init!.body as FormData).get('type')).toBe('CarPhoto')
    expect((uploads()[0]!.init!.body as FormData).get('expenseEntryId')).toBeNull()
    expect(await within(photo).findByRole('link', { name: /Photos/ })).toHaveAttribute('href', '/bt53akj/photos')
  })

  it('shows the existing title when the file is a duplicate', async () => {
    mockApi({
      chat: [stream(frame('done', { messages: [] }))],
      upload: () => json({ title: 'One or more validation errors occurred.', errors: { File: ["This file is already filed as 'After the wash'. The bytes are identical, so it would be the same document twice."] } }, 400),
    })
    renderPanel()

    await sendWith([PHOTO])
    const photo = await findCard('IMG_2041.JPG')
    await userEvent.click(within(photo).getByRole('button', { name: 'File' }))

    expect(await within(photo).findByRole('alert')).toHaveTextContent("already filed as 'After the wash'")
    expect(within(photo).queryByText('Filed')).not.toBeInTheDocument()
  })

  it('shows the plan message when the allowance is spent', async () => {
    mockApi({
      chat: [stream(frame('done', { messages: [] }))],
      upload: () => json({ title: 'One or more validation errors occurred.', errors: { File: ['This account is holding 100 of its 100 documents. Remove one to file another.'] } }, 400),
    })
    renderPanel()

    await sendWith([PHOTO])
    const photo = await findCard('IMG_2041.JPG')
    await userEvent.click(within(photo).getByRole('button', { name: 'File' }))

    expect(await within(photo).findByRole('alert')).toHaveTextContent('holding 100 of its 100 documents')
  })

  it('says how many documents are left when fewer than ten', async () => {
    mockApi({
      chat: [stream(
        frame('filing_offer', { offer: offer({ attachment: 1, allowance: { used: 93, limit: 100 } }) }),
        frame('filing_offer', { offer: offer({ attachment: 2, allowance: { used: 90, limit: 100 } }) }),
        frame('done', { messages: [] }),
      )],
    })
    renderPanel()

    await sendWith([PHOTO, RECEIPT])

    expect(await within(await findCard('IMG_2041.JPG')).findByText('7 left on your plan')).toBeInTheDocument()
    await within(card('euro-car-parts.pdf')).findByText('Suggested by the assistant')
    expect(within(card('euro-car-parts.pdf')).queryByText(/left on your plan/)).not.toBeInTheDocument()
  })
})

describe('Edit and Not now', () => {
  it('Edit opens the upload sheet with the card and the file', async () => {
    mockApi({ chat: [stream(frame('filing_offer', { offer: offer({ attachment: 1 }) }), frame('done', { messages: [] }))] })
    renderPanel()

    await sendWith([RECEIPT])
    const receipt = await findCard('euro-car-parts.pdf')
    await within(receipt).findByText('Suggested by the assistant')
    await userEvent.click(within(receipt).getByRole('button', { name: 'Edit' }))

    const sheet = await screen.findByRole('dialog', { name: 'Upload a document' })
    await waitFor(() => expect(within(sheet).getByRole('combobox', { name: 'Category' })).toHaveValue('PartsReceipt'))
    expect(within(sheet).getByRole('textbox', { name: 'Title' })).toHaveValue('Euro Car Parts - coolant')
    expect(within(sheet).getByLabelText('Dated')).toHaveValue('2026-11-14')
    expect(within(sheet).getByText(/expense · Parts · £63.20/)).toBeInTheDocument()

    await userEvent.click(within(sheet).getByRole('button', { name: 'Upload' }))

    await waitFor(() => expect(uploads()).toHaveLength(1))
    const body = uploads()[0]!.init!.body as FormData
    expect((body.get('file') as File).name).toBe('euro-car-parts.pdf')
    expect(body.get('expenseEntryId')).toBe('412')
    expect(await within(card('euro-car-parts.pdf')).findByText('Filed')).toBeInTheDocument()
  })

  it('Not now removes the card', async () => {
    mockApi({ chat: [stream(frame('done', { messages: [] }))] })
    renderPanel()

    await sendWith([PHOTO])
    await userEvent.click(within(await findCard('IMG_2041.JPG')).getByRole('button', { name: 'Not now' }))

    expect(screen.queryByRole('region', { name: 'Filing card for IMG_2041.JPG' })).not.toBeInTheDocument()
    expect(uploads()).toHaveLength(0)
  })
})

describe('what is kept', () => {
  it('never writes an attachment, its bytes or its name to storage', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem')
    mockApi({
      chat: [stream(frame('filing_offer', { offer: offer({ attachment: 1 }) }), frame('done', { messages: [] }))],
    })
    renderPanel()

    await sendWith([RECEIPT, PHOTO])
    const receipt = await findCard('euro-car-parts.pdf')
    await within(receipt).findByText('Suggested by the assistant')
    await userEvent.click(within(receipt).getByRole('button', { name: 'File' }))
    await within(receipt).findByText('Filed')
    await userEvent.click(within(card('IMG_2041.JPG')).getByRole('button', { name: 'Not now' }))

    for (const [key, value] of setItem.mock.calls) {
      for (const secret of ['IMG_2041', 'euro-car-parts', 'UFJFUEFSRUQ=', 'the original photo bytes', 'Euro Car Parts']) {
        expect(String(key)).not.toContain(secret)
        expect(String(value)).not.toContain(secret)
      }
    }
  })

  it('has no axe violations with two cards open', async () => {
    mockApi({ chat: [stream(frame('filing_offer', { offer: offer({ attachment: 2 }) }), frame('done', { messages: [] }))] })
    const { container } = renderPanel()

    await sendWith([PHOTO, RECEIPT])
    await within(await findCard('euro-car-parts.pdf')).findByText('Suggested by the assistant')

    expect(await axe(container)).toHaveNoViolations()
  })
})
