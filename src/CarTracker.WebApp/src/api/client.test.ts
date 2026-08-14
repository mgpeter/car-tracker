import { afterEach, describe, expect, it, vi } from 'vitest'
import { apiRequest, apiStream } from './client'

/**
 * The client's body-parsing contract.
 *
 * The bug this guards against: `request()` used to call `response.json()` on every 2xx, which throws
 * "Unexpected end of JSON input" on an empty body. Every DELETE returns 204 No Content, and the
 * reference-list renames return an empty 200 — so a *successful* call surfaced a parse error and the
 * deleted-row-that-shows-an-error symptom. The success path must treat an empty body as `undefined`,
 * while still parsing real JSON and still reading the ProblemDetails on a failure body.
 */

function stubFetch(response: Response) {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(response))
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('apiRequest body handling', () => {
  it('treats a 204 No Content (every DELETE) as success without throwing', async () => {
    // A real 204 carries no body; new Response(null, ...) reproduces that exactly.
    stubFetch(new Response(null, { status: 204 }))

    const result = await apiRequest<null>('/api/vehicles/BT53AKJ/fuel/1', { method: 'DELETE' })

    expect(result).toEqual({ ok: true, value: undefined })
  })

  it('treats an empty 200 (a reference-list rename) as success', async () => {
    stubFetch(new Response('', { status: 200 }))

    const result = await apiRequest<unknown>('/api/reference/garages/Old', { method: 'PATCH' })

    expect(result).toEqual({ ok: true, value: undefined })
  })

  it('parses a 200 with a JSON body unchanged', async () => {
    stubFetch(
      new Response(JSON.stringify({ id: 7, registration: 'BT53 AKJ' }), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      }),
    )

    const result = await apiRequest<{ id: number; registration: string }>('/api/vehicles')

    expect(result).toEqual({ ok: true, value: { id: 7, registration: 'BT53 AKJ' } })
  })

  it('surfaces the ProblemDetails message on a non-ok body (guarded delete/rename)', async () => {
    stubFetch(
      new Response(JSON.stringify({ detail: '3 records use this garage' }), {
        status: 409,
        headers: { 'Content-Type': 'application/problem+json' },
      }),
    )

    const result = await apiRequest<null>('/api/reference/garages/InUse', { method: 'DELETE' })

    expect(result).toEqual({
      ok: false,
      error: { kind: 'http', status: 409, message: '3 records use this garage' },
    })
  })
})

/**
 * The event stream, and the frame shape that actually cost something.
 */
describe('apiStream', () => {
  /** One SSE frame, built the way the server writes it: every payload line carries its own `data:` prefix. */
  const frame = (event: string, ...lines: string[]) =>
    [`event: ${event}`, ...lines.map((line) => `data: ${line}`), '', ''].join('\n')

  function respond(body: string): void {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(
            new ReadableStream({
              start(controller) {
                // Two chunks, split mid-frame: a delta arriving in two reads must still be one event.
                const bytes = new TextEncoder().encode(body)
                const cut = Math.floor(bytes.length / 2)
                controller.enqueue(bytes.slice(0, cut))
                controller.enqueue(bytes.slice(cut))
                controller.close()
              },
            }),
            { status: 200, headers: { 'Content-Type': 'text/event-stream' } },
          ),
      ),
    )
  }

  async function collect(body: string): Promise<unknown[]> {
    respond(body)
    const result = await apiStream<unknown>('/api/chat', {})
    if (!result.ok) throw new Error('expected a stream')

    const events: unknown[] = []
    for await (const event of result.value) events.push(event)
    return events
  }

  it('reads a frame split across chunks', async () => {
    const events = await collect(frame('text', '{"delta":"Your MOT runs out on 8 July 2027."}'))

    expect(events).toEqual([{ type: 'text', delta: 'Your MOT runs out on 8 July 2027.' }])
  })

  it('reassembles a payload spread over several data lines', async () => {
    // Not hypothetical. The transcript was first serialised with the AI library's default options, which are
    // indented, so the `done` payload was twenty lines. A parser that read only the first line dropped the
    // event silently, and the failure surfaced three requests later as a 500 from a different endpoint when
    // /confirm answered a suspension the transcript no longer contained.
    const events = await collect(frame('done', '{', '  "messages": []', '}'))

    expect(events).toEqual([{ type: 'done', messages: [] }])
  })

  it('skips a frame it cannot parse rather than ending the stream', async () => {
    const events = await collect(frame('text', 'not json') + frame('done', '{"messages":[]}'))

    expect(events).toEqual([{ type: 'done', messages: [] }])
  })

  it('turns a refusal into an ordinary ApiError rather than a stream', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(
        async () =>
          new Response(JSON.stringify({ detail: 'The account daily chat allowance is spent.' }), {
            status: 429,
            headers: { 'Content-Type': 'application/problem+json' },
          }),
      ),
    )

    const result = await apiStream('/api/chat', {})

    expect(result.ok).toBe(false)
    if (!result.ok && result.error.kind === 'http') {
      expect(result.error.status).toBe(429)
      expect(result.error.message).toContain('allowance is spent')
    }
  })
})
