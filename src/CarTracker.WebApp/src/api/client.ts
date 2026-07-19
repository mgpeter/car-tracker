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
  | { kind: 'http'; status: number; message: string }

export type ApiResult<T> = { ok: true; value: T } | { ok: false; error: ApiError }

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

  // Omit the header entirely when unset rather than sending an empty one: an absent header is "no
  // credentials offered", which the server answers differently from "here is a wrong key".
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
    // "A vehicle with registration 'BT53 AKJ' already exists" beats "Conflict".
    const detail = await response
      .json()
      .then((body: unknown) =>
        typeof body === 'object' && body !== null && 'detail' in body && typeof body.detail === 'string'
          ? body.detail
          : response.statusText,
      )
      .catch(() => response.statusText)

    return { ok: false, error: { kind: 'http', status: response.status, message: detail } }
  }

  return { ok: true, value: (await response.json()) as T }
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
