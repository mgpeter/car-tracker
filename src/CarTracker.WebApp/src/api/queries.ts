import { QueryClient, useQuery } from '@tanstack/react-query'
import {
  getAuthenticated,
  getGarage,
  getMeta,
  getReminders,
  getVehicleSummary,
  type ApiError,
  type ApiResult,
} from './client'

/**
 * The query client.
 *
 * `staleTime: 30s` — every account's data changes only when *that account* changes it (a vehicle belongs to one
 * owner; nothing is shared), so refetching on every mount would be noise. `refetchOnWindowFocus` covers the real
 * case: you logged a fill on your phone at the pump, then came back to the tab on the desk.
 *
 * `retry: false` for 401s, 403s and 404s specifically — retrying a wrong API key three times just delays the
 * honest answer by a second and a half, and a refusal about *who you are* answers the same way however many
 * times it is asked.
 */
export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        refetchOnWindowFocus: true,
        retry: (failureCount, error) => {
          if (
            error instanceof ApiFailure &&
            (error.error.kind === 'unauthorized' || isForbidden(error.error) || isNotFound(error.error))
          ) {
            return false
          }
          return failureCount < 2
        },
      },
    },
  })
}

const isNotFound = (e: ApiError) => e.kind === 'http' && e.status === 404

// Added with the invitation door: an uninvited sign-in is refused identically every time, and three retries
// would only make the login wall sit on a splash for a second and a half before saying so.
const isForbidden = (e: ApiError) => e.kind === 'http' && e.status === 403

/**
 * TanStack Query signals failure by rejection, but `apiFetch` returns a discriminated result — deliberately,
 * because a 401 and a dead server are different answers and an exception flattens them into one. This carries
 * the discriminant across the boundary rather than losing it.
 */
export class ApiFailure extends Error {
  // An explicit field, not a constructor parameter property: `erasableSyntaxOnly` is on, and a parameter
  // property is TypeScript that has to be *compiled* rather than stripped.
  readonly error: ApiError

  constructor(error: ApiError) {
    super(error.kind === 'network' ? error.message : error.kind === 'unauthorized' ? 'Unauthorized' : error.message)
    this.name = 'ApiFailure'
    this.error = error
  }
}

async function unwrap<T>(result: Promise<ApiResult<T>>): Promise<T> {
  const r = await result
  if (!r.ok) throw new ApiFailure(r.error)
  return r.value
}

/**
 * The RFC 9457 `type` the server refuses an uninvited sign-in under (`SignupPolicy.NotInvitedProblemType`).
 * Matched exactly, and it is the only reason this app reads a problem type at all.
 */
export const NOT_INVITED = 'signup-not-invited'

/**
 * Whether a failure is "your address is not on the invitation list" rather than any other refusal.
 *
 * Status alone will not do: a 403 also means an assistant token reaching somewhere it may not, and telling a
 * newcomer they are forbidden — with no mention of an invitation — leaves them nothing to act on.
 */
export function isNotInvited(error: unknown): boolean {
  return error instanceof ApiFailure && error.error.kind === 'http' && error.error.type === NOT_INVITED
}

/** One place the key shapes are decided, so an invalidation cannot miss a cache by a typo. */
export const queryKeys = {
  meta: ['meta'] as const,
  access: ['access'] as const,
  account: ['account', 'summary'] as const,
  garage: ['garage'] as const,
  vehicleSummary: (reg: string) => ['vehicle', reg, 'summary'] as const,
  reminders: (reg: string) => ['vehicle', reg, 'reminders'] as const,
}

export function useMeta() {
  return useQuery({
    queryKey: queryKeys.meta,
    queryFn: () => unwrap(getMeta()),
  })
}

/**
 * One authenticated call made *above the router*, so the login wall can tell an admitted account from a signed-in
 * stranger before a single screen mounts.
 *
 * `/api/meta/authenticated` carries no data and needs no vehicle — it exists to prove a credential is accepted,
 * which is exactly the question here. The invitation refusal is written by `CurrentUserMiddleware` before any
 * handler runs, so any protected endpoint would answer the same; this one costs nothing to ask.
 */
export function useAccessCheck(enabled: boolean) {
  return useQuery({
    queryKey: queryKeys.access,
    queryFn: () => unwrap(getAuthenticated()),
    enabled,
    // No retries, unlike every other query here. Only one answer to this call changes what renders — the
    // invitation refusal, which never retries anyway — and every other failure lets the app through. Retrying
    // would hold the whole app on a splash through two backoffs to reach a conclusion already decided.
    retry: false,
  })
}

export function useGarage() {
  return useQuery({
    queryKey: queryKeys.garage,
    queryFn: () => unwrap(getGarage()),
  })
}

export function useVehicleSummary(reg: string) {
  return useQuery({
    queryKey: queryKeys.vehicleSummary(reg),
    queryFn: () => unwrap(getVehicleSummary(reg)),
    // Every screen passes a real registration; the assistant's dock is rendered on the garage too, where there
    // is none, and a hook cannot be called conditionally. Without this it would ask for `/api/vehicles//summary`
    // and take a 404 for an answer it never wanted.
    enabled: reg !== '',
  })
}

export function useReminders(reg: string) {
  return useQuery({
    queryKey: queryKeys.reminders(reg),
    queryFn: () => unwrap(getReminders(reg)),
  })
}
