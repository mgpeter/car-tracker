import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useRef, useState, type KeyboardEvent, type PointerEvent } from 'react'
import { createPortal } from 'react-dom'
import { apiRequest } from '../../api/client'
import {
  categoryLabel,
  openDocument,
  useDocumentObjectUrl,
  useDocumentTypes,
  type DocumentItem,
} from '../../api/documents'
import { ApiFailure, queryKeys } from '../../api/queries'
import { Mark } from '../../components/Btn'
import { ConfirmButton } from '../../components/ConfirmButton'
import { useFocusTrap, useInertBackground } from '../../lib/useFocusTrap'
import { useScrollLock } from '../../lib/useScrollLock'
import { useToast } from '../../shell/Toast'
import { LinkChip } from '../documents/LinkChip'
import { shortDate } from '../documents/format'
import { photoDay } from './dates'

/** What every supported browser can draw from the original when there is no display copy yet. HEIC is not. */
const DRAWABLE = new Set(['image/jpeg', 'image/png', 'image/webp', 'image/gif'])

/** Far enough to be a deliberate swipe, not a wobble on a tap. */
const SWIPE_PX = 50

interface PhotoViewerProps {
  reg: string
  /** In the order the arrows step through them. */
  photos: readonly DocumentItem[]
  startIndex: number
  onClose: () => void
  onRetag?: (photo: DocumentItem) => void
  /** Two panes, the first photo on the left and the last on the right, each dated. */
  compare?: boolean
}

/**
 * The full-screen viewer: one photo at a time, or two side by side to compare.
 *
 * Built on the hooks `Sheet` is built on (focus trap, inert background, scroll lock) rather than on `Sheet`
 * itself, which is a form with a footer (assumed: A18). Portalled to `body` so the page behind can be made
 * inert. Sized by the image's `width` and `height` attributes and CSS classes, never a `style` attribute,
 * because the CSP refuses inline styles.
 */
export function PhotoViewer({ reg, photos, startIndex, onClose, onRetag, compare = false }: PhotoViewerProps) {
  const ref = useRef<HTMLDivElement>(null)
  const [requested, setIndex] = useState(startIndex)
  const swipe = useRef<{ x: number; y: number } | null>(null)

  useFocusTrap({ active: true, containerRef: ref, onEscape: onClose })
  useScrollLock(true)
  useInertBackground(true)

  // Clamped, because a delete or a re-tag can shrink the list underneath the viewer.
  const index = Math.max(0, Math.min(requested, photos.length - 1))
  const photo = photos[index]
  const previous = photos[index - 1]
  const next = photos[index + 1]

  // The neighbours' display copies, so stepping is instant. Fetched and held, never rendered.
  useDocumentObjectUrl(reg, previous?.id ?? 0, 'Display', !compare && previous?.hasDisplay === true)
  useDocumentObjectUrl(reg, next?.id ?? 0, 'Display', !compare && next?.hasDisplay === true)

  if (photo === undefined) return null

  const step = (by: number) => setIndex(Math.max(0, Math.min(index + by, photos.length - 1)))

  const onKeyDown = (e: KeyboardEvent) => {
    if (compare) return
    if (e.key === 'ArrowRight') {
      e.preventDefault()
      step(1)
    } else if (e.key === 'ArrowLeft') {
      e.preventDefault()
      step(-1)
    }
  }

  const onPointerDown = (e: PointerEvent) => {
    swipe.current = { x: e.clientX, y: e.clientY }
  }

  const onPointerUp = (e: PointerEvent) => {
    const start = swipe.current
    swipe.current = null
    if (start === null) return
    const dx = e.clientX - start.x
    const dy = e.clientY - start.y
    if (Math.abs(dx) < SWIPE_PX || Math.abs(dx) < Math.abs(dy)) return
    step(dx < 0 ? 1 : -1)
  }

  const first = photos[0]!
  const last = photos[photos.length - 1]!

  const viewer = (
    <>
      <div className="ovl pviewer-ovl" onClick={onClose} aria-hidden="true" />
      <div
        className={compare ? 'pviewer is-compare' : 'pviewer'}
        role="dialog"
        aria-modal="true"
        aria-label={compare ? `Compare ${first.title} with ${last.title}` : photo.title}
        tabIndex={-1}
        ref={ref}
        onKeyDown={onKeyDown}
      >
        <div className="pviewer-bar">
          <span className="pviewer-count num">
            {compare ? `Earliest and latest of ${photos.length}` : `${index + 1} of ${photos.length}`}
          </span>
          <Mark onClick={onClose}>Close</Mark>
        </div>

        {compare ? (
          <div className="pcompare">
            {[first, last].map((p) => (
              <figure className="pcompare-pane" key={p.id}>
                <ViewerImage reg={reg} photo={p} />
                <figcaption className="pviewer-cap">
                  <b>{p.title}</b>
                  <DayLabel photo={p} />
                </figcaption>
              </figure>
            ))}
          </div>
        ) : (
          <>
            <div
              className="pviewer-stage"
              data-testid="pviewer-stage"
              onPointerDown={onPointerDown}
              onPointerUp={onPointerUp}
              onPointerCancel={() => (swipe.current = null)}
            >
              <ViewerImage reg={reg} photo={photo} />
            </div>
            <div className="pviewer-nav">
              <button type="button" className="mark" onClick={() => step(-1)} disabled={previous === undefined}>
                Previous
              </button>
              <button type="button" className="mark" onClick={() => step(1)} disabled={next === undefined}>
                Next
              </button>
            </div>
            <Caption reg={reg} photo={photo} />
            <Actions
              reg={reg}
              photo={photo}
              onRetag={onRetag}
              onDeleted={() => {
                if (photos.length === 1) onClose()
              }}
            />
          </>
        )}
      </div>
    </>
  )

  return createPortal(viewer, document.body)
}

function DayLabel({ photo }: { photo: DocumentItem }) {
  const { day, uploaded } = photoDay(photo)
  return <span className="num">{uploaded ? `uploaded ${shortDate(day)}` : shortDate(day)}</span>
}

/**
 * The display copy; the original where there is none and the browser can draw it; otherwise a placeholder.
 * The original is a fallback rather than a choice: it can be large, and it still carries the camera's metadata
 * (it stays in this browser's memory, but it is the bytes as filed) (assumed: A3).
 */
function ViewerImage({ reg, photo }: { reg: string; photo: DocumentItem }) {
  const variant = photo.hasDisplay ? 'Display' : DRAWABLE.has(photo.contentType) ? 'Original' : null
  const src = useDocumentObjectUrl(reg, photo.id, variant ?? 'Original', variant !== null)

  if (variant === null) return <div className="pplace pplace-lg">No preview in this browser</div>
  if (src === null) return <span className="pcell-loading pviewer-loading" aria-hidden="true" />

  const size = photo.width !== null && photo.height !== null ? { width: photo.width, height: photo.height } : {}
  return <img className="pviewer-img" src={src} alt={photo.title} {...size} />
}

function Caption({ reg, photo }: { reg: string; photo: DocumentItem }) {
  const { data: types } = useDocumentTypes()
  return (
    <div className="pviewer-cap" role="group" aria-label="About this photo">
      <b>{photo.title}</b>
      <DayLabel photo={photo} />
      <span className="dchip">{categoryLabel(photo.type, types)}</span>
      {photo.linkedTo !== null && <LinkChip document={photo} reg={reg} />}
    </div>
  )
}

function Actions({
  reg,
  photo,
  onRetag,
  onDeleted,
}: {
  reg: string
  photo: DocumentItem
  onRetag: ((photo: DocumentItem) => void) | undefined
  onDeleted: () => void
}) {
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const cover = useMutation({
    mutationFn: async () => {
      const result = await apiRequest<null>(`/api/vehicles/${encodeURIComponent(reg)}/cover-photo`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ documentId: photo.id }),
      })
      if (!result.ok) throw new ApiFailure(result.error)
    },
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.garage }),
        queryClient.invalidateQueries({ queryKey: queryKeys.vehicleDetail(reg) }),
      ])
      toast('Cover photo set · it shows on the garage card')
    },
    onError: (e) => toast(e instanceof Error ? e.message : 'Could not set the cover photo'),
  })

  const remove = useMutation({
    mutationFn: async () => {
      const result = await apiRequest<null>(
        `/api/vehicles/${encodeURIComponent(reg)}/documents/${photo.id}`,
        { method: 'DELETE' },
      )
      if (!result.ok) throw new ApiFailure(result.error)
    },
    onSuccess: async () => {
      onDeleted()
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: queryKeys.documents(reg) }),
        queryClient.invalidateQueries({ queryKey: queryKeys.garage }),
      ])
      toast('Photo deleted · the file and its previews went too')
    },
    onError: (e) => toast(e instanceof Error ? e.message : 'Could not delete the photo'),
  })

  return (
    <div className="pviewer-acts">
      <Mark onClick={() => void openDocument(reg, photo, true)}>Save original</Mark>
      {onRetag !== undefined && <Mark onClick={() => onRetag(photo)}>Re-tag</Mark>}
      <Mark onClick={() => cover.mutate()}>{cover.isPending ? 'Setting…' : 'Set as cover'}</Mark>
      <ConfirmButton onConfirm={() => remove.mutate()} pending={remove.isPending} cascade="the file and its previews" />
    </div>
  )
}
