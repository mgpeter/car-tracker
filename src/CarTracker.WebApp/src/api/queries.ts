import { QueryClient, useQuery } from '@tanstack/react-query'
import { getGarage, getMeta, getVehicleSummary, type ApiError, type ApiResult } from './client'

/**
 * The query client.
 *
 * `staleTime: 30s` — this is a single-user, self-hosted app whose data changes when *you* change it, so
 * refetching on every mount would be noise. `refetchOnWindowFocus` covers the real case: you logged a fill on
 * your phone at the pump, then came back to the tab on the desk.
 *
 * `retry: false` for 401s and 404s specifically — retrying a wrong API key three times just delays the honest
 * answer by a second and a half.
 */
export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        refetchOnWindowFocus: true,
        retry: (failureCount, error) => {
          if (error instanceof ApiFailure && (error.error.kind === 'unauthorized' || isNotFound(error.error))) {
            return false
          }
          return failureCount < 2
        },
      },
    },
  })
}

const isNotFound = (e: ApiError) => e.kind === 'http' && e.status === 404

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

/** One place the key shapes are decided, so an invalidation cannot miss a cache by a typo. */
export const queryKeys = {
  meta: ['meta'] as const,
  garage: ['garage'] as const,
  vehicleSummary: (reg: string) => ['vehicle', reg, 'summary'] as const,
}

export function useMeta() {
  return useQuery({
    queryKey: queryKeys.meta,
    queryFn: () => unwrap(getMeta()),
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
  })
}
