import { useEffect, useRef, useState, type RefObject } from 'react'
import { useBackfillState, useDocumentObjectUrl, type DocumentItem } from '../../api/documents'
import { THUMBNAIL_EDGE } from '../../lib/renditions'

/** True once the element has come within a screen of the viewport; true at once where there is no observer. */
function useNearViewport(ref: RefObject<HTMLElement | null>): boolean {
  const [near, setNear] = useState(() => typeof IntersectionObserver === 'undefined')

  useEffect(() => {
    const element = ref.current
    if (near || element === null) return
    const observer = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) {
          setNear(true)
          observer.disconnect()
        }
      },
      { rootMargin: '400px' },
    )
    observer.observe(element)
    return () => observer.disconnect()
  }, [near, ref])

  return near
}

/** The thumbnail's own size, from the original's, so the tile reserves the right box before the bytes arrive. */
function thumbnailSize(photo: Pick<DocumentItem, 'width' | 'height'>) {
  if (photo.width === null || photo.height === null) return {}
  const scale = Math.min(1, THUMBNAIL_EDGE / Math.max(photo.width, photo.height))
  return { width: Math.round(photo.width * scale), height: Math.round(photo.height * scale) }
}

/**
 * One photo as a button that opens it: the thumbnail, fetched through the authenticated seam only once the
 * tile is near the viewport, and only when the photo has one. A photo without a thumbnail shows a placeholder,
 * which says "making a preview" while the backfill tries and "no preview in this browser" when it cannot.
 */
export function PhotoThumb({
  reg,
  photo,
  onOpen,
  label = `Open ${photo.title}`,
}: {
  reg: string
  photo: DocumentItem
  onOpen: () => void
  label?: string
}) {
  const ref = useRef<HTMLButtonElement>(null)
  const near = useNearViewport(ref)
  const src = useDocumentObjectUrl(reg, photo.id, 'Thumbnail', near && photo.hasThumbnail)
  const backfill = useBackfillState(photo.id)

  return (
    <button ref={ref} type="button" className="ptile" aria-label={label} onClick={onOpen}>
      {!photo.hasThumbnail ? (
        <span className="pplace">{backfill === 'working' ? 'Making a preview…' : 'No preview in this browser'}</span>
      ) : src === null ? (
        <span className="pcell-loading" aria-hidden="true" />
      ) : (
        <img src={src} alt={photo.title} {...thumbnailSize(photo)} />
      )}
    </button>
  )
}
