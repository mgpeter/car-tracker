import { getSettings } from '../lib/settings'

/**
 * Typed fetch wrapper.
 *
 * Requests are same-origin: the gateway serves this app at / and proxies /api to the WebApi, in development
 * exactly as in production. That is why there is no base URL and no CORS anywhere (DEC-009).
 */

/** Distinguishes the three ways a call can fail, because they need three different messages. */
export type ApiError =
  | { kind: 'unauthorized' }
  | { kind: 'network'; message: string }
  | { kind: 'http'; status: number; message: string }

export type ApiResult<T> = { ok: true; value: T } | { ok: false; error: ApiError }

export async function apiFetch<T>(path: string, init?: RequestInit): Promise<ApiResult<T>> {
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
    response = await fetch(path, { ...init, headers })
  } catch (cause) {
    // fetch only rejects when the request never got an answer — DNS, connection refused, CORS.
    return { ok: false, error: { kind: 'network', message: String(cause) } }
  }

  if (response.status === 401) {
    return { ok: false, error: { kind: 'unauthorized' } }
  }

  if (!response.ok) {
    return {
      ok: false,
      error: { kind: 'http', status: response.status, message: response.statusText },
    }
  }

  return { ok: true, value: (await response.json()) as T }
}

/**
 * Shapes are hand-written for now. Per the react-app-foundation spec they will be generated from the API's
 * OpenAPI document (`npm run gen:api`), so a C# rename breaks this build instead of shipping an undefined.
 */
export interface MetaResponse {
  applicationName: string
  version: string
  environment: string
  serverTimeUtc: string
}

export interface AuthenticatedResponse {
  authenticated: boolean
}

/** Open — needs no key. Proves the API is reachable. */
export const getMeta = () => apiFetch<MetaResponse>('/api/meta')

/** Protected — proves the configured key is accepted. */
export const getAuthenticated = () => apiFetch<AuthenticatedResponse>('/api/meta/authenticated')
