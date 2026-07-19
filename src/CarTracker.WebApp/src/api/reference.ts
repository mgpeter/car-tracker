import { useQuery } from '@tanstack/react-query'
import { apiRequest } from './client'
import { ApiFailure } from './queries'

/** The three name-keyed reference lists, by their URL segment. */
export type ReferenceKind = 'garages' | 'wash-locations' | 'expense-categories'

/**
 * A reference-list row. Garages and wash-locations carry `contact`/`notes`; categories carry the system/mirror
 * flags. `referenceCount` is on all three — how many records point at this name, i.e. how "used" it is.
 */
export interface ReferenceRow {
  name: string
  referenceCount: number
  contact?: string | null
  notes?: string | null
  isSystem?: boolean
  isMirrorOnly?: boolean
}

/**
 * The reference list for a kind. Shares its query key with the settings editor and the expense sheet, so a
 * combobox that reads it and a panel that edits it stay one cache — no second fetch, no drift.
 */
export function useReferenceList(kind: ReferenceKind) {
  return useQuery({
    queryKey: ['reference', kind] as const,
    queryFn: async () => {
      const r = await apiRequest<ReferenceRow[]>(`/api/reference/${kind}`)
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
  })
}

/** Reference names as combobox suggestions, most-used first, with the use-count as the hint. */
export function useReferenceSuggestions(kind: ReferenceKind): { value: string; hint?: string }[] {
  const { data } = useReferenceList(kind)
  const rows = Array.isArray(data) ? data : []
  return rows
    .slice()
    .sort((a, b) => b.referenceCount - a.referenceCount)
    .map((r) => ({ value: r.name, ...(r.referenceCount > 0 && { hint: `${r.referenceCount}×` }) }))
}

/** One generic starter check, as the add-vehicle sheet presents it — cadence is display-only there. */
export interface StarterCheck {
  name: string
  cadenceLabel: string
  intervalDays: number
  guidance: string | null
}

/**
 * The generic starter set the add-vehicle sheet offers for selection. Reads the same list `VehicleFactory`
 * applies on create (`GET /api/reference/starter-checks`), so the picker cannot show a check create would not
 * make. Static within a session — cached indefinitely.
 */
export function useStarterChecks(enabled = true) {
  return useQuery({
    queryKey: ['reference', 'starter-checks'] as const,
    enabled,
    staleTime: Infinity,
    queryFn: async () => {
      const r = await apiRequest<StarterCheck[]>('/api/reference/starter-checks')
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
  })
}

/** A stored check definition — enough to preview another vehicle's checks for copying, and its own for locking. */
export interface VehicleCheck {
  id: number
  name: string
  cadenceLabel: string
  intervalDays: number
  guidance: string | null
  displayOrder: number
  isActive: boolean
}

/**
 * A vehicle's own check definitions (`GET /checks/definitions`, retired included). Used to preview a *source*
 * vehicle's checks when copying, and to lock the checks a *target* vehicle already has. Shares its key with the
 * settings panel's own definitions query so the two stay one cache.
 */
export function useVehicleChecks(reg: string, enabled = true) {
  return useQuery({
    queryKey: ['vehicle', reg, 'check-definitions'] as const,
    enabled: enabled && reg !== '',
    queryFn: async () => {
      const r = await apiRequest<VehicleCheck[]>(`/api/vehicles/${encodeURIComponent(reg)}/checks/definitions`)
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
  })
}
