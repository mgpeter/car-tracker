import { useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import {
  categoryLabel,
  kindOf,
  LINK_LABEL,
  uploadDocument,
  uploadRefusal,
  useDocumentTypes,
} from '../api/documents'
import { queryKeys, useGarage } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { AppLink } from '../lib/link'
import { shortDate } from '../screens/documents/format'
import { UploadSheet } from '../screens/documents/UploadSheet'
import type { FilingCardState } from './useChat'

/** Below this many documents left, the card says how many. */
const LOW_ALLOWANCE = 10

/**
 * One attachment, offered for filing.
 *
 * It shows what would be filed (category, title, date, vehicle, link) and whether that is the default or the
 * assistant's suggestion. **Nothing is filed until the owner presses File**, or saves the upload sheet Edit
 * opens (assumed: A1). File uploads the original the owner picked, not the JPEG the chat prepared for the model,
 * with the gallery's renditions, through the same documents endpoint as every other upload.
 */
export function FilingCard({
  card,
  vehicle,
  original,
  onFiled,
  onDismiss,
}: {
  card: FilingCardState
  /** The panel's vehicle, used until an offer names one. */
  vehicle: string | null
  /** The file itself, while the panel still holds it. */
  original: File | undefined
  onFiled: () => void
  onDismiss: () => void
}) {
  const { data: types } = useDocumentTypes()
  const needsGarage = card.registration === null && vehicle === null
  const { data: garage } = useGarage(needsGarage)
  const fallback = garage?.find((v) => v.isDefault)?.registration ?? garage?.[0]?.registration ?? null
  const reg = card.registration ?? vehicle ?? fallback

  const queryClient = useQueryClient()
  const [pending, setPending] = useState(false)
  const [refused, setRefused] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)

  const label = categoryLabel(card.type, types)
  const remaining = card.allowance === null ? null : Math.max(0, card.allowance.limit - card.allowance.used)
  const screen = kindOf(card.type, types) === 'Photo' ? 'photos' : 'documents'

  const file = async () => {
    if (reg === null || original === undefined || pending) return
    setPending(true)
    setRefused(null)
    const result = await uploadDocument(reg, original, {
      type: card.type,
      title: card.title,
      documentDate: card.documentDate,
      link: card.link,
    })
    setPending(false)

    if (!result.ok) {
      setRefused(uploadRefusal(result.error))
      return
    }

    await queryClient.invalidateQueries({ queryKey: queryKeys.documents(reg) })
    onFiled()
  }

  return (
    <section className={card.filed ? 'fcard is-filed' : 'fcard'} aria-label={`Filing card for ${card.fileName}`}>
      <p className="fcard-eyebrow">
        {card.filed ? 'Filed' : card.source === 'assistant' ? 'Suggested by the assistant' : 'Default suggestion'}
      </p>

      <dl className="fcard-facts">
        <div>
          <dt>Category</dt>
          <dd>{label}</dd>
        </div>
        <div>
          <dt>Title</dt>
          <dd>{card.title}</dd>
        </div>
        <div>
          <dt>Dated</dt>
          <dd className="num">{shortDate(card.documentDate)}</dd>
        </div>
        <div>
          <dt>Vehicle</dt>
          <dd className="num">{reg ?? 'no vehicle yet'}</dd>
        </div>
        {card.link !== null && (
          <div>
            <dt>Linked to</dt>
            <dd>
              → {LINK_LABEL[card.link.kind]}
              {card.link.label !== undefined && <> · {card.link.label}</>}
            </dd>
          </div>
        )}
      </dl>

      {card.filed ? (
        reg !== null && (
          <p className="fcard-done">
            <AppLink to={screen} reg={reg}>
              Open {screen === 'photos' ? 'Photos' : 'Documents'}
            </AppLink>
          </p>
        )
      ) : (
        <>
          {remaining !== null && remaining < LOW_ALLOWANCE && (
            <p className="fcard-left">{remaining} left on your plan</p>
          )}
          {refused !== null && (
            <p className="fcard-err" role="alert">
              {refused}
            </p>
          )}
          <div className="fcard-acts">
            <Btn onClick={() => void file()} disabled={pending || reg === null || original === undefined}>
              {pending ? 'Filing…' : 'File'}
            </Btn>
            <Mark onClick={() => setEditing(true)}>Edit</Mark>
            <Mark onClick={onDismiss}>Not now</Mark>
          </div>
        </>
      )}

      {reg !== null && (
        <UploadSheet
          open={editing}
          onClose={() => setEditing(false)}
          reg={reg}
          initial={{
            type: card.type,
            title: card.title,
            documentDate: card.documentDate,
            ...(card.link !== null && { link: card.link }),
            ...(original !== undefined && { files: [original] }),
          }}
          onFiled={onFiled}
        />
      )}
    </section>
  )
}
