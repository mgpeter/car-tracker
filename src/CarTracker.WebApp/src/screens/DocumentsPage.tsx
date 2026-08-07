import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useEffect, useState } from 'react'
import { apiBlob, apiRequest } from '../api/client'
import { ApiFailure } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { ConfirmButton } from '../components/ConfirmButton'
import { Absent, DataTable, Sub, type Column } from '../components/DataTable'
import { Kv } from '../components/Kv'
import { IntegrityPill } from '../components/Pill'
import { Field, Select, Sheet } from '../components/Sheet'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { todayIso } from '../lib/date'
import { fieldError, formError, reportApiError, type FieldErrors } from '../lib/formErrors'
import { usePlate } from '../lib/usePlate'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'

type DocumentType = 'V5C' | 'Insurance' | 'MOT' | 'Receipt' | 'Photo' | 'Manual' | 'Other'

interface DocumentLink {
  kind: 'ServiceRecord' | 'Expense' | 'Issue'
  id: number
  label: string
}

interface DocumentItem {
  id: number
  type: DocumentType
  title: string
  documentDate: string | null
  contentType: string
  sizeBytes: number
  sha256: string | null
  serviceRecordId: number | null
  expenseEntryId: number | null
  issueId: number | null
  notes: string | null
  linkedTo: DocumentLink | null
}

interface DocumentLog {
  papers: DocumentItem[]
  photos: DocumentItem[]
  totalCount: number
  totalSizeBytes: number
}

const TYPES: DocumentType[] = ['V5C', 'Insurance', 'MOT', 'Receipt', 'Photo', 'Manual', 'Other']

/** Where a link chip goes. The design's `→ policy` chip has no FK behind it and is deliberately not invented. */
const LINK_SCREEN = {
  ServiceRecord: 'service',
  Expense: 'expenses',
  Issue: 'issues',
} as const

const LINK_LABEL = {
  ServiceRecord: 'service record',
  Expense: 'expense',
  Issue: 'issue',
} as const

const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

/** Sizes are for a human deciding whether a thing will download quickly, so one decimal is plenty. */
const fileSize = (bytes: number) =>
  bytes >= 1024 * 1024 ? `${(bytes / (1024 * 1024)).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`

const isPdf = (contentType: string) => contentType === 'application/pdf'

/**
 * Documents — the seventeenth and last workbook screen, and the only one that needed file upload, which is
 * why it went last.
 *
 * **Papers is a table and photo sets is a grid, deliberately.** The design says so in its own eyebrow — "PDFs
 * listed, photo sets gridded — they are not the same thing" — and it is the same seam that keeps checks a list:
 * a table earns its keep when there are columns of aligned facts to compare, and a set of images has none.
 * Papers is `<DataTable>`'s fifth consumer; the photo grid is not forced through it.
 *
 * **The chips are `Type` and the links, and nothing else.** The design's chip row *looks* like free-form tags
 * (`identity`, `statutory`, `history`), but the schema models one `DocumentType` plus three nullable link FKs.
 * Inventing a tags table to match the mock would be a schema change with no mandate; showing the type and the
 * link is the honest read of what is actually stored.
 */
export function DocumentsPage() {
  const reg = useVehicleReg()
  const plate = usePlate()
  const [editing, setEditing] = useState<DocumentItem | null>(null)
  const [uploading, setUploading] = useState(false)

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['vehicle', reg, 'documents'] as const,
    queryFn: async () => {
      const result = await apiRequest<DocumentLog>(`/api/vehicles/${encodeURIComponent(reg)}/documents`)
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
  })

  const columns: Column<DocumentItem>[] = [
    {
      key: 'type',
      label: 'Kind',
      width: '78px',
      priority: 'essential',
      render: (d) => <span className="dchip">{d.type}</span>,
    },
    {
      key: 'title',
      label: 'Document',
      width: '1.6fr',
      priority: 'essential',
      render: (d) => (
        <>
          {d.title}
          {d.notes !== null && <Sub>{d.notes}</Sub>}
        </>
      ),
    },
    {
      key: 'date',
      label: 'Dated',
      width: '96px',
      priority: 'normal',
      render: (d) => (d.documentDate === null ? <Absent>not dated</Absent> : shortDate(d.documentDate)),
    },
    {
      key: 'link',
      label: 'Attached to',
      width: '1fr',
      priority: 'normal',
      render: (d) => <LinkChip document={d} reg={reg} />,
    },
    {
      key: 'size',
      label: 'Size',
      width: '74px',
      align: 'right',
      priority: 'secondary',
      render: (d) => fileSize(d.sizeBytes),
    },
    {
      key: 'open',
      label: 'File',
      width: '104px',
      priority: 'normal',
      // Real anchors, not buttons: viewing and downloading are navigations to a URL, and a link is what lets
      // "open in a new tab" and "save as" work the way everyone already expects them to.
      render: (d) => <FileLinks document={d} reg={reg} />,
    },
  ]

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="documents"
      footer={
        <>
          The bytes live on a mounted volume and the row keeps the path — never in the database, which would
          make every backup carry the photo sets. Download returns the original file untouched; nothing is
          re-encoded on the way out.
        </>
      }
      center={{ kind: 'action', icon: 'plus', label: 'Upload', onClick: () => setUploading(true) }}
    >
      <PageHead
        eyebrow="Documents · PDFs listed, photo sets gridded"
        title="Documents"
        plate={plate}
        pmeta={
          data === undefined ? undefined : (
            <>
              {data.totalCount} {data.totalCount === 1 ? 'file' : 'files'} ·{' '}
              {fileSize(data.totalSizeBytes)}
              <br />
              A document can link to a service record,
              <br />
              an expense or an issue
            </>
          )
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel>
              <p className="panel-empty">
                {formError({ _: [error instanceof Error ? error.message : 'Could not load documents.'] }) ?? ''}{' '}
                <Mark onClick={() => refetch()}>Try again</Mark>
              </p>
            </Panel>
          </Wrap>
        </Section>
      ) : isPending ? (
        <Section last>
          <Wrap>
            <Panel>
              <p className="panel-empty">Loading documents…</p>
            </Panel>
          </Wrap>
        </Section>
      ) : (
        <>
          <Section>
            <Wrap>
              <SectionHead
                title="Papers"
                rule={<>tap to re-tag · download keeps the original file</>}
                link={<Mark onClick={() => setUploading(true)}>Upload</Mark>}
              />
              {data.papers.length === 0 ? (
                <Panel>
                  <p className="panel-empty">
                    No papers filed. The V5C, the insurance certificate and the MOT pass are the three worth
                    having here — <Mark onClick={() => setUploading(true)}>upload one</Mark>.
                  </p>
                </Panel>
              ) : (
                <DataTable
                  columns={columns}
                  rows={data.papers}
                  rowKey={(d) => d.id}
                  label="Filed papers"
                  onRowClick={(d) => setEditing(d)}
                  rowLabel={(d) => `Re-tag ${d.title}`}
                />
              )}
            </Wrap>
          </Section>

          <Section last>
            <Wrap>
              <SectionHead
                title="Photo sets"
                rule={<>condition record · the baseline "worsening" is measured against</>}
              />
              {data.photos.length === 0 ? (
                <Panel>
                  <p className="panel-empty">
                    No photos yet. A condition set taken now is what a future argument about rust or cracking
                    gets measured against.
                  </p>
                </Panel>
              ) : (
                <>
                  <div className="photos">
                    {data.photos.map((p) => (
                      <PhotoTile key={p.id} photo={p} reg={reg} onEdit={() => setEditing(p)} />
                    ))}
                  </div>
                  <p className="ifootnote">
                    <b>Photos are evidence, not decoration.</b> Each links to the issue or record it documents,
                    and the unlinked ones are the baseline set — what "worsening" is compared against when an
                    issue is argued about months later.
                  </p>
                </>
              )}
            </Wrap>
          </Section>
        </>
      )}

      <UploadSheet open={uploading} onClose={() => setUploading(false)} reg={reg} />
      <RetagSheet document={editing} onClose={() => setEditing(null)} reg={reg} />
    </AppShell>
  )
}

/** The `→ service record` chip, from whichever FK is set. Absent is a real state and says so. */
function LinkChip({ document, reg }: { document: DocumentItem; reg: string }) {
  if (document.linkedTo === null) return <Absent>not attached</Absent>

  const { kind, label } = document.linkedTo
  return (
    <a className="dlink" href={`/v/${encodeURIComponent(reg)}/${LINK_SCREEN[kind]}`}>
      → {LINK_LABEL[kind]}
      <Sub>{label}</Sub>
    </a>
  )
}

const fileUrl = (reg: string, id: number) =>
  `/api/vehicles/${encodeURIComponent(reg)}/documents/${id}/file`

/**
 * Fetches a document's bytes through the authenticated seam and hands back a temporary object URL.
 *
 * The revoke on unmount is not tidiness — an object URL pins its blob in memory for the life of the document,
 * so a photo grid that never revoked would hold every image it had ever shown.
 */
function useDocumentObjectUrl(reg: string, id: number, enabled = true) {
  const [url, setUrl] = useState<string | null>(null)

  useEffect(() => {
    if (!enabled) return
    let revoked = false
    let objectUrl: string | null = null

    void apiBlob(fileUrl(reg, id)).then((result) => {
      if (!result.ok || revoked) return
      objectUrl = URL.createObjectURL(result.value)
      setUrl(objectUrl)
    })

    return () => {
      revoked = true
      if (objectUrl !== null) URL.revokeObjectURL(objectUrl)
    }
  }, [reg, id, enabled])

  return url
}

/** Opens the bytes in a tab, or saves them under the document's title. */
async function openDocument(reg: string, document: DocumentItem, download: boolean) {
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

function FileLinks({ document, reg }: { document: DocumentItem; reg: string }) {
  return (
    <span className="dacts">
      {/* Buttons, not anchors: the bytes need our Authorization header, so they come through fetch and become
          an object URL rather than being a plain navigation the browser would send unauthenticated. */}
      <Mark onClick={() => void openDocument(reg, document, false)}>View</Mark>
      <Mark onClick={() => void openDocument(reg, document, true)}>Save</Mark>
    </span>
  )
}

/**
 * One condition photo.
 *
 * The image is fetched from the same file endpoint the viewer uses. No thumbnail pipeline: a handful of photos
 * per car does not justify a resize step, and the browser scales them fine.
 */
function PhotoTile({ photo, reg, onEdit }: { photo: DocumentItem; reg: string; onEdit: () => void }) {
  const src = useDocumentObjectUrl(reg, photo.id)
  return (
    <figure className="pcell">
      {src === null ? (
        <div className="pcell-loading" aria-hidden="true" />
      ) : (
        <img src={src} alt={photo.title} />
      )}
      <figcaption className="pcap">
        <span>{photo.title}</span>
        <span className="num">
          {photo.documentDate === null ? 'not dated' : shortDate(photo.documentDate)}
        </span>
      </figcaption>
      <div className="pcap-acts">
        {photo.linkedTo !== null && (
          <IntegrityPill>→ {LINK_LABEL[photo.linkedTo.kind]}</IntegrityPill>
        )}
        <Mark onClick={onEdit}>Re-tag</Mark>
      </div>
    </figure>
  )
}

/**
 * Upload.
 *
 * `FormData`, not JSON — the one write in the app that sends a file. The type and the link are chosen here
 * rather than afterwards, which is what the design's toast promises: "tag on the way in, link to a record if it
 * belongs to one".
 */
function UploadSheet({ open, onClose, reg }: { open: boolean; onClose: () => void; reg: string }) {
  const [file, setFile] = useState<File | null>(null)
  const [v, setV] = useState<Record<string, string>>({})
  const [errors, setErrors] = useState<FieldErrors>({})
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const get = (k: string, fallback = '') => v[k] ?? fallback
  const set = (k: string, value: string) => setV((p) => ({ ...p, [k]: value }))

  const FIELD_KEYS = ['title', 'file', 'type', 'link', 'serviceRecordId', 'expenseEntryId', 'issueId'] as const

  const validate = (): FieldErrors => {
    const e: FieldErrors = {}
    if (file === null) e['file'] = ['Choose a PDF or a photo to upload.']
    if (get('title').trim() === '') e['title'] = ['Give the document a title.']
    return e
  }

  const reset = () => {
    setFile(null)
    setV({})
    setErrors({})
  }

  const mutation = useMutation({
    mutationFn: async () => {
      const body = new FormData()
      body.append('file', file!)
      body.append('title', get('title'))
      body.append('type', get('type', 'Other'))
      if (get('documentDate') !== '') body.append('documentDate', get('documentDate'))
      if (get('notes') !== '') body.append('notes', get('notes'))

      const result = await apiRequest<DocumentItem>(
        `/api/vehicles/${encodeURIComponent(reg)}/documents`,
        // No Content-Type header: the browser must set it, because only it knows the multipart boundary.
        { method: 'POST', body },
      )
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'documents'] })
      toast('Filed · tag and link it from the row')
      reset()
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  const submit = () => {
    const found = validate()
    setErrors(found)
    if (Object.keys(found).length === 0) mutation.mutate()
  }

  return (
    <Sheet
      open={open}
      onClose={() => {
        reset()
        onClose()
      }}
      title="Upload a document"
      subtitle="PDF or photo · tag it on the way in"
      onSubmit={submit}
      footer={
        <Btn type="submit" onClick={() => {}}>
          {mutation.isPending ? 'Uploading…' : 'Upload'}
        </Btn>
      }
    >
      <Field label="File" wide error={fieldError(errors, 'file')} hint="PDF or photo, up to 25 MB">
        {(p) => (
          <input
            type="file"
            accept="application/pdf,image/jpeg,image/png,image/webp,image/heic,image/heif,image/gif"
            onChange={(e) => {
              const chosen = e.target.files?.[0] ?? null
              setFile(chosen)
              // The filename is a decent first guess at a title and is not stored otherwise — the file lands
              // on disk named for its own hash, so this is the only place it survives at all.
              if (chosen !== null && get('title') === '') {
                set('title', chosen.name.replace(/\.[^.]+$/, ''))
              }
            }}
            {...p}
          />
        )}
      </Field>

      <Field label="Title" wide error={fieldError(errors, 'title')}>
        {(p) => (
          <input
            type="text"
            placeholder="MOT certificate — pass"
            value={get('title')}
            onChange={(e) => set('title', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Kind" error={fieldError(errors, 'type')}>
        {(p) => (
          <Select value={get('type', 'Other')} onChange={(e) => set('type', e.target.value)} {...p}>
            {TYPES.map((t) => (
              <option key={t} value={t}>
                {t}
              </option>
            ))}
          </Select>
        )}
      </Field>

      <Field label="Dated" hint="the date on the document, not today">
        {(p) => (
          <input
            type="date"
            value={get('documentDate', todayIso())}
            onChange={(e) => set('documentDate', e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Notes" wide>
        {(p) => (
          <input
            type="text"
            placeholder="8 Jul 2026 · 80,705 mi · 2 advisories"
            value={get('notes')}
            onChange={(e) => set('notes', e.target.value)}
            {...p}
          />
        )}
      </Field>

      {formError(errors) !== undefined && (
        <div className="field wide">
          <span className="hint err" role="alert">
            {formError(errors)}
          </span>
        </div>
      )}
    </Sheet>
  )
}

/** Re-tag: type, title, date, notes, and which record it attaches to. Plus remove. */
function RetagSheet({
  document,
  onClose,
  reg,
}: {
  document: DocumentItem | null
  onClose: () => void
  reg: string
}) {
  const [v, setV] = useState<Record<string, string>>({})
  const [errors, setErrors] = useState<FieldErrors>({})
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const get = (k: string, fallback = '') => v[k] ?? fallback
  const set = (k: string, value: string) => setV((p) => ({ ...p, [k]: value }))

  const FIELD_KEYS = ['title', 'type', 'link'] as const

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'documents'] })
  }

  const mutation = useMutation({
    mutationFn: async () => {
      const result = await apiRequest<DocumentItem>(
        `/api/vehicles/${encodeURIComponent(reg)}/documents/${document!.id}`,
        {
          method: 'PATCH',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            title: get('title', document!.title),
            type: get('type', document!.type),
            documentDate: get('documentDate', document!.documentDate ?? '') || null,
            notes: get('notes', document!.notes ?? '') || null,
            // Detaching is its own flag: null on the id fields already means "leave it alone".
            clearLink: get('clearLink') === 'yes',
          }),
        },
      )
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async () => {
      await invalidate()
      toast('Document re-tagged')
      setV({})
      setErrors({})
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  const remove = useMutation({
    mutationFn: async () => {
      const result = await apiRequest<null>(
        `/api/vehicles/${encodeURIComponent(reg)}/documents/${document!.id}`,
        { method: 'DELETE' },
      )
      if (!result.ok) throw new ApiFailure(result.error)
    },
    onSuccess: async () => {
      await invalidate()
      toast('Document removed · the file was deleted too')
      setV({})
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  return (
    <Sheet
      open={document !== null}
      onClose={() => {
        setV({})
        onClose()
      }}
      title="Re-tag document"
      subtitle={document?.title ?? ''}
      onSubmit={() => mutation.mutate()}
      footer={
        <>
          <ConfirmButton onConfirm={() => remove.mutate()} pending={remove.isPending} />
          <Btn type="submit" onClick={() => {}}>
            {mutation.isPending ? 'Saving…' : 'Save'}
          </Btn>
        </>
      }
    >
      {document !== null && (
        <>
          <Field label="Title" wide error={fieldError(errors, 'title')}>
            {(p) => (
              <input
                type="text"
                value={get('title', document.title)}
                onChange={(e) => set('title', e.target.value)}
                {...p}
              />
            )}
          </Field>

          <Field label="Kind" error={fieldError(errors, 'type')}>
            {(p) => (
              <Select value={get('type', document.type)} onChange={(e) => set('type', e.target.value)} {...p}>
                {TYPES.map((t) => (
                  <option key={t} value={t}>
                    {t}
                  </option>
                ))}
              </Select>
            )}
          </Field>

          <Field label="Dated">
            {(p) => (
              <input
                type="date"
                value={get('documentDate', document.documentDate ?? '')}
                onChange={(e) => set('documentDate', e.target.value)}
                {...p}
              />
            )}
          </Field>

          <Field label="Notes" wide>
            {(p) => (
              <input
                type="text"
                value={get('notes', document.notes ?? '')}
                onChange={(e) => set('notes', e.target.value)}
                {...p}
              />
            )}
          </Field>

          <Field label="Attached to" wide hint="a document attaches to one record, or to none">
            {() => (
              <div className="dattach">
                {document.linkedTo === null ? (
                  <span className="faint">Not attached to anything.</span>
                ) : (
                  <>
                    <span>
                      {LINK_LABEL[document.linkedTo.kind]} · {document.linkedTo.label}
                    </span>
                    <label className="dattach-clear">
                      <input
                        type="checkbox"
                        checked={get('clearLink') === 'yes'}
                        onChange={(e) => set('clearLink', e.target.checked ? 'yes' : '')}
                      />
                      Detach on save
                    </label>
                  </>
                )}
              </div>
            )}
          </Field>

          <Kv label="Size" value={fileSize(document.sizeBytes)} note={isPdf(document.contentType) ? 'PDF' : 'image'} />

          {formError(errors) !== undefined && (
            <div className="field wide">
              <span className="hint err" role="alert">
                {formError(errors)}
              </span>
            </div>
          )}
        </>
      )}
    </Sheet>
  )
}
