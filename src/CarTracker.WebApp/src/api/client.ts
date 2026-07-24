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
  | { kind: 'http'; status: number; message: string; errors?: Record<string, string[]> }

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

async function request<T>(url: string, init?: RequestInit): Promise<ApiResult<T>> {
  const { apiKey } = getSettings()

  const headers = new Headers(init?.headers)
  headers.set('Accept', 'application/json')

  // The signed-in user's Auth0 bearer — the web app's auth path. Fetched silently (refresh-token backed, no
  // iframe), so it is a cheap cache read once logged in. A failure here (not logged in yet, token expired
  // mid-flight) sends the request without it and lets the 401 handling below take over.
  if (accessTokenProvider) {
    try {
      const token = await accessTokenProvider()
      if (token) headers.set('Authorization', `Bearer ${token}`)
    } catch (cause) {
      // The request will go unauthenticated and the server answers 401. Surface why the token could not be
      // fetched (missing refresh token, login_required, consent_required) rather than swallowing it — a silent
      // catch here is exactly how "it just says unauthorized" becomes undiagnosable.
      console.warn('[auth] could not obtain an access token; sending request without one:', cause)
    }
  }

  // Legacy: the shared static key. Retained for scripts and break-glass; the web app now authenticates with the
  // bearer above and leaves this empty. Omit the header entirely when unset rather than sending an empty one.
  if (apiKey !== '') {
    headers.set('X-Api-Key', apiKey)
  }

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
    const body: unknown = await response.json().catch(() => null)
    const obj = typeof body === 'object' && body !== null ? (body as Record<string, unknown>) : null
    const detail = obj !== null && typeof obj.detail === 'string' ? obj.detail : response.statusText
    const errors =
      obj !== null && typeof obj.errors === 'object' && obj.errors !== null
        ? (obj.errors as Record<string, string[]>)
        : undefined

    return { ok: false, error: { kind: 'http', status: response.status, message: detail, ...(errors && { errors }) } }
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
