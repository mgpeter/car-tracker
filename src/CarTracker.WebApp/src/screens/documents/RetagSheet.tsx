import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../../api/client'
import { LINK_LABEL, type DocumentItem } from '../../api/documents'
import { ApiFailure, queryKeys } from '../../api/queries'
import { Btn } from '../../components/Btn'
import { ConfirmButton } from '../../components/ConfirmButton'
import { Kv } from '../../components/Kv'
import { Field, Sheet } from '../../components/Sheet'
import { fieldError, formError, reportApiError, type FieldErrors } from '../../lib/formErrors'
import { useToast } from '../../shell/Toast'
import { CategorySelect } from './CategorySelect'
import { fileSize } from './format'

const FIELD_KEYS = ['title', 'type', 'link'] as const

/**
 * Re-tag: category, title, date, notes, and whether it stays attached. Plus remove.
 *
 * Moving a photo to a paper category moves it to the Documents screen, and the server clears it as the car's
 * cover if it was one, so the garage and vehicle queries are refreshed with the documents.
 */
export function RetagSheet({
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

  const close = () => {
    setV({})
    setErrors({})
    onClose()
  }

  const invalidate = async () => {
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: queryKeys.documents(reg) }),
      queryClient.invalidateQueries({ queryKey: queryKeys.garage }),
      queryClient.invalidateQueries({ queryKey: queryKeys.vehicleDetail(reg) }),
    ])
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
      close()
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
      close()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  return (
    <Sheet
      open={document !== null}
      onClose={close}
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

          <Field label="Category" error={fieldError(errors, 'type')}>
            {(p) => <CategorySelect value={get('type', document.type)} onChange={(t) => set('type', t)} {...p} />}
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

          <Kv
            label="Size"
            value={fileSize(document.sizeBytes)}
            note={document.contentType === 'application/pdf' ? 'PDF' : 'image'}
          />

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
