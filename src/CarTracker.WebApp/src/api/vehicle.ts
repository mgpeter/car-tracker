import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiRequest, type VehicleSummary } from './client'
import type { components } from './generated/schema'
import { ApiFailure, queryKeys } from './queries'

/**
 * One `PATCH /api/vehicles/{reg}` for every editor on the vehicle screen.
 *
 * There are eight of them, and they all do the same thing: send a partial block, invalidate what it moved,
 * toast, close. Eight copies would be eight chances to get the invalidation set wrong, and the two that
 * existed before this hook **already disagreed** - the fuel-tank editor invalidated the vehicle detail and the
 * statutory editor did not. On two separate screens that was invisible. On one merged page it would mean
 * editing the insurer and watching the four insurance rows below it keep the old values.
 *
 * A patch can also move money: `PurchasePrice` re-mirrors the Purchase expense, which moves total outlay,
 * cost-per-mile and the budget. So the invalidation is the **whole `['vehicle', reg]` prefix** rather than a
 * hand-listed set - TanStack matches keys by prefix, and a list is the thing that goes stale when a new
 * screen adds a key.
 */
export function useVehiclePatch(reg: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (body: Record<string, unknown>) => {
      const result = await apiRequest<VehicleSummary>(`/api/vehicles/${encodeURIComponent(reg)}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg] })
      // Not under that prefix: the garage card projects the same summary, so a renamed colour or a corrected
      // purchase price has to reach it too.
      await queryClient.invalidateQueries({ queryKey: queryKeys.garage })
    },
  })
}

/**
 * A null in the PATCH means "unchanged" on every field, because the MCP settings tools leave parameters out
 * and must not wipe what they did not mention. Clearing a stored value goes the other way: the field is left
 * out of its block and named in the root-level `clear` list instead, which the server applies after the merge.
 *
 * This is the hint text for any editable field without its own. A field that was already empty and stays
 * empty is neither sent nor cleared, so an untouched sheet clears nothing.
 */
export const BLANK_CLEARS = 'leave blank to clear it'

/** A stored vehicle input that `PATCH /api/vehicles/{reg}` can set back to null through its `clear` list. */
export type VehicleField = components['schemas']['VehicleField']

/** `GET /api/vehicles/{reg}/deletion-summary` - the weight the confirmation states before it will arm. */
export type VehicleDeletionSummary = components['schemas']['VehicleDeletionSummary']

/** What `DELETE /api/vehicles/{reg}` answers, including any vehicle that became the default in its place. */
export type VehicleDeletedResponse = components['schemas']['VehicleDeletedResponse']

/**
 * Destroys a vehicle and everything filed under it.
 *
 * **The cache is removed, not invalidated**, and that is the whole reason this lives beside `useVehiclePatch`
 * rather than inline in the panel. Every per-vehicle key in the app hangs off `['vehicle', reg]` - the three
 * in `queryKeys`, `anomalyKeys`, the check-definitions key, and the ten hand-built ones across the log screens
 * - so invalidating would refetch a dozen endpoints against a vehicle that no longer exists and paint their
 * 404s on the way out. That is the same reasoning the account deletion applies to its own cache, arrived at
 * the same way.
 *
 * The caller navigates away *before* calling this. A cache entry a live observer is watching refetches the
 * moment it is removed, so removing it while the vehicle screen is still mounted would fire exactly the
 * requests this is avoiding.
 */
export function useVehicleDeletion(reg: string) {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (confirmRegistration: string) => {
      const result = await apiRequest<VehicleDeletedResponse>(`/api/vehicles/${encodeURIComponent(reg)}`, {
        method: 'DELETE',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ confirmRegistration }),
      })
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: () => {
      queryClient.removeQueries({ queryKey: ['vehicle', reg] })
      // The garage is where the caller is going, and it must not still show the card.
      void queryClient.invalidateQueries({ queryKey: queryKeys.garage })
      // The account summary states vehicle, log-entry and document counts, all of which just moved.
      void queryClient.invalidateQueries({ queryKey: queryKeys.account })
    },
  })
}
