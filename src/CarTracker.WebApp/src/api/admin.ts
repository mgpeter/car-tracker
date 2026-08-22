import type { components } from './generated/schema'
import { apiRequest } from './client'
import { ApiFailure } from './queries'

export type AdminUserRow = components['schemas']['AdminUserRow']
export type AdminUserList = components['schemas']['AdminUserList']
export type AdminUserDetail = components['schemas']['AdminUserDetail']
export type AdminUsageResponse = components['schemas']['AdminUsageResponse']
export type AdminDiagnosticsResponse = components['schemas']['AdminDiagnosticsResponse']
export type AccountPlan = components['schemas']['AccountPlan']

/**
 * Query keys for the operator surface.
 *
 * Separate from `queryKeys` in `queries.ts` because nothing outside this screen reads them and the shared file
 * is the place for keys several screens invalidate. The plan write invalidates the whole `['admin']` prefix:
 * changing a tier moves the row, and it moves the account's own plan too, so a narrower key would leave one of
 * the two stale.
 */
export const adminKeys = {
  all: ['admin'] as const,
  users: ['admin', 'users'] as const,
  user: (id: number) => ['admin', 'users', id] as const,
  usage: ['admin', 'usage'] as const,
  diagnostics: ['admin', 'diagnostics'] as const,
}

async function get<T>(url: string): Promise<T> {
  const result = await apiRequest<T>(url)
  if (!result.ok) throw new ApiFailure(result.error)
  return result.value
}

export const getAdminUsers = () => get<AdminUserList>('/api/admin/users')
export const getAdminUser = (id: number) => get<AdminUserDetail>(`/api/admin/users/${id}`)
export const getAdminUsage = () => get<AdminUsageResponse>('/api/admin/usage')
export const getAdminDiagnostics = () => get<AdminDiagnosticsResponse>('/api/admin/diagnostics')

/** Pin an account to a tier. Returns the row as it now resolves, so one row refreshes rather than the list. */
export async function setPlanOverride(id: number, plan: AccountPlan): Promise<AdminUserRow> {
  const result = await apiRequest<AdminUserRow>(`/api/admin/users/${id}/plan`, {
    method: 'PUT',
    // `request()` sets Accept centrally and leaves Content-Type to the call site, so every JSON write in this
    // app declares it by hand and this one did not. `SetPlanOverrideRequest` is an inferred body parameter, so
    // without the header the binder refuses the request 415 before the handler runs - an empty-bodied failure
    // that reads as a server fault rather than a missing header.
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ plan }),
  })
  if (!result.ok) throw new ApiFailure(result.error)
  return result.value
}

/** Clear the override so the account falls back to the comp list. A different operation from setting Free. */
export async function clearPlanOverride(id: number): Promise<AdminUserRow> {
  const result = await apiRequest<AdminUserRow>(`/api/admin/users/${id}/plan`, { method: 'DELETE' })
  if (!result.ok) throw new ApiFailure(result.error)
  return result.value
}
