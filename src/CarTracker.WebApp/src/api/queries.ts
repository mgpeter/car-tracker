import { QueryClient, useQuery } from '@tanstack/react-query'
import {
  getAuthenticated,
  getGarage,
  getMeta,
  getReminders,
  getVehicle,
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
  // Hand-built in three places before this existed, which is how one of those three came to invalidate it on
  // write and the other did not. A key that can be grepped is a key an invalidation cannot quietly miss.
  vehicleDetail: (reg: string) => ['vehicle', reg, 'detail'] as const,
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
 * `/api/meta/authenticated` needs no vehicle and was originally there to prove a credential is accepted, which
 * is exactly the question here. The invitation refusal is written by `CurrentUserMiddleware` before any handler
 * runs, so any protected endpoint would answer the same; this one costs nothing to ask.
 *
 * It now also carries the account's **plan and allowances**, and that is why: this is the one authenticated call
 * the app already blocks on before rendering anything, so putting them here costs no extra request and no extra
 * wait. `useAllowances` below reads the same cache entry.
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

/**
 * What this account may spend. Reads the `queryKeys.access` entry `AuthGate` has already filled, so a screen
 * asking costs nothing.
 *
 * Undefined while the call is in flight, which every caller must treat as "no" rather than "yes" - see
 * `useChatAvailable`.
 */
export function useAllowances() {
  return useQuery({
    queryKey: queryKeys.access,
    queryFn: () => unwrap(getAuthenticated()),
  }).data?.allowances
}

/**
 * Whether to render an entry point to the assistant.
 *
 * **Two conditions, and they are different facts.** `chatConfigured` says this *deployment* holds a model
 * credential; `chatEnabled` says this *account's plan* includes the assistant. One is on an anonymous response
 * because it describes the server, the other is on an authenticated one because it describes a person.
 *
 * Both are tested `=== true`, so an in-flight answer hides the control rather than offering one that would 503
 * or 403 - the rule the DVLA lookup button follows, and for the same reason: a control that cannot work is not
 * offered.
 *
 * **This will be reversed when checkout ships.** An unentitled account should then see the entry point and be
 * taken to a payment page, because a paywall nobody can see sells nothing. Hiding it is right only while there
 * is nowhere to send them.
 */
export function useChatAvailable(): boolean {
  const configured = useMeta().data?.chatConfigured === true
  const entitled = useAllowances()?.chatEnabled === true

  return configured && entitled
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

/**
 * The vehicle's stored inputs.
 *
 * The counterpart to {@link useVehicleSummary}: that one is everything computed, this one is everything typed.
 * The vehicle screen needs both - the statutory dates the countdowns run on come back on the summary, while
 * the insurer, premium and VED cost behind them are stored here.
 */
export function useVehicleDetail(reg: string) {
  return useQuery({
    queryKey: queryKeys.vehicleDetail(reg),
    queryFn: () => unwrap(getVehicle(reg)),
    enabled: reg !== '',
  })
}

export function useReminders(reg: string) {
  return useQuery({
    queryKey: queryKeys.reminders(reg),
    queryFn: () => unwrap(getReminders(reg)),
  })
}
