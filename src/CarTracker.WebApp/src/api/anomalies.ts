import { useQuery } from '@tanstack/react-query'
import { apiRequest } from './client'
import type { components } from './generated/schema'
import { ApiFailure } from './queries'

/**
 * A flag from the integrity queue — the **generated** type, not a hand-written one.
 *
 * The integrity screen used to declare its own `interface AnomalyItem` with `kind`, `severity` and `status`
 * widened to `string`, and that is precisely why the fourth detector shipped with no copy for six days: a
 * `Record<string, …>` of per-kind prose cannot fail the build when a kind is missing from it. Off the wire
 * enum, it can — and now does.
 */
export type AnomalyItem = components['schemas']['AnomalyItem']
export type AnomalyKind = components['schemas']['AnomalyKind']

/** What a flag points at. Three today; the detector writes `nameof(T)`, so these are the entity class names. */
export type AnomalyEntityType = 'MileageReading' | 'FuelEntry' | 'EquipmentItem'

/**
 * The queue's key shapes.
 *
 * `all(reg)` is the **prefix** both scopes share, so one `invalidateQueries` after a write clears the open list
 * and the resolved one together. A write that fixes the underlying data retracts its flag inside the same
 * transaction (`AnomalyScanner.Reconcile`), so every screen that can fix a flag has to invalidate this.
 */
export const anomalyKeys = {
  all: (reg: string) => ['vehicle', reg, 'anomalies'] as const,
  list: (reg: string, scope: AnomalyScope) => ['vehicle', reg, 'anomalies', scope] as const,
}

/** `open` is work to do; `all` is history. The queue defaults to the former. */
export type AnomalyScope = 'open' | 'all'

/**
 * The integrity queue.
 *
 * Shared rather than inlined on the one screen that reads it, because the *fix* flow reads it too: a page
 * arriving at `?flag=7` resolves that id against this cache to learn which row to open and why. Two fetches of
 * the same list would be two chances for the banner to describe a flag the queue no longer shows.
 */
export function useAnomalies(reg: string, scope: AnomalyScope, enabled = true) {
  return useQuery({
    queryKey: anomalyKeys.list(reg, scope),
    enabled,
    queryFn: async () => {
      const r = await apiRequest<AnomalyItem[]>(
        `/api/vehicles/${encodeURIComponent(reg)}/anomalies${scope === 'all' ? '?status=all' : ''}`,
      )
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
  })
}
