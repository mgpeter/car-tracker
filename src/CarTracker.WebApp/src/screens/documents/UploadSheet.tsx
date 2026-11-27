import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import {
  firstPaper,
  LINK_LABEL,
  uploadDocument,
  useDocumentTypes,
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
import { titleFromFileName } from './format'

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
}

const ACCEPT = 'application/pdf,image/jpeg,image/png,image/webp,image/heic,image/heif,image/gif'

const FIELD_KEYS = ['title', 'file', 'type', 'link', 'serviceRecordId', 'expenseEntryId', 'issueId'] as const

/**
 * Upload.
 *
 * Tag on the way in, and link to a record if it belongs to one. Renditions are made by
 * {@link uploadDocument}, so an image filed from here gets its previews whichever screen it lands on.
 */
export function UploadSheet({ open, onClose, reg, initial }: UploadSheetProps) {
  // Mounted only while open, so every opening starts from `initial` rather than from the last upload's state.
  return open ? <UploadForm onClose={onClose} reg={reg} initial={initial ?? {}} /> : null
}

function UploadForm({ onClose, reg, initial }: { onClose: () => void; reg: string; initial: UploadInitial }) {
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
