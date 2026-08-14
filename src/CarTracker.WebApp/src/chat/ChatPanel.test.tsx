import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createQueryClient } from '../api/queries'
import { IconSprite } from '../components/IconSprite'
import { axe } from '../test/axe'
import { ChatPanel } from './ChatPanel'

/** One SSE frame, exactly as `ChatEndpoints` writes it. */
const frame = (event: string, data: unknown) => `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`

/**
 * A response that streams — the point of the exercise.
 *
 * The frames are pushed as **separate chunks**, and one of them is deliberately split down the middle, because
 * the buffering in `readEvents` exists for exactly that: a delta arriving in two TCP reads must still be one
 * event rather than two broken ones, and a fake that always delivers whole frames would never notice.
 */
function stream(...frames: string[]): Response {
  const text = frames.join('')
  const cut = Math.floor(text.length / 2)
  const encoder = new TextEncoder()

  return new Response(
    new ReadableStream({
      start(controller) {
        controller.enqueue(encoder.encode(text.slice(0, cut)))
        controller.enqueue(encoder.encode(text.slice(cut)))
        controller.close()
      },
    }),
    { status: 200, headers: { 'Content-Type': 'text/event-stream' } },
  )
}

const DRAFT = {
  pendingWriteId: 'pw_test',
  tool: 'add_service',
  title: 'Add a service record',
  arguments: { serviceDate: '2026-07-08', mileage: 80705, garage: 'K&P Motors' },
  schema: {
    properties: {
      serviceDate: { type: 'string', description: 'The date the work was done.' },
      mileage: { type: 'integer' },
      garage: { type: 'string' },
    },
    required: ['serviceDate'],
  },
}

/** Records every request so a test can assert what the confirm actually sent. */
let sent: { url: string; body: unknown }[] = []

function mockChat(...responses: Response[]) {
  let next = 0
  vi.stubGlobal(
    'fetch',
    vi.fn(async (url: string | URL, init?: RequestInit) => {
      const href = String(url)

      // The reference lists the draft card's comboboxes read.
      if (href.includes('/api/reference/')) {
        return new Response('[]', { status: 200, headers: { 'Content-Type': 'application/json' } })
      }

      sent.push({ url: href, body: init?.body === undefined ? null : JSON.parse(String(init.body)) })

      return responses[next++] ?? stream(frame('done', { messages: [] }))
    }),
  )
}

beforeEach(() => {
  sent = []
  localStorage.clear()
})

afterEach(() => vi.unstubAllGlobals())

const renderPanel = (vehicle: string | null = 'BT53 AKJ') =>
  render(
    <QueryClientProvider client={createQueryClient()}>
      <IconSprite />
      <ChatPanel vehicle={vehicle} variant="dock" onClose={() => {}} />
    </QueryClientProvider>,
  )

describe('ChatPanel', () => {
  it('says what it is for before anyone has asked anything', () => {
    mockChat()
    renderPanel()

    expect(screen.getByText(/Nothing is saved until you press Save/)).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Message' })).toBeInTheDocument()
  })

  it('streams the answer into the transcript', async () => {
    mockChat(
      stream(
        frame('text', { delta: 'Your MOT runs out ' }),
        frame('text', { delta: 'on 8 July 2027.' }),
        frame('done', { messages: [{ role: 'assistant', contents: [] }] }),
      ),
    )

    renderPanel()
    await userEvent.type(screen.getByRole('textbox', { name: 'Message' }), 'When is the MOT due?')
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    // Assembled from two deltas split across chunk boundaries, which is the whole reason for the buffer.
    expect(await screen.findByText('Your MOT runs out on 8 July 2027.')).toBeInTheDocument()
    expect(screen.getByText('When is the MOT due?')).toBeInTheDocument()
  })

  it('narrates a read tool rather than leaving a silent pause', async () => {
    mockChat(
      stream(
        frame('tool', { name: 'get_due_items', status: 'running' }),
        frame('text', { delta: 'Two things.' }),
        frame('done', { messages: [] }),
      ),
    )

    renderPanel()
    await userEvent.type(screen.getByRole('textbox', { name: 'Message' }), 'What needs attention?')
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    expect(await screen.findByText('get due items')).toBeInTheDocument()
  })

  it('renders a proposed write as a form built from the tool schema', async () => {
    mockChat(stream(frame('pending_write', DRAFT), frame('done', { messages: [] })))

    renderPanel()
    await userEvent.type(screen.getByRole('textbox', { name: 'Message' }), 'Log the MOT')
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    // Every field comes from the schema, labelled from its own property name, filled with what was proposed.
    expect(await screen.findByLabelText('Service date')).toHaveValue('2026-07-08')
    expect(screen.getByLabelText('Mileage')).toHaveValue('80705')
    expect(screen.getByLabelText('Garage')).toHaveValue('K&P Motors')
    expect(screen.getByText('The date the work was done.')).toBeInTheDocument()
  })

  it('leads with what was read and folds the rest away', async () => {
    // add_vehicle has fourteen optional parameters. A card that opens with eleven empty boxes buries the three
    // figures the owner is actually here to check.
    const sparse = {
      ...DRAFT,
      arguments: { serviceDate: '2026-07-08' },
      schema: {
        properties: {
          serviceDate: { type: 'string' },
          mileage: { type: 'integer' },
          garage: { type: 'string' },
        },
        required: ['serviceDate'],
      },
    }

    mockChat(stream(frame('pending_write', sparse), frame('done', { messages: [] })))

    renderPanel()
    await userEvent.type(screen.getByRole('textbox', { name: 'Message' }), 'Log the MOT')
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    expect(await screen.findByLabelText('Service date')).toHaveValue('2026-07-08')

    // Present and reachable, not absent: an unfilled field is one the owner may want to fill.
    const more = screen.getByText('2 more fields')
    expect(screen.getByLabelText('Mileage')).toHaveValue('')
    await userEvent.click(more)
    expect(screen.getByLabelText('Garage')).toBeVisible()
  })

  it('sends what the owner corrected, not what the model proposed', async () => {
    mockChat(
      stream(frame('pending_write', DRAFT), frame('done', { messages: [] })),
      stream(frame('text', { delta: 'Saved.' }), frame('done', { messages: [] })),
    )

    renderPanel()
    await userEvent.type(screen.getByRole('textbox', { name: 'Message' }), 'Log the MOT')
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    const mileage = await screen.findByLabelText('Mileage')
    await userEvent.clear(mileage)
    await userEvent.type(mileage, '80,705')

    await userEvent.click(screen.getByRole('button', { name: 'Save it' }))

    await waitFor(() => expect(sent.some((r) => r.url.endsWith('/confirm'))).toBe(true))

    const confirm = sent.find((r) => r.url.endsWith('/confirm'))!.body as {
      pendingWriteId: string
      arguments: Record<string, unknown>
      tool?: string
    }

    expect(confirm.pendingWriteId).toBe('pw_test')
    // Coerced through the schema's integer, separators and all — and the whole point of the card.
    expect(confirm.arguments.mileage).toBe(80705)
    // There is no tool field to send: the server reads it from its own store, which is what makes the id an
    // authorisation rather than a suggestion.
    expect(confirm.tool).toBeUndefined()
  })

  it('answers a discarded draft rather than dropping it', async () => {
    mockChat(
      stream(frame('pending_write', DRAFT), frame('done', { messages: [] })),
      stream(frame('text', { delta: 'Nothing saved.' }), frame('done', { messages: [] })),
    )

    renderPanel()
    await userEvent.type(screen.getByRole('textbox', { name: 'Message' }), 'Log the MOT')
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    // Two-step, like every other destructive control in the app.
    const discard = await screen.findByRole('button', { name: 'Discard' })
    await userEvent.click(discard)
    await userEvent.click(screen.getByRole('button', { name: /Discard it/ }))

    // An unanswered approval breaks the transcript for every later turn, so the refusal is a request.
    await waitFor(() => expect(sent.some((r) => r.url.endsWith('/decline'))).toBe(true))
    expect(await screen.findByText(/Discarded/)).toBeInTheDocument()
  })

  it('shows the server’s own sentence when a turn is refused', async () => {
    // A spent allowance says when it resets; an unconfigured deployment says which setting turns it on. Both
    // are more use than the status word, so the detail is rendered as written.
    mockChat(
      new Response(
        JSON.stringify({ detail: 'The account daily chat allowance is spent. It resets at 00:00 on 15 August.' }),
        { status: 429, headers: { 'Content-Type': 'application/problem+json' } },
      ),
    )

    renderPanel()
    await userEvent.type(screen.getByRole('textbox', { name: 'Message' }), 'Hello')
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('resets at 00:00')
  })

  it('has no axe violations, empty or with a draft open', async () => {
    mockChat(stream(frame('pending_write', DRAFT), frame('done', { messages: [] })))

    const { container } = renderPanel()
    expect(await axe(container)).toHaveNoViolations()

    await userEvent.type(screen.getByRole('textbox', { name: 'Message' }), 'Log the MOT')
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))
    await screen.findByLabelText('Service date')

    expect(await axe(container)).toHaveNoViolations()
  })
})
