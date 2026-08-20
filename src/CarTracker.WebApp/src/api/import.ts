import { apiRequest } from './client'
import type { components } from './generated/schema'
import { ApiFailure } from './queries'

/**
 * The import's shapes, off the wire rather than hand-written.
 *
 * The same reasoning `api/anomalies.ts` records: a hand-declared interface with the counts widened to `number`
 * and the vehicle list widened to `unknown[]` cannot fail the build when the server adds a table to the
 * per-vehicle row counts, so the panel would quietly stop showing one. Generated, it can.
 */
export type ImportPreview = components['schemas']['ImportPreview']
export type ImportVehiclePreview = components['schemas']['ImportVehiclePreview']
export type ImportReport = components['schemas']['ImportReport']
export type ImportRowCounts = components['schemas']['ImportRowCounts']
export type ImportVehicleDecision = components['schemas']['ImportVehicleDecision']

/**
 * Uploads an export and gets back what importing it would do. Writes nothing.
 *
 * `FormData` and no `Content-Type` header: the browser sets it, including the multipart boundary, and setting
 * it by hand produces a body the server cannot parse for a reason nothing in the response explains. The
 * authenticated fetch seam adds the bearer, as it does for every other call.
 */
export async function previewImport(file: File): Promise<ImportPreview> {
  const body = new FormData()
  body.append('file', file)

  const result = await apiRequest<ImportPreview>('/api/account/import/preview', { method: 'POST', body })
  if (!result.ok) throw new ApiFailure(result.error)

  return result.value
}

/**
 * Writes a previewed import.
 *
 * The decisions and nothing else. **The file is not re-sent**: the server is already holding it, and a commit
 * that carried its own payload would validate the request against itself - the mistake the chat's confirm
 * endpoint was designed around, and the reason `importId` is opaque and server-held.
 */
export async function commitImport(
  importId: string,
  vehicles: ImportVehicleDecision[],
): Promise<ImportReport> {
  const result = await apiRequest<ImportReport>(`/api/account/import/${encodeURIComponent(importId)}/commit`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ vehicles }),
  })
  if (!result.ok) throw new ApiFailure(result.error)

  return result.value
}

/**
 * Whether a failure means "that preview is gone" rather than "that import is wrong".
 *
 * The distinction is the whole reason the panel does not simply show an error: a preview lives in memory for
 * fifteen minutes and a container restart forgets it, so the honest response is "upload it again" rather than
 * a dead button beside a stale panel.
 */
export function isExpiredPreview(error: unknown): boolean {
  return error instanceof ApiFailure && error.error.kind === 'http' && error.error.status === 404
}

/** Whether a failure is a registration clash, which is corrected in place rather than re-uploaded. */
export function isRegistrationClash(error: unknown): boolean {
  return error instanceof ApiFailure && error.error.kind === 'http' && error.error.status === 409
}
