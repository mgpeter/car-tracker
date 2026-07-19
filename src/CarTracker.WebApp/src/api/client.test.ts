import { afterEach, describe, expect, it, vi } from 'vitest'
import { apiRequest } from './client'

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
