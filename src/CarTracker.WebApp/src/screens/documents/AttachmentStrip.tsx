import { useId, useMemo, useState } from 'react'
import {
  categoryLabel,
  openDocument,
  useDocuments,
  useDocumentTypes,
  useRenditionBackfill,
  type DocumentItem,
  type DocumentLinkRef,
} from '../../api/documents'
import { Mark } from '../../components/Btn'
import { oldestFirst } from '../photos/dates'
import { PhotoThumb } from '../photos/PhotoThumb'
import { PhotoViewer } from '../photos/PhotoViewer'
import { UploadSheet } from './UploadSheet'

/** Whether a document is attached to this record, by the link's own foreign key. */
function isLinkedTo(d: DocumentItem, link: DocumentLinkRef): boolean {
  switch (link.kind) {
    case 'ServiceRecord':
      return d.serviceRecordId === link.id
    case 'Expense':
      return d.expenseEntryId === link.id
    case 'Issue':
      return d.issueId === link.id
  }
}

/**
 * "Papers & photos": what is attached to one record, shown on the record itself, so the link from a document
 * to its service record, expense or issue can be followed from either end.
 *
 * Filtered from the car's one cached document log, so a screen with a strip on every row still makes one
 * request. Photos run oldest first (a timeline) and open the viewer over this record's photos only; with
 * `compare`, two or more of them offer the earliest and latest side by side. Attach opens the upload sheet with
 * the link preset.
 */
export function AttachmentStrip({
  reg,
  link,
  compare = false,
}: {
  reg: string
  link: DocumentLinkRef
  compare?: boolean
}) {
  const headingId = useId()
  const { data } = useDocuments(reg)
  const { data: types } = useDocumentTypes()
  const [viewing, setViewing] = useState<{ index: number; compare: boolean } | null>(null)
  const [attaching, setAttaching] = useState(false)

  const { papers, photos } = useMemo(
    () => ({
      papers: (data?.papers ?? []).filter((d) => isLinkedTo(d, link)),
      photos: oldestFirst((data?.photos ?? []).filter((d) => isLinkedTo(d, link))),
    }),
    [data, link],
  )

  useRenditionBackfill(reg, photos)

  return (
    <section className="astrip" aria-labelledby={headingId}>
      <div className="astrip-head">
        {/* A label, not a heading: the strip sits in a sheet under an h3 and in an issue card under an h2, and
            no one level is right in both. */}
        <span className="astrip-label" id={headingId}>
          Papers &amp; photos
        </span>
        {compare && photos.length >= 2 && (
          <Mark onClick={() => setViewing({ index: 0, compare: true })}>Compare earliest and latest</Mark>
        )}
        <Mark onClick={() => setAttaching(true)}>Attach</Mark>
      </div>

      {photos.length > 0 && (
        <div className="astrip-photos">
          {photos.map((photo, index) => (
            <PhotoThumb key={photo.id} reg={reg} photo={photo} onOpen={() => setViewing({ index, compare: false })} />
          ))}
        </div>
      )}

      {papers.length > 0 && (
        <ul className="astrip-papers">
          {papers.map((paper) => (
            <li key={paper.id}>
              <span className="dchip">{categoryLabel(paper.type, types)}</span>
              <span className="astrip-title">{paper.title}</span>
              <span className="dacts">
                <Mark onClick={() => void openDocument(reg, paper, false)}>View</Mark>
                <Mark onClick={() => void openDocument(reg, paper, true)}>Save</Mark>
              </span>
            </li>
          ))}
        </ul>
      )}

      {photos.length === 0 && papers.length === 0 && (
        <p className="astrip-empty">Nothing attached yet. An invoice, a receipt or a photo filed here stays with this record.</p>
      )}

      {viewing !== null && (
        <PhotoViewer
          reg={reg}
          photos={photos}
          startIndex={viewing.index}
          compare={viewing.compare}
          onClose={() => setViewing(null)}
        />
      )}
      <UploadSheet open={attaching} onClose={() => setAttaching(false)} reg={reg} initial={{ link }} />
    </section>
  )
}
