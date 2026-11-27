import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import {
  firstPaper,
  LINK_LABEL,
  uploadDocument,
  uploadRefusal,
  useDocumentTypes,
  type DocumentItem,
  type DocumentLinkRef,
  type DocumentType,
} from '../../api/documents'
import { ApiFailure, queryKeys } from '../../api/queries'
import { Btn } from '../../components/Btn'
import { Field, Sheet } from '../../components/Sheet'
import { todayIso } from '../../lib/date'
import { fieldError, formError, reportApiError, type FieldErrors } from '../../lib/formErrors'
import { useToast } from '../../shell/Toast'
import { CategorySelect } from './CategorySelect'
import { localIsoDate, titleFromFileName } from './format'

/** What a caller can fill in before the sheet opens: the Photos screen, an attachment strip, a chat card. */
export interface UploadInitial {
  type?: DocumentType
  title?: string
  documentDate?: string
  /** Shown as a read-only chip and sent as its id field; the sheet cannot change it. */
  link?: DocumentLinkRef
  files?: File[]
}

interface UploadSheetProps {
  open: boolean
  onClose: () => void
  reg: string
  initial?: UploadInitial
  /** Several files in one go: one shared category, a title and a date per file, filed one after another. */
  multiple?: boolean
  /** Told once the single-file form has filed, before it closes: a chat card marks itself filed from this. */
  onFiled?: (item: DocumentItem) => void
}

const ACCEPT = 'application/pdf,image/jpeg,image/png,image/webp,image/heic,image/heif,image/gif'

const FIELD_KEYS = ['title', 'file', 'type', 'link', 'serviceRecordId', 'expenseEntryId', 'issueId'] as const

/**
 * Upload.
 *
 * Tag on the way in, and link to a record if it belongs to one. Renditions are made by
 * {@link uploadDocument}, so an image filed from here gets its previews whichever screen it lands on.
 */
export function UploadSheet({ open, onClose, reg, initial, multiple = false, onFiled }: UploadSheetProps) {
  // Mounted only while open, so every opening starts from `initial` rather than from the last upload's state.
  if (!open) return null
  return multiple ? (
    <MultiUploadForm onClose={onClose} reg={reg} initial={initial ?? {}} />
  ) : (
    <UploadForm onClose={onClose} reg={reg} initial={initial ?? {}} {...(onFiled !== undefined && { onFiled })} />
  )
}

function UploadForm({
  onClose,
  reg,
  initial,
  onFiled,
}: {
  onClose: () => void
  reg: string
  initial: UploadInitial
  onFiled?: (item: DocumentItem) => void
}) {
  const first = initial.files?.[0] ?? null
  const [file, setFile] = useState<File | null>(first)
  const [title, setTitle] = useState(initial.title ?? (first === null ? '' : titleFromFileName(first.name)))
  const [type, setType] = useState<DocumentType | undefined>(initial.type)
  const [documentDate, setDocumentDate] = useState(initial.documentDate ?? todayIso())
  const [notes, setNotes] = useState('')
  const [errors, setErrors] = useState<FieldErrors>({})
  const [pending, setPending] = useState(false)
  const { data: types } = useDocumentTypes()
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const category = type ?? firstPaper(types)

  const validate = (): FieldErrors => {
    const e: FieldErrors = {}
    if (file === null) e['file'] = ['Choose a PDF or a photo to upload.']
    if (title.trim() === '') e['title'] = ['Give the document a title.']
    if (category === undefined) e['type'] = ['Choose a category.']
    return e
  }

  const submit = async () => {
    const found = validate()
    setErrors(found)
    if (Object.keys(found).length > 0 || pending) return

    setPending(true)
    const result = await uploadDocument(reg, file!, {
      type: category!,
      title,
      documentDate,
      notes,
      link: initial.link ?? null,
    })
    setPending(false)

    if (!result.ok) {
      setErrors(reportApiError(new ApiFailure(result.error), FIELD_KEYS))
      return
    }

    await queryClient.invalidateQueries({ queryKey: queryKeys.documents(reg) })
    toast(initial.link ? `Filed · attached to this ${LINK_LABEL[initial.link.kind]}` : 'Filed')
    onFiled?.(result.value)
    onClose()
  }

  return (
    <Sheet
      open
      onClose={onClose}
      title="Upload a document"
      subtitle="PDF or photo · tag it on the way in"
      onSubmit={() => void submit()}
      footer={
        <Btn type="submit" onClick={() => {}}>
          {pending ? 'Uploading…' : 'Upload'}
        </Btn>
      }
    >
      <Field label="File" wide error={fieldError(errors, 'file')} hint="PDF or photo, up to 25 MB">
        {(p) => (
          <input
            type="file"
            accept={ACCEPT}
            onChange={(e) => {
              const chosen = e.target.files?.[0] ?? null
              setFile(chosen)
              if (chosen !== null && title === '') setTitle(titleFromFileName(chosen.name))
            }}
            {...p}
          />
        )}
      </Field>

      <Field label="Title" wide error={fieldError(errors, 'title')}>
        {(p) => (
          <input
            type="text"
            placeholder="MOT certificate - pass"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            {...p}
          />
        )}
      </Field>

      <Field label="Category" error={fieldError(errors, 'type')}>
        {(p) => <CategorySelect value={category ?? ''} onChange={setType} {...p} />}
      </Field>

      <Field label="Dated" hint="the date on the document, not today">
        {(p) => <input type="date" value={documentDate} onChange={(e) => setDocumentDate(e.target.value)} {...p} />}
      </Field>

      <Field label="Notes" wide>
        {(p) => (
          <input
            type="text"
            placeholder="8 Jul 2026 · 80,705 mi · 2 advisories"
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            {...p}
          />
        )}
      </Field>

      {initial.link !== undefined && <LinkedTo link={initial.link} />}

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

/** The preset link, read-only: the sheet was opened from that record, so that is where the file goes. */
function LinkedTo({ link }: { link: DocumentLinkRef }) {
  return (
    <div className="field wide">
      <div className="dattach">
        <span className="dattach-clear">Attached to</span>
        <span>
          → {LINK_LABEL[link.kind]}
          {link.label !== undefined && <> · {link.label}</>}
        </span>
      </div>
    </div>
  )
}

type FileStatus =
  | { kind: 'waiting' }
  | { kind: 'uploading' }
  | { kind: 'filed' }
  | { kind: 'refused'; message: string }
  | { kind: 'not-sent' }

interface Row {
  file: File
  title: string
  documentDate: string
  status: FileStatus
}

const rowFor = (file: File): Row => ({
  file,
  title: titleFromFileName(file.name),
  // The file's modified day, which on most phones is when the photo was taken. Not the EXIF capture date:
  // reading that needs a parser this spec does not add, and on some phones the two differ.
  documentDate: localIsoDate(new Date(file.lastModified)),
  status: { kind: 'waiting' },
})

/**
 * The plan's document allowance, recognised by the server's own sentence (`DocumentService.FullMessage`). The
 * refusal has no type or code of its own, so its wording is the contract; a duplicate or a bad file is not this,
 * and the rest of the batch carries on past those.
 */
const isAllowanceRefusal = (message: string) => /holding \d+ of its \d+ documents/.test(message)

const STATUS_TEXT: Record<Exclude<FileStatus['kind'], 'refused'>, string> = {
  waiting: 'Waiting',
  uploading: 'Uploading…',
  filed: 'Filed',
  'not-sent': 'Not sent',
}

function MultiUploadForm({ onClose, reg, initial }: { onClose: () => void; reg: string; initial: UploadInitial }) {
  const [rows, setRows] = useState<Row[]>(() => (initial.files ?? []).map(rowFor))
  const [type, setType] = useState<DocumentType | undefined>(initial.type)
  const [phase, setPhase] = useState<'editing' | 'uploading' | 'done'>('editing')
  const [summary, setSummary] = useState<string | null>(null)
  const [errors, setErrors] = useState<FieldErrors>({})
  const { data: types } = useDocumentTypes()
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const category = type ?? firstPaper(types)
  const noun = (n: number) => (n === 1 ? 'photo' : 'photos')

  const update = (index: number, patch: Partial<Row>) =>
    setRows((current) => current.map((row, i) => (i === index ? { ...row, ...patch } : row)))

  const submit = async () => {
    if (phase === 'done') {
      onClose()
      return
    }
    const found: FieldErrors = {}
    if (rows.length === 0) found['file'] = ['Choose the photos to upload.']
    if (category === undefined) found['type'] = ['Choose a category.']
    if (rows.some((r) => r.title.trim() === '')) found['title'] = ['Every photo needs a title.']
    setErrors(found)
    if (Object.keys(found).length > 0 || phase !== 'editing') return

    setPhase('uploading')
    let filed = 0
    let stopped: string | null = null

    // One after another, not in parallel: the allowance is counted per file, so a parallel batch could not
    // stop at the ceiling, and a phone on a weak signal does better with one upload at a time.
    for (const [index, row] of rows.entries()) {
      if (stopped !== null) {
        update(index, { status: { kind: 'not-sent' } })
        continue
      }
      update(index, { status: { kind: 'uploading' } })
      const result = await uploadDocument(reg, row.file, {
        type: category!,
        title: row.title,
        documentDate: row.documentDate,
        link: initial.link ?? null,
      })
      if (result.ok) {
        filed += 1
        update(index, { status: { kind: 'filed' } })
        continue
      }
      const message = uploadRefusal(result.error)
      update(index, { status: { kind: 'refused', message } })
      if (isAllowanceRefusal(message)) stopped = message
    }

    await queryClient.invalidateQueries({ queryKey: queryKeys.documents(reg) })
    setPhase('done')
    setSummary(
      stopped !== null
        ? `Stopped at the account's document allowance: ${filed} ${noun(filed)} ${filed === 1 ? 'was' : 'were'} filed.`
        : `Filed ${filed} of ${rows.length}.`,
    )
    if (filed > 0) toast(`${filed} ${noun(filed)} filed`)
  }

  const label =
    phase === 'done' ? 'Done' : phase === 'uploading' ? 'Uploading…' : `Upload ${rows.length} ${noun(rows.length)}`

  return (
    <Sheet
      open
      onClose={onClose}
      title="Upload photos"
      subtitle="one category for all · a title and a date each"
      onSubmit={() => void submit()}
      footer={
        <Btn type="submit" onClick={() => {}}>
          {label}
        </Btn>
      }
    >
      {phase === 'editing' && (
        <Field label="Files" wide error={fieldError(errors, 'file')} hint="photos or PDFs, up to 25 MB each">
          {(p) => (
            <input
              type="file"
              multiple
              accept={ACCEPT}
              onChange={(e) => setRows([...(e.target.files ?? [])].map(rowFor))}
              {...p}
            />
          )}
        </Field>
      )}

      <Field label="Category" error={fieldError(errors, 'type')}>
        {(p) => <CategorySelect value={category ?? ''} onChange={setType} {...p} />}
      </Field>

      {rows.length > 0 &&
        (phase === 'editing' ? (
          <div className="field wide">
            {fieldError(errors, 'title') !== undefined && (
              <span className="hint err" role="alert">
                {fieldError(errors, 'title')}
              </span>
            )}
            <ul className="uprows" aria-label="Files to upload">
              {rows.map((row, index) => (
                <li key={`${row.file.name}-${index}`} className="uprow">
                  <span className="uprow-name num">{row.file.name}</span>
                  <input
                    type="text"
                    aria-label={`Title for ${row.file.name}`}
                    value={row.title}
                    onChange={(e) => update(index, { title: e.target.value })}
                  />
                  <input
                    type="date"
                    aria-label={`Date for ${row.file.name}`}
                    value={row.documentDate}
                    onChange={(e) => update(index, { documentDate: e.target.value })}
                  />
                </li>
              ))}
            </ul>
          </div>
        ) : (
          <div className="field wide">
            <ul className="uprows" aria-label="Upload progress">
              {rows.map((row, index) => (
                <li key={`${row.file.name}-${index}`} className={`uprow is-${row.status.kind}`}>
                  <span className="uprow-name">{row.title}</span>
                  <span className="uprow-status">
                    {row.status.kind === 'refused' ? row.status.message : STATUS_TEXT[row.status.kind]}
                  </span>
                </li>
              ))}
            </ul>
          </div>
        ))}

      {summary !== null && (
        <div className="field wide">
          <p className="hint" role="status">
            {summary}
          </p>
        </div>
      )}
    </Sheet>
  )
}
