import { useQueryClient } from '@tanstack/react-query'
import { useRef, useState, type CSSProperties } from 'react'
import {
  commitImport,
  isExpiredPreview,
  isRegistrationClash,
  previewImport,
  type ImportPreview,
  type ImportReport,
  type ImportRowCounts,
} from '../../api/import'
import { Btn } from '../../components/Btn'
import { Panel } from '../../components/layout'
import { Field } from '../../components/Sheet'
import { fieldError, formError, reportApiError, type FieldErrors } from '../../lib/formErrors'
import { useToast } from '../../shell/Toast'

/**
 * PUT IT BACK - an export file read into this account, beside the button that produced one.
 *
 * **Two steps, and the first writes nothing.** Choosing a file uploads it to a preview endpoint that reports
 * exactly what importing it would do and holds the parsed file for fifteen minutes under an opaque id; the
 * second call commits against that id and carries only decisions. The panel never re-sends the file, which is
 * the property the server's design depends on.
 *
 * **The warnings lead, and the first one is load-bearing.** Importing the same file twice silently succeeds,
 * producing a `-2` and then a `-3` copy of everything, because a colliding registration is renamed rather than
 * refused. "1 of 1 vehicle already exists in your garage" is the sentence that stops that, so it sits above the
 * vehicle list rather than beside a row.
 *
 * **An expired preview degrades to "upload it again", never to a dead button.** The server holds it in memory,
 * so a container restart forgets it - which costs a re-upload and must read as one.
 */
export function ImportPanel() {
  const { toast } = useToast()
  const queryClient = useQueryClient()
  const input = useRef<HTMLInputElement>(null)

  const [preview, setPreview] = useState<ImportPreview | null>(null)
  const [report, setReport] = useState<ImportReport | null>(null)
  const [plates, setPlates] = useState<Record<number, string>>({})
  const [errors, setErrors] = useState<FieldErrors>({})
  const [expired, setExpired] = useState(false)
  const [busy, setBusy] = useState(false)

  const reset = () => {
    setPreview(null)
    setReport(null)
    setPlates({})
    setErrors({})
    setExpired(false)
    // Cleared so choosing the same file again still fires a change event. Without it, re-uploading the file
    // you just cancelled does nothing at all and looks like a broken button.
    if (input.current) input.current.value = ''
  }

  const choose = async (file: File | undefined) => {
    if (file === undefined) return

    reset()
    setBusy(true)

    try {
      setPreview(await previewImport(file))
    } catch (failure) {
      setErrors(reportApiError(failure))
      if (input.current) input.current.value = ''
    } finally {
      setBusy(false)
    }
  }

  const commit = async () => {
    if (preview === null) return

    setBusy(true)
    setErrors({})

    try {
      const written = await commitImport(
        preview.importId,
        preview.vehicles.map((v) => ({
          index: v.index,
          include: true,
          registration: plates[v.index] ?? v.proposedRegistration,
        })),
      )

      setReport(written)
      setPreview(null)

      // Everything, and the rule is the chat's confirmed write: which screens went stale depends on what the
      // file contained, so narrowing this to a key list is a guess that is wrong for some file.
      await queryClient.invalidateQueries()

      toast(`Imported ${written.totalRows} rows into your garage`)
    } catch (failure) {
      if (isExpiredPreview(failure)) {
        setPreview(null)
        setExpired(true)
      } else {
        // The plate keys are declared, so a clash marks the field it is about rather than folding into the
        // footer banner. Everything else - a validation map keyed into the file, a network drop - still folds,
        // which is what `_` is for.
        setErrors(reportApiError(failure, preview.vehicles.map((v) => `vehicles[${v.index}].registration`)))
        // A clash is corrected in place: the preview stays open and the plate field is where the fix goes.
        if (!isRegistrationClash(failure)) setPreview(preview)
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <div
      style={{
        display: 'grid',
        gap: 10,
        gridTemplateColumns: 'minmax(0, 1fr)',
        borderTop: '1px solid var(--line)',
        paddingTop: 22,
      }}
    >
      <h3 style={{ margin: 0, fontSize: 15 }}>Bring data in</h3>
      <p style={{ margin: 0, color: 'var(--muted)', fontSize: 13, maxWidth: '56ch' }}>
        An export file - yours from another deployment, or one sent by the person you bought a car from - read
        back into this account. The cars land <b>beside</b> the ones you already have and nothing is replaced.
        You see exactly what it would do before anything is written. Document files, assistant tokens and the
        assistant&rsquo;s write history are not imported; data-integrity flags are worked out again from the
        rows once they land.
      </p>

      <div>
        {/* The input is inside its own label, so this is one control rather than a button that reaches for a
            hidden one - which would be two focus stops for a single action. Visually hidden rather than
            `hidden` or `display: none`: both of those take the input out of the tab order, and it is the
            focusable thing here. */}
        <label className="btn ghost" style={{ cursor: busy ? 'progress' : 'pointer' }}>
          {busy && preview === null ? 'Reading…' : 'Choose an export file…'}
          <input
            ref={input}
            type="file"
            accept="application/json,.json"
            disabled={busy}
            onChange={(e) => void choose(e.target.files?.[0])}
            style={VISUALLY_HIDDEN}
          />
        </label>
      </div>

      {expired && (
        <p className="hint err" role="alert" style={{ margin: 0 }}>
          That upload is no longer held - a preview lasts fifteen minutes. Choose the file again; nothing was
          written.
        </p>
      )}

      {formError(errors) !== undefined && (
        <p className="hint err" role="alert" style={{ margin: 0 }}>
          {formError(errors)}
        </p>
      )}

      {preview !== null && (
        <PreviewPanel
          preview={preview}
          plates={plates}
          errors={errors}
          busy={busy}
          onPlate={(index, value) => setPlates((p) => ({ ...p, [index]: value }))}
          onCommit={() => void commit()}
          onCancel={reset}
        />
      )}

      {report !== null && <ReportPanel report={report} />}
    </div>
  )
}

function PreviewPanel({
  preview,
  plates,
  errors,
  busy,
  onPlate,
  onCommit,
  onCancel,
}: {
  preview: ImportPreview
  plates: Record<number, string>
  errors: FieldErrors
  busy: boolean
  onPlate: (index: number, value: string) => void
  onCommit: () => void
  onCancel: () => void
}) {
  const exported = new Date(preview.source.exportedAt)

  // `.attn.attn-info` is the house shape for exactly this - prose left, actions right, on a `1fr auto` grid -
  // and it is what the data-integrity panel and the fix banner already use. Inventing a fourth callout for an
  // import preview is the mistake FixBanner's own comment records making once.
  return (
    <Panel className="attn attn-info">
      <div style={{ display: 'grid', gap: 12, gridTemplateColumns: 'minmax(0, 1fr)' }}>
        <div className="attn-k">What this file would do</div>
        <p style={{ margin: 0, fontSize: 13 }}>
          Exported <b>{exported.toLocaleDateString()}</b>
          {preview.source.email !== null && preview.source.email !== undefined && (
            <>
              {' '}
              by <b>{preview.source.email}</b>
            </>
          )}
          {preview.source.schemaVersion !== null && preview.source.schemaVersion !== undefined && (
            <> · version {preview.source.schemaVersion}</>
          )}
        </p>

        {/* First, before the list. The count of what already exists is what stops a second import of a file
            somebody has already brought in, and it stops being that if it is a detail beside a row. */}
        {preview.warnings.length > 0 && (
          <ul style={{ margin: 0, paddingLeft: 18, display: 'grid', gap: 6, fontSize: 13 }}>
            {preview.warnings.map((warning) => (
              <li key={warning}>{warning}</li>
            ))}
          </ul>
        )}

        {preview.vehicles.length === 0 ? (
          <p style={{ margin: 0, fontSize: 13 }}>This export holds no vehicles, so there is nothing to import.</p>
        ) : (
          <div style={{ display: 'grid', gap: 14, gridTemplateColumns: 'minmax(0, 1fr)' }}>
            {preview.vehicles.map((vehicle) => (
              <div key={vehicle.index} style={{ display: 'grid', gap: 6, gridTemplateColumns: 'minmax(0, 1fr)' }}>
                <p style={{ margin: 0, fontSize: 13 }}>
                  <b>{vehicle.registration}</b>
                  {vehicle.description !== '' && <> · {vehicle.description}</>}
                  <br />
                  <span style={{ color: 'var(--muted)' }}>{summarise(vehicle.rows)}</span>
                </p>

                {vehicle.collides && (
                  <Field
                    label={`Registration for the imported ${vehicle.registration}`}
                    wide
                    error={fieldError(errors, `vehicles[${vehicle.index}].registration`)}
                    hint={`You already own ${vehicle.registration}, so this copy needs a different plate. It is
                      recorded in the car's notes as having been ${vehicle.registration}.`}
                  >
                    {(p) => (
                      <input
                        type="text"
                        autoComplete="off"
                        maxLength={16}
                        value={plates[vehicle.index] ?? vehicle.proposedRegistration}
                        onChange={(e) => onPlate(vehicle.index, e.target.value)}
                        {...p}
                      />
                    )}
                  </Field>
                )}

                {vehicle.skipped.documents > 0 && (
                  <p style={{ margin: 0, fontSize: 12, color: 'var(--muted)' }}>
                    {vehicle.skipped.documents} document {vehicle.skipped.documents === 1 ? 'record' : 'records'}{' '}
                    skipped - the export carries their details, not the files.
                  </p>
                )}
              </div>
            ))}
          </div>
        )}

        <p style={{ margin: 0, fontSize: 12, color: 'var(--muted)' }}>
          Reference lists: {preview.reference.garages.willCreate} of {preview.reference.garages.inFile} garages,{' '}
          {preview.reference.washLocations.willCreate} of {preview.reference.washLocations.inFile} wash locations
          and {preview.reference.expenseCategories.willCreate} of {preview.reference.expenseCategories.inFile}{' '}
          expense categories will be added. The rest you already have, and yours are left exactly as they are.
        </p>
      </div>

      <div className="attn-act">
        <Btn onClick={onCommit} disabled={busy || preview.vehicles.length === 0}>
          {busy ? 'Importing…' : 'Import'}
        </Btn>
        <button className="mark" type="button" onClick={onCancel} disabled={busy}>
          Cancel
        </button>
      </div>
    </Panel>
  )
}

function ReportPanel({ report }: { report: ImportReport }) {
  return (
    <Panel className="attn attn-info">
      <div style={{ display: 'grid', gap: 8, gridTemplateColumns: 'minmax(0, 1fr)', fontSize: 13 }} role="status">
        <div className="attn-k">Imported</div>
        <p style={{ margin: 0 }}>
          <b>{`Imported ${report.totalRows} rows.`}</b>
        </p>
        <ul style={{ margin: 0, paddingLeft: 18, display: 'grid', gap: 4 }}>
          {report.vehicles.map((vehicle) => (
            <li key={vehicle.registration}>
              <b>{vehicle.registration}</b>
              {/* Interpolated whole rather than assembled from fragments: React renders a fragment sequence as
                  several text nodes, so "was BT53 AKJ" would exist on screen and match nothing looking for it -
                  which is a real problem for a screen reader as well as for a test. */}
              {vehicle.registration !== vehicle.importedFrom && ` (was ${vehicle.importedFrom})`}
              {` · ${vehicle.rows} rows`}
              {vehicle.anomaliesRaised > 0 &&
                ` · ${vehicle.anomaliesRaised} data-integrity ` +
                  `${vehicle.anomaliesRaised === 1 ? 'flag' : 'flags'} raised`}
            </li>
          ))}
        </ul>
        {(report.skipped.documents > 0 ||
          report.skipped.assistantTokens > 0 ||
          report.skipped.auditEntries > 0) && (
          <p style={{ margin: 0, color: 'var(--muted)' }}>
            {`Not imported: ${report.skipped.documents} document records, ` +
              `${report.skipped.assistantTokens} assistant tokens and ` +
              `${report.skipped.auditEntries} assistant write-audit entries.`}
          </p>
        )}
      </div>
    </Panel>
  )
}

/**
 * Off-screen but not out of the tab order.
 *
 * `display: none` and the `hidden` attribute both remove the element from focus, and a file input that cannot
 * be reached by keyboard is a file input nobody using a keyboard can use.
 */
const VISUALLY_HIDDEN: CSSProperties = {
  position: 'absolute',
  width: 1,
  height: 1,
  padding: 0,
  margin: -1,
  overflow: 'hidden',
  clip: 'rect(0 0 0 0)',
  whiteSpace: 'nowrap',
  border: 0,
}

/** "2 fills, 1 service record, 15 checks" - only the tables that have anything in them. */
function summarise(rows: ImportRowCounts): string {
  const parts: string[] = []
  const add = (n: number, one: string, many = `${one}s`) => {
    if (n > 0) parts.push(`${n} ${n === 1 ? one : many}`)
  }

  add(rows.fuelEntries, 'fill')
  add(rows.expenses, 'expense')
  add(rows.serviceRecords, 'service record')
  add(rows.mileageReadings, 'mileage reading')
  add(rows.checkDefinitions, 'check')
  add(rows.checkLogs, 'check log')
  add(rows.tyreReadings, 'tyre reading')
  add(rows.washEntries, 'wash')
  add(rows.tasks, 'task')
  add(rows.issues, 'issue')
  add(rows.equipment, 'equipment item')
  add(rows.budgetGroups, 'budget group')

  return parts.length === 0 ? 'no log entries' : parts.join(', ')
}
