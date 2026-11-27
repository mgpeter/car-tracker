import { useId, useMemo, useState } from 'react'
import { openDocument, useDocuments, useRenditionBackfill, type DocumentItem } from '../../api/documents'
import { Mark } from '../../components/Btn'
import { Seg } from '../../components/Seg'
import { Panel, Section, SectionHead, Wrap } from '../../components/layout'
import { formError } from '../../lib/formErrors'
import { usePlate } from '../../lib/usePlate'
import { useVehicleReg } from '../../routes'
import { AppShell } from '../../shell/AppShell'
import { PageHead } from '../../shell/PageHead'
import { shortDate } from '../documents/format'
import { RetagSheet } from '../documents/RetagSheet'
import { UploadSheet } from '../documents/UploadSheet'
import { monthLabel, newestFirst, photoDay } from './dates'
import { PhotoThumb } from './PhotoThumb'
import { PhotoViewer } from './PhotoViewer'

type Filter = 'all' | 'CarPhoto' | 'ConditionPhoto'

const FILTERS = [
  { value: 'all', label: 'All' },
  { value: 'CarPhoto', label: 'Car photos' },
  { value: 'ConditionPhoto', label: 'Condition' },
] as const

/**
 * Photos: the car's pictures, in a gallery rather than a filing cabinet.
 *
 * Photos are rows in `documents` like papers are; what makes one a photo is its category. Tiles group by the
 * month of the photo's own date, or of its upload day when it has none (assumed: A7), newest first, and the
 * viewer steps through them in the same order (assumed: A23). Thumbnails load as tiles come into view, and a
 * photo filed without previews gets them from the backfill the first time a browser that can decode it shows
 * this screen.
 */
export function PhotosPage() {
  const reg = useVehicleReg()
  const plate = usePlate()
  const [filter, setFilter] = useState<Filter>('all')
  const [viewing, setViewing] = useState<number | null>(null)
  const [retagging, setRetagging] = useState<DocumentItem | null>(null)
  const [uploading, setUploading] = useState(false)

  const { data, isPending, isError, error, refetch } = useDocuments(reg)
  const all = useMemo(() => newestFirst(data?.photos ?? []), [data])
  const shown = useMemo(() => (filter === 'all' ? all : all.filter((p) => p.type === filter)), [all, filter])

  useRenditionBackfill(reg, data?.photos)

  const months = useMemo(() => {
    const groups: Array<{ month: string; photos: Array<{ photo: DocumentItem; index: number }> }> = []
    shown.forEach((photo, index) => {
      const month = photoDay(photo).day.slice(0, 7)
      const last = groups[groups.length - 1]
      if (last?.month === month) last.photos.push({ photo, index })
      else groups.push({ month, photos: [{ photo, index }] })
    })
    return groups
  }, [shown])

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="photos"
      footer={
        <>
          The originals are kept exactly as uploaded, and Save gives them back byte for byte. The previews on this
          screen are copies your browser made, without the camera&rsquo;s metadata.
        </>
      }
      center={{ kind: 'action', icon: 'plus', label: 'Upload', onClick: () => setUploading(true) }}
    >
      <PageHead
        eyebrow="Photos · the car, and the evidence"
        title="Photos"
        plate={plate}
        pmeta={
          data === undefined ? undefined : (
            <>
              {all.length} {all.length === 1 ? 'photo' : 'photos'}
              <br />
              Condition photos are the baseline
              <br />
              "worsening" is measured against
            </>
          )
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel>
              <p className="panel-empty">
                {formError({ _: [error instanceof Error ? error.message : 'Could not load photos.'] }) ?? ''}{' '}
                <Mark onClick={() => refetch()}>Try again</Mark>
              </p>
            </Panel>
          </Wrap>
        </Section>
      ) : isPending ? (
        <Section last>
          <Wrap>
            <Panel>
              <p className="panel-empty">Loading photos…</p>
            </Panel>
          </Wrap>
        </Section>
      ) : (
        <Section last>
          <Wrap>
            <SectionHead
              title="Gallery"
              rule={<>tap a photo to open it · arrows and swipe step through</>}
              link={<Mark onClick={() => setUploading(true)}>Upload</Mark>}
            />
            {all.length === 0 ? (
              <Panel>
                <p className="panel-empty">
                  No photos yet. A condition set taken now is what a future argument about rust or cracking gets
                  measured against. <Mark onClick={() => setUploading(true)}>Upload some</Mark>.
                </p>
              </Panel>
            ) : (
              <>
                <div className="pfilter">
                  <Seg label="Show" options={FILTERS} value={filter} onChange={setFilter} />
                </div>
                {months.length === 0 ? (
                  <Panel>
                    <p className="panel-empty">No photos in this category.</p>
                  </Panel>
                ) : (
                  months.map(({ month, photos }) => (
                    <Month key={month} label={monthLabel(month)}>
                      {photos.map(({ photo, index }) => (
                        <Tile key={photo.id} reg={reg} photo={photo} onOpen={() => setViewing(index)} />
                      ))}
                    </Month>
                  ))
                )}
              </>
            )}
          </Wrap>
        </Section>
      )}

      {viewing !== null && (
        <PhotoViewer
          reg={reg}
          photos={shown}
          startIndex={viewing}
          onClose={() => setViewing(null)}
          onRetag={(photo) => {
            setViewing(null)
            setRetagging(photo)
          }}
        />
      )}
      <UploadSheet
        open={uploading}
        onClose={() => setUploading(false)}
        reg={reg}
        multiple
        initial={{ type: 'CarPhoto' }}
      />
      <RetagSheet document={retagging} onClose={() => setRetagging(null)} reg={reg} />
    </AppShell>
  )
}

function Month({ label, children }: { label: string; children: React.ReactNode }) {
  const id = useId()
  return (
    <section className="pmonth" aria-labelledby={id}>
      <h3 id={id} className="pmonth-head">
        {label}
      </h3>
      <div className="photos">{children}</div>
    </section>
  )
}

function Tile({ reg, photo, onOpen }: { reg: string; photo: DocumentItem; onOpen: () => void }) {
  const { day, uploaded } = photoDay(photo)
  return (
    <figure className="pcell">
      <PhotoThumb reg={reg} photo={photo} onOpen={onOpen} />
      <figcaption className="pcap">
        <span>{photo.title}</span>
        <span className="num">{uploaded ? `uploaded ${shortDate(day)}` : shortDate(day)}</span>
      </figcaption>
      {!photo.hasThumbnail && (
        <div className="pcap-acts">
          <Mark onClick={() => void openDocument(reg, photo, true)}>Save</Mark>
        </div>
      )}
    </figure>
  )
}
