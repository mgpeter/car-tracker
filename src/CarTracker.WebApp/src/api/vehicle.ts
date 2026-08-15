import { useMutation, useQueryClient } from '@tanstack/react-query'
import { apiRequest, type VehicleSummary } from './client'
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
 * Every field on this screen is **merged, never cleared**: `VehicleUpdateService` applies `patch.X ?? stored.X`
 * to all of them, so an omitted field keeps its value and a `null` does too.
 *
 * This is the hint text for any editable field, and it is written down once because the fuel-tank editor spent
 * two phases promising the opposite - "leave blank to clear it" - after the server stopped honouring it.
 * Clearing a stored value is not possible through this endpoint by anyone, including the assistant.
 */
export const BLANK_LEAVES_STORED = 'blank leaves the stored value - this endpoint cannot clear a field'
