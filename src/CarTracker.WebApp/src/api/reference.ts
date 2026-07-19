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
