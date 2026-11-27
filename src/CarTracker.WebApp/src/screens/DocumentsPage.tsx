import { useMemo, useState } from 'react'
import {
  categoryLabel,
  GROUP_LABEL,
  groupLabel,
  openDocument,
  useDocuments,
  useDocumentTypes,
  type DocumentGroup,
  type DocumentItem,
} from '../api/documents'
import { Mark } from '../components/Btn'
import { Absent, DataTable, Sub, type Column } from '../components/DataTable'
import { TableControls } from '../components/TableControls'
import { useTableView, type FilterGroup, type TableSearch } from '../components/useTableView'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { formError } from '../lib/formErrors'
import { AppLink } from '../lib/link'
import { usePlate } from '../lib/usePlate'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { fileSize, shortDate } from './documents/format'
import { LinkChip } from './documents/LinkChip'
import { RetagSheet } from './documents/RetagSheet'
import { UploadSheet } from './documents/UploadSheet'
import { useFilePrefill } from './documents/useFilePrefill'

/** The paper groups, in the server's order; Photos is the other screen's. */
const PAPER_GROUPS = (Object.keys(GROUP_LABEL) as DocumentGroup[]).filter((g) => g !== 'Photos')

/**
 * Documents: the car's papers, filed under the server's categories.
 *
 * Photos moved to their own screen with the gallery; what is left here is a table of aligned facts, so it goes
 * through `<DataTable>` and `useTableView` like every other log, with search over everything an owner might
 * remember about a paper (its title, its notes, its category and the record it is attached to).
 */
export function DocumentsPage() {
  const reg = useVehicleReg()
  const plate = usePlate()
  const [editing, setEditing] = useState<DocumentItem | null>(null)
  const [uploading, setUploading] = useState(false)
  // A filing link landed here: the upload sheet opens with its category, title, date and record chosen.
  const prefill = useFilePrefill()

  const { data, isPending, isError, error, refetch } = useDocuments(reg)
  const { data: types } = useDocumentTypes()
  const papers = data?.papers ?? []

  const groups: FilterGroup<DocumentItem>[] = useMemo(
    () => [
      {
        id: 'group',
        label: 'Group',
        render: 'select',
        options: PAPER_GROUPS.map((g) => ({ id: g, label: GROUP_LABEL[g], test: (d) => d.group === g })),
      },
    ],
    [],
  )

  const search: TableSearch<DocumentItem> = useMemo(
    () => ({
      label: 'Search papers',
      fields: (d) => [d.title, d.notes, categoryLabel(d.type, types), d.linkedTo?.label],
    }),
    [types],
  )

  const view = useTableView(papers, { groups, search })

  const columns: Column<DocumentItem>[] = [
    {
      key: 'category',
      label: 'Category',
      width: '128px',
      priority: 'essential',
      render: (d) => (
        <>
          <span className="dchip">{categoryLabel(d.type, types)}</span>
          <Sub>{groupLabel(d.group)}</Sub>
        </>
      ),
    },
    {
      key: 'title',
      label: 'Title',
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
      label: 'Date',
      width: '96px',
      priority: 'normal',
      render: (d) => (d.documentDate === null ? <Absent>not dated</Absent> : shortDate(d.documentDate)),
    },
    {
      key: 'link',
      label: 'Linked to',
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
      render: (d) => (
        <span className="dacts">
          {/* Buttons, not anchors: the bytes need our Authorization header, so they come through fetch and
              become an object URL rather than a plain navigation the browser would send unauthenticated. */}
          <Mark onClick={() => void openDocument(reg, d, false)}>View</Mark>
          <Mark onClick={() => void openDocument(reg, d, true)}>Save</Mark>
        </span>
      ),
    },
  ]

  const photoCount = data?.photos.length ?? 0

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="documents"
      footer={
        <>
          The bytes live on a mounted volume and the row keeps the path, never in the database. Download returns
          the original file untouched; nothing is re-encoded on the way out.
        </>
      }
      center={{ kind: 'action', icon: 'plus', label: 'Upload', onClick: () => setUploading(true) }}
    >
      <PageHead
        eyebrow="Documents · papers, filed by category"
        title="Documents"
        plate={plate}
        pmeta={
          data === undefined ? undefined : (
            <>
              {papers.length} {papers.length === 1 ? 'paper' : 'papers'}
              <br />
              A paper can link to a service record,
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
        <Section last>
          <Wrap>
            <SectionHead
              title="Papers"
              rule={<>tap to re-tag · download keeps the original file</>}
              link={<Mark onClick={() => setUploading(true)}>Upload</Mark>}
            />
            {papers.length === 0 ? (
              <Panel>
                <p className="panel-empty">
                  No papers filed. The V5C, the insurance certificate and the MOT pass are the three worth having
                  here. <Mark onClick={() => setUploading(true)}>Upload one</Mark>.
                </p>
              </Panel>
            ) : (
              <>
                <TableControls view={view} noun="papers" />
                {view.count === 0 ? (
                  <Panel>
                    <p className="panel-empty">No papers match.</p>
                  </Panel>
                ) : (
                  <DataTable
                    columns={columns}
                    rows={view.rows}
                    rowKey={(d) => d.id}
                    label="Filed papers"
                    onRowClick={(d) => setEditing(d)}
                    rowLabel={(d) => `Re-tag ${d.title}`}
                  />
                )}
              </>
            )}
            <p className="ifootnote">
              {photoCount === 0 ? 'Pictures of the car' : `${photoCount} ${photoCount === 1 ? 'photo' : 'photos'}`}{' '}
              live on <AppLink to="photos" reg={reg}>Photos</AppLink>, in a gallery rather than a table.
            </p>
          </Wrap>
        </Section>
      )}

      <UploadSheet open={uploading && prefill.initial === null} onClose={() => setUploading(false)} reg={reg} />
      <UploadSheet open={prefill.initial !== null} onClose={prefill.clear} reg={reg} initial={prefill.initial ?? {}} />
      <RetagSheet document={editing} onClose={() => setEditing(null)} reg={reg} />
    </AppShell>
  )
}
