import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { makeRenditions, type Renditions } from '../lib/renditions'
import { apiBlob, apiGetAt, apiRequest, type ApiResult } from './client'
import type { components } from './generated/schema'
import { ApiFailure, queryKeys } from './queries'

type Schemas = components['schemas']

export type DocumentItem = Schemas['DocumentItem']
export type DocumentLog = Schemas['DocumentLog']
export type DocumentType = Schemas['DocumentType']
export type DocumentGroup = Schemas['DocumentGroup']
export type DocumentKind = Schemas['DocumentKind']
export type DocumentTypeInfo = Schemas['DocumentTypeInfo']
export type DocumentVariant = NonNullable<Schemas['DocumentVariant']>
export type DocumentLinkKind = Schemas['DocumentLinkKind']

/** A record a document can be attached to, by kind and id; the label is for display only. */
export interface DocumentLinkRef {
  kind: DocumentLinkKind
  id: number
  label?: string
}

/**
 * Group headings. Typed off the generated enum, so a group added on the server fails `tsc -b` here rather than
 * rendering blank (assumed: A11). A group this build has never heard of still renders, under its raw name: see
 * {@link groupLabel}.
 */
export const GROUP_LABEL: Record<DocumentGroup, string> = {
  Ownership: 'Ownership',
  LegalAndCover: 'Legal & cover',
  WorkAndParts: 'Work & parts',
  Reference: 'Reference',
  Photos: 'Photos',
  Other: 'Other',
}

/** The heading for a group, or the raw name for one the server added after this build. */
export function groupLabel(group: string): string {
  return (GROUP_LABEL as Record<string, string | undefined>)[group] ?? group
}

/** What an owner calls a category, from the server's table; the enum name until that table has loaded. */
export function categoryLabel(type: string, types: readonly DocumentTypeInfo[] | undefined): string {
  return types?.find((t) => t.type === type)?.label ?? type
}

/** Paper or photo, from the server's table. Undefined until it has loaded. */
export function kindOf(type: string, types: readonly DocumentTypeInfo[] | undefined): DocumentKind | undefined {
  return types?.find((t) => t.type === type)?.kind
}

/** Group names in the order the server lists them, including any this build has never heard of. */
export function groupsOf(types: readonly DocumentTypeInfo[]): string[] {
  return [...new Set(types.map((t) => t.group as string))]
}

/** The first paper category: the default wherever a paper is the likelier thing being filed. */
export function firstPaper(types: readonly DocumentTypeInfo[] | undefined): DocumentType | undefined {
  return types?.find((t) => t.kind === 'Paper')?.type
}

/** The noun a link chip uses for each record kind. */
export const LINK_LABEL: Record<DocumentLinkKind, string> = {
  ServiceRecord: 'service record',
  Expense: 'expense',
  Issue: 'issue',
}

const vehiclePath = (reg: string) => `/api/vehicles/${encodeURIComponent(reg)}`

/**
 * Where a document's bytes are. The original by default; a rendition by the contract's own enum, which the
 * endpoint binds case-sensitively (assumed: A21).
 */
export function fileUrl(reg: string, id: number, variant: DocumentVariant = 'Original'): string {
  const base = `${vehiclePath(reg)}/documents/${id}/file`
  return variant === 'Original' ? base : `${base}?variant=${variant}`
}

/** The car's documents: papers and photos, one cached fetch for every screen and strip that shows them. */
export function useDocuments(reg: string) {
  return useQuery({
    queryKey: queryKeys.documents(reg),
    queryFn: async () => {
      const result = await apiGetAt('/api/vehicles/{registration}/documents', `${vehiclePath(reg)}/documents`)
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    enabled: reg !== '',
  })
}

/** The category table. It changes only with a deploy, so it is fetched once per session. */
export function useDocumentTypes() {
  return useQuery({
    queryKey: queryKeys.documentTypes,
    queryFn: async () => {
      const result = await apiRequest<DocumentTypeInfo[]>('/api/document-types')
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    staleTime: Infinity,
  })
}

/**
 * Fetches one variant of a document through the authenticated seam and hands back a temporary object URL, or
 * null until it arrives (and for good when it fails).
 *
 * The revoke on unmount is not tidiness: an object URL pins its blob for the life of the page, so a grid that
 * never revoked would hold every image it had ever shown.
 */
export function useDocumentObjectUrl(
  reg: string,
  id: number,
  variant: DocumentVariant = 'Original',
  enabled = true,
): string | null {
  const [url, setUrl] = useState<string | null>(null)

  useEffect(() => {
    if (!enabled) return
    let revoked = false
    let objectUrl: string | null = null

    void apiBlob(fileUrl(reg, id, variant)).then((result) => {
      if (!result.ok || revoked) return
      objectUrl = URL.createObjectURL(result.value)
      setUrl(objectUrl)
    })

    return () => {
      revoked = true
      if (objectUrl !== null) URL.revokeObjectURL(objectUrl)
      setUrl(null)
    }
  }, [reg, id, variant, enabled])

  return url
}

/** Opens a document's original in a tab, or saves it under its title. */
export async function openDocument(reg: string, document: Pick<DocumentItem, 'id' | 'title'>, download: boolean) {
  const result = await apiBlob(fileUrl(reg, document.id))
  if (!result.ok) return

  const url = URL.createObjectURL(result.value)
  if (download) {
    const anchor = window.document.createElement('a')
    anchor.href = url
    // The saved name comes from the title: the uploaded filename is not stored, because the file lands on disk
    // named for its own hash and a client-supplied name is not a safe path component.
    anchor.download = document.title
    anchor.click()
  } else {
    window.open(url, '_blank', 'noopener')
  }

  // Long enough for the tab or the save to have taken the bytes, then let them go.
  window.setTimeout(() => URL.revokeObjectURL(url), 60_000)
}

/** What travels with an upload besides the file. */
export interface UploadMeta {
  type: DocumentType
  title: string
  documentDate?: string | null
  notes?: string | null
  link?: DocumentLinkRef | null
}

const LINK_FIELD: Record<DocumentLinkKind, string> = {
  ServiceRecord: 'serviceRecordId',
  Expense: 'expenseEntryId',
  Issue: 'issueId',
}

function appendRenditions(body: FormData, renditions: Renditions) {
  body.append('thumbnail', renditions.thumbnail, 'thumbnail')
  body.append('display', renditions.display, 'display')
  body.append('width', String(renditions.width))
  body.append('height', String(renditions.height))
}

/**
 * Files one document: makes its renditions when it is an image this browser can decode, and posts the lot.
 *
 * No React here, so the Photos screen's multi-upload, the attachment strips and a chat card can all call it
 * (assumed: A13). An image the browser cannot decode still uploads, just without previews; the backfill fills
 * them in later from a browser that can.
 */
export async function uploadDocument(reg: string, file: File, meta: UploadMeta): Promise<ApiResult<DocumentItem>> {
  const renditions = await makeRenditions(file)

  const body = new FormData()
  body.append('file', file)
  body.append('title', meta.title)
  body.append('type', meta.type)
  if (meta.documentDate) body.append('documentDate', meta.documentDate)
  if (meta.notes) body.append('notes', meta.notes)
  if (meta.link) body.append(LINK_FIELD[meta.link.kind], String(meta.link.id))
  if (renditions !== null) appendRenditions(body, renditions)

  // No Content-Type header: the browser must set it, because only it knows the multipart boundary.
  return apiRequest<DocumentItem>(`${vehiclePath(reg)}/documents`, { method: 'POST', body })
}

/** Sends renditions for a document filed without them. */
export function postRenditions(reg: string, id: number, renditions: Renditions): Promise<ApiResult<DocumentItem>> {
  const body = new FormData()
  appendRenditions(body, renditions)
  return apiRequest<DocumentItem>(`${vehiclePath(reg)}/documents/${id}/renditions`, { method: 'POST', body })
}

// ---- backfill ------------------------------------------------------------------------------------------------

/**
 * Document ids already tried this session, whatever came of it. In memory only: a reload tries once more,
 * which is harmless, and a storage key would reopen the cookie-consent question for nothing (assumed: A14).
 */
const attempted = new Set<number>()
const waiting: Array<() => Promise<void>> = []
let running = 0
const CONCURRENCY = 2

function schedule(job: () => Promise<void>) {
  waiting.push(job)
  pump()
}

function pump() {
  while (running < CONCURRENCY && waiting.length > 0) {
    const job = waiting.shift()!
    running += 1
    void job()
      .catch(() => {})
      .finally(() => {
        running -= 1
        pump()
      })
  }
}

/** For tests: forget what was attempted and drop anything queued. */
export function resetRenditionBackfill() {
  attempted.clear()
  waiting.length = 0
  running = 0
}

/** An image filed without previews, which this browser might be able to make. */
const needsRenditions = (d: DocumentItem) => d.contentType.startsWith('image/') && !d.hasThumbnail

/**
 * Fills in previews for image documents filed without them: a photo filed before renditions existed, or an
 * HEIC uploaded from a desktop browser that could not decode it. Fetches the original, makes both renditions
 * if this browser can decode it, and posts them. Two at a time, once per document per session.
 */
export function useRenditionBackfill(reg: string, documents: readonly DocumentItem[] | undefined) {
  const queryClient = useQueryClient()
  const pending = (documents ?? []).filter((d) => needsRenditions(d) && !attempted.has(d.id)).map((d) => d.id)
  const key = pending.join(',')

  useEffect(() => {
    if (key === '') return
    for (const id of key.split(',').map(Number)) {
      if (attempted.has(id)) continue
      attempted.add(id)
      schedule(async () => {
        const original = await apiBlob(fileUrl(reg, id))
        if (!original.ok) return
        const renditions = await makeRenditions(original.value)
        if (renditions === null) return
        const posted = await postRenditions(reg, id, renditions)
        if (posted.ok) await queryClient.invalidateQueries({ queryKey: queryKeys.documents(reg) })
      })
    }
  }, [reg, key, queryClient])
}
