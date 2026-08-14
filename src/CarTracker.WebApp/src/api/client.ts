import type { paths } from './generated/schema'
import { getSettings } from '../lib/settings'

/**
 * Typed fetch over the generated paths.
 *
 * Requests are same-origin: the gateway serves this app at / and proxies /api to the WebApi, in development
 * exactly as in production. That is why there is no base URL and no CORS anywhere (DEC-009).
 *
 * The types come from `src/api/generated/schema.d.ts`, generated from `api-contract/v1.json` — the document
 * the WebApi emits at build time. Rename a C# property and this build breaks, which is the entire point: the
 * derived-metrics service returns figures that are legitimately null (MPG with no previous fill, cost-per-mile
 * at zero miles), and a hand-written interface drifts from the C# in silence. That is the defect class this
 * project exists to eliminate, reintroduced at the wire.
 */

/** Distinguishes the three ways a call can fail, because they need three different messages. */
export type ApiError =
  | { kind: 'unauthorized' }
  | { kind: 'network'; message: string }
  // `errors` is the RFC 9457 field→messages map the server already emits on a 400 (validation problem). It is
  // absent on 404/409/network — a form maps it to its fields, and falls back to `message` when it is missing.
  //
  // `type` is the problem's own identifier, and it is carried because one refusal needs telling apart from
  // every other refusal with the same status: an uninvited sign-in is a 403 that means "ask for an invitation",
  // where every other 403 means "you cannot do that". Reading the status alone would put a stranger in front of
  // a generic error and leave them no idea what to do next.
  | { kind: 'http'; status: number; message: string; type?: string; errors?: Record<string, string[]> }

export type ApiResult<T> = { ok: true; value: T } | { ok: false; error: ApiError }

/**
 * The Auth0 access-token getter, registered once by <AuthBridge> (which has the useAuth0 hook) and read here in
 * a plain module function. Same shape as `getSettings()`: the request layer stays hook-free, and a component
 * that owns the hook feeds it in. Null before login (or in tests that never wire it) — the request just goes
 * without a bearer and the server answers 401, which the query layer already handles.
 */
let accessTokenProvider: (() => Promise<string | null>) | null = null

export function setAccessTokenProvider(provider: (() => Promise<string | null>) | null): void {
  accessTokenProvider = provider
}

/** Every path the API actually has. A typo is a type error rather than a 404 at runtime. */
export type ApiPath = keyof paths

/** The 200 body of a GET, pulled straight off the generated document. */
export type GetResponse<P extends ApiPath> = paths[P] extends {
  get: { responses: { 200: { content: { 'application/json': infer R } } } }
}
  ? R
  : never

/**
 * The credentials every call carries, in one place.
 *
 * Three callers now — JSON, bytes, and the chat's event stream — and the third is what made this a function.
 * A fourth copy of the bearer logic is a fourth place for it to go subtly wrong, and it is the sort of wrong
 * that presents as "it just says unauthorized".
 */
async function authHeaders(init?: HeadersInit): Promise<Headers> {
  const headers = new Headers(init)
  const { apiKey } = getSettings()

  // The signed-in user's Auth0 bearer — the web app's auth path. Fetched silently (refresh-token backed, no
  // iframe), so it is a cheap cache read once logged in. A failure here (not logged in yet, token expired
  // mid-flight) sends the request without it and lets the 401 handling take over.
  if (accessTokenProvider) {
    try {
      const token = await accessTokenProvider()
      if (token) headers.set('Authorization', `Bearer ${token}`)
    } catch (cause) {
      // Surface why the token could not be fetched (missing refresh token, login_required, consent_required)
      // rather than swallowing it — a silent catch here is exactly how "it just says unauthorized" becomes
      // undiagnosable.
      console.warn('[auth] could not obtain an access token; sending request without one:', cause)
    }
  }

  // Legacy: the shared static key. Retained for scripts and break-glass; the web app now authenticates with the
  // bearer above and leaves this empty. Omit the header entirely when unset rather than sending an empty one.
  if (apiKey !== '') headers.set('X-Api-Key', apiKey)

  return headers
}

/** The RFC 9457 body behind a failed response, read once and split into the parts callers need. */
async function problemFrom(response: Response): Promise<ApiError> {
  const body: unknown = await response.json().catch(() => null)
  const obj = typeof body === 'object' && body !== null ? (body as Record<string, unknown>) : null
  const detail = obj !== null && typeof obj.detail === 'string' ? obj.detail : response.statusText
  const type = obj !== null && typeof obj.type === 'string' ? obj.type : undefined
  const errors =
    obj !== null && typeof obj.errors === 'object' && obj.errors !== null
      ? (obj.errors as Record<string, string[]>)
      : undefined

  return { kind: 'http', status: response.status, message: detail, ...(type && { type }), ...(errors && { errors }) }
}

async function request<T>(url: string, init?: RequestInit): Promise<ApiResult<T>> {
  const headers = await authHeaders(init?.headers)
  headers.set('Accept', 'application/json')

  let response: Response
  try {
    response = await fetch(url, { ...init, headers })
  } catch (cause) {
    // fetch only rejects when the request never got an answer — DNS, connection refused, CORS.
    return { ok: false, error: { kind: 'network', message: String(cause) } }
  }

  if (response.status === 401) {
    return { ok: false, error: { kind: 'unauthorized' } }
  }

  if (!response.ok) {
    // The API answers failures with RFC 9457 ProblemDetails, so there is usually a real reason to show —
    // "A vehicle with registration 'BT53 AKJ' already exists" beats "Conflict". A validation 400 additionally
    // carries an `errors` map (field → messages); read the body once and pull out both.
    return { ok: false, error: await problemFrom(response) }
  }

  // A 204 (every DELETE) or an empty 200 (e.g. a reference-list rename) has no body to parse.
  // Calling response.json() on zero bytes throws "Unexpected end of JSON input" even though the
  // request succeeded — the delete-that-shows-an-error bug. Read as text; parse only when present.
  const body = await response.text()
  return { ok: true, value: (body === '' ? undefined : JSON.parse(body)) as T }
}

/** GET a documented path that takes no route parameters. */
export function apiGet<P extends ApiPath>(path: P, init?: RequestInit): Promise<ApiResult<GetResponse<P>>> {
  return request<GetResponse<P>>(path, init)
}

/** Bytes, plus the name the server said to save them under. Null filename when it did not say. */
export interface DownloadedFile {
  blob: Blob
  filename: string | null
}

/**
 * GET raw bytes rather than JSON — the document file endpoint and the account export, and nothing else so far.
 *
 * This exists because **a browser will not send our Authorization header for you.** An `<img src>` or an
 * `<a href>` is a plain navigation: it carries cookies, and this app authenticates with an Auth0 bearer, so
 * pointing an image straight at `/api/.../file` gets a 401 and a broken-image icon. The bytes have to come
 * through the same authenticated fetch seam as everything else and become an object URL on this side.
 *
 * The filename comes back too, because an object URL carries none: a `blob:` href ignores the
 * `Content-Disposition` the server sent, so the save name has to be read out of the response and put on the
 * anchor by hand. Deriving it in the client instead would be a second definition of a format the server
 * already owns — the export's filename carries the *server's* export date, and the two would drift by a day
 * for anyone downloading late in the evening west of UTC.
 */
export async function apiDownload(url: string): Promise<ApiResult<DownloadedFile>> {
  const headers = await authHeaders()

  let response: Response
  try {
    response = await fetch(url, { headers })
  } catch (cause) {
    return { ok: false, error: { kind: 'network', message: String(cause) } }
  }

  if (response.status === 401) return { ok: false, error: { kind: 'unauthorized' } }

  if (!response.ok) return { ok: false, error: await problemFrom(response) }

  return {
    ok: true,
    value: { blob: await response.blob(), filename: filenameFrom(response.headers.get('Content-Disposition')) },
  }
}

/** Just the bytes, for the callers that already know what to call the file (every document). */
export async function apiBlob(url: string): Promise<ApiResult<Blob>> {
  const result = await apiDownload(url)
  return result.ok ? { ok: true, value: result.value.blob } : result
}

/**
 * The save name out of a `Content-Disposition`.
 *
 * `filename*` first — it is the encoded form and wins over the plain one when a server sends both. Anything
 * with a path separator in it is discarded rather than sanitised: a filename is not a path, and the honest
 * answer to a header that disagrees is to fall back to the caller's own name.
 */
function filenameFrom(header: string | null): string | null {
  if (header === null) return null

  const encoded = /filename\*=(?:UTF-8'')?([^;]+)/i.exec(header)?.[1]
  const plain = /filename="?([^";]+)"?/i.exec(header)?.[1]
  const raw = encoded !== undefined ? decodeURIComponent(encoded.trim()) : plain?.trim()

  if (raw === undefined || raw === '' || /[/\\]/.test(raw)) return null
  return raw
}

/**
 * GET a path with its route parameters filled in.
 *
 * The *template* is the type parameter, so the compiler still checks the path exists and still infers the
 * response, while the URL actually sent is the concrete one.
 */
export function apiGetAt<P extends ApiPath>(
  _template: P,
  url: string,
  init?: RequestInit,
): Promise<ApiResult<GetResponse<P>>> {
  return request<GetResponse<P>>(url, init)
}

/** Escape hatch for verbs and shapes the helpers above do not cover yet. */
export const apiRequest = request

export type MetaResponse = GetResponse<'/api/meta'>
export type VehicleSummary = GetResponse<'/api/vehicles/{registration}/summary'>
export type Garage = GetResponse<'/api/vehicles'>
export type GarageItem = Garage[number]
export type ReminderList = GetResponse<'/api/vehicles/{registration}/reminders'>
export type ReminderItem = ReminderList['items'][number]

/** Open — needs no key. Proves the API is reachable. */
export const getMeta = () => apiGet('/api/meta')

/** The garage. An empty array is a real answer — "no cars yet", not an error. */
export const getGarage = () => apiGet('/api/vehicles')

/** Protected — proves the configured key is accepted. */
export const getAuthenticated = () => apiGet('/api/meta/authenticated')

export const getVehicleSummary = (reg: string) =>
  apiGetAt('/api/vehicles/{registration}/summary', `/api/vehicles/${encodeURIComponent(reg)}/summary`)

/**
 * The fired reminders for a vehicle. Derived on read from the same summary the dashboard uses, so the badge
 * count and the dashboard's due state cannot disagree. `includeQuiet` adds the evaluated-but-not-firing rows.
 */
export const getReminders = (reg: string, includeQuiet = false) =>
  apiGetAt(
    '/api/vehicles/{registration}/reminders',
    `/api/vehicles/${encodeURIComponent(reg)}/reminders${includeQuiet ? '?includeQuiet=true' : ''}`,
  )

/**
 * POST something and read a `text/event-stream` back.
 *
 * Neither `request()` nor `apiDownload()` can consume SSE — one awaits `response.text()`, the other
 * `response.blob()`, and both of those are "wait for the whole thing", which is the one property a stream must
 * not have. This is the third consumer of the fetch seam and it lives beside them because that is where the
 * bearer, the 401 and the RFC 9457 body already live, and because tests mock at this seam.
 *
 * **The result is an ApiResult of a stream, not a stream of results.** Everything that can fail before the
 * first byte — a spent budget, a missing key, an expired token, an unreachable provider — is a status code the
 * server has already chosen, so it becomes an ordinary `ApiError` the caller handles like any other. Only once
 * events are flowing does failure become an `error` event, because by then the status line is long gone.
 */
export async function apiStream<T>(url: string, body: unknown): Promise<ApiResult<AsyncIterable<T>>> {
  const headers = await authHeaders()
  headers.set('Accept', 'text/event-stream')
  headers.set('Content-Type', 'application/json')

  let response: Response
  try {
    response = await fetch(url, { method: 'POST', headers, body: JSON.stringify(body) })
  } catch (cause) {
    return { ok: false, error: { kind: 'network', message: String(cause) } }
  }

  if (response.status === 401) return { ok: false, error: { kind: 'unauthorized' } }
  if (!response.ok) return { ok: false, error: await problemFrom(response) }

  if (response.body === null) {
    return { ok: false, error: { kind: 'network', message: 'The response carried no body to read.' } }
  }

  return { ok: true, value: readEvents<T>(response.body) }
}

/**
 * Server-sent events, parsed.
 *
 * Deliberately not `EventSource`: that API is GET-only and cannot carry an Authorization header, which is the
 * same wall `apiDownload` exists for. Frames are separated by a blank line and may split across chunks — the
 * buffer is what makes a delta arriving in two TCP reads still one event rather than two broken ones.
 */
async function* readEvents<T>(stream: ReadableStream<Uint8Array>): AsyncGenerator<T> {
  const reader = stream.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  try {
    for (;;) {
      const { done, value } = await reader.read()
      if (done) break

      buffer += decoder.decode(value, { stream: true })

      let boundary = buffer.indexOf('\n\n')
      while (boundary !== -1) {
        const frame = buffer.slice(0, boundary)
        buffer = buffer.slice(boundary + 2)
        const parsed = parseFrame<T>(frame)
        if (parsed !== null) yield parsed
        boundary = buffer.indexOf('\n\n')
      }
    }
  } finally {
    // Releasing matters on the abandoned path: a panel closed mid-turn leaves the reader locked otherwise.
    reader.releaseLock()
  }
}

/** One frame into `{ type, ...data }` — the shape the panel switches on. Unparseable frames are skipped. */
function parseFrame<T>(frame: string): T | null {
  let event = 'message'
  let data = ''

  for (const line of frame.split('\n')) {
    if (line.startsWith('event:')) event = line.slice(6).trim()
    else if (line.startsWith('data:')) data += line.slice(5).trim()
  }

  if (data === '') return null

  try {
    return { type: event, ...(JSON.parse(data) as object) } as T
  } catch {
    return null
  }
}
