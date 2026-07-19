import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { IntegrityPill } from '../components/Pill'
import { Field, Sheet } from '../components/Sheet'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { formError, reportApiError, type FieldErrors } from '../lib/formErrors'
import { AppLink } from '../lib/link'
import { usePlate } from '../lib/usePlate'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'

interface AnomalyItem {
  id: number
  kind: string
  severity: string
  entityType: string
  entityId: number | null
  message: string
  detail: string | null
  status: string
  resolvedAt: string | null
  resolutionNote: string | null
  createdAt: string
}

type Resolution = 'Corrected' | 'Accepted' | 'Dismissed'

/**
 * What each detector is looking for, in the reader's terms rather than the enum's.
 *
 * `Record<Kind, …>` off the wire enum, so a fourth detector fails the build here instead of rendering its
 * enum name — the mistake the mileage screen's hand-guessed origin map already made once.
 */
const KIND: Record<string, { title: string; why: string }> = {
  MileageNonMonotonic: {
    title: 'A reading is above a later one',
    why: 'A mileage cannot go down, so one of the two is a typo. Which one is not ours to guess — the odometer keeps deriving from the newest reading by date, and nothing has been changed.',
  },
  ImplausibleMpg: {
    title: 'An MPG outside what the car can do',
    why: 'Computed correctly from exact litres and still not real — usually a missed fill or a mistyped odometer. It is excluded from the averages and kept on the entry: marked, not deleted.',
  },
  FuelCostDiscrepancy: {
    title: 'A fill costs what its litres and price do not',
    why: 'Litres times price per litre does not reach the total on the receipt. Receipts round, so a penny is normal and this is not that.',
  },
}

/** The three terminal statuses, and the difference that makes them worth distinguishing. */
const RESOLUTIONS: { status: Resolution; label: string; help: string }[] = [
  {
    status: 'Corrected',
    label: 'Corrected',
    help: 'I fixed the underlying data. The detector re-checks — if the condition comes back, the fix did not hold and so does this flag.',
  },
  {
    status: 'Accepted',
    label: 'Accepted',
    help: 'The data is right and the flag is a false positive. It stays down.',
  },
  {
    status: 'Dismissed',
    label: 'Dismissed',
    help: 'Not worth acting on. It stays down.',
  },
]

const when = (iso: string) =>
  new Date(iso).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

/**
 * The data-integrity queue.
 *
 * **A list, not a table.** Each flag is a claim with a comparison and a decision attached; there are no columns
 * worth aligning, and forcing `<DataTable>` on prose is the wrong-abstraction failure the seam exists to avoid.
 * Checks stayed a list for the same reason.
 *
 * **Blue throughout, and never a due tone.** Integrity is its own axis (see `lib/status.ts`): "this datum is
 * unreliable" is a different question from "this is overdue", and the design's DETECTORS panel conflates them
 * by listing "Check never logged" here. That one is `CheckStatus.NeverLogged` on the due axis and stays there.
 *
 * Severity orders the queue. It does not become green/amber/rust.
 */
export function DataIntegrityPage() {
  const reg = useVehicleReg()
  const plate = usePlate()
  const [showAll, setShowAll] = useState(false)
  const [resolving, setResolving] = useState<AnomalyItem | null>(null)

  const { data, isPending, isError, error, refetch } = useQuery({
    queryKey: ['vehicle', reg, 'anomalies', showAll ? 'all' : 'open'] as const,
    queryFn: async () => {
      const result = await apiRequest<AnomalyItem[]>(
        `/api/vehicles/${encodeURIComponent(reg)}/anomalies${showAll ? '?status=all' : ''}`,
      )
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
  })

  const open = (data ?? []).filter((a) => a.status === 'Open')
  const resolved = (data ?? []).filter((a) => a.status !== 'Open')

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="data-integrity"
      center={null}
      footer={
        <>
          A flag <b>never blocks a save</b> (§5.3). The entry is recorded as given and then questioned — the
          alternative is an app that silently corrects your data, which is how a spreadsheet ends up with a
          figure nobody can trace. Nothing here is deleted; a resolved flag keeps its row and its reason.
        </>
      }
    >
      <PageHead
        eyebrow="Data integrity · computed live"
        title="Data integrity"
        plate={plate}
        pmeta={
          data === undefined ? undefined : (
            <>
              <b>{open.length} open</b>
              {resolved.length > 0 && <> · {resolved.length} resolved</>}
              <br />
              Three detectors run on every write —<br />
              they flag, they never refuse
            </>
          )
        }
      />

      {isError ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <h2 className="panel-title">The queue could not be loaded</h2>
              <p className="panel-empty">{error instanceof Error ? error.message : 'The request failed.'}</p>
              <button className="btn" type="button" onClick={() => void refetch()}>
                Try again
              </button>
            </Panel>
          </Wrap>
        </Section>
      ) : isPending || data === undefined ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <p className="panel-empty">Loading…</p>
            </Panel>
          </Wrap>
        </Section>
      ) : (
        <Section last>
          <Wrap>
            <SectionHead
              title={showAll ? 'Every flag' : 'Open flags'}
              rule={<>worst first</>}
              link={
                <Mark onClick={() => setShowAll((s) => !s)}>
                  {showAll ? 'Open only' : 'Show resolved'}
                </Mark>
              }
            />

            {open.length === 0 && !showAll ? (
              <Panel>
                <p className="panel-empty">
                  Nothing flagged. The three detectors run on every write — mileage that goes backwards, an MPG
                  outside what the car can do, and a fill whose cost does not match its litres — and none of
                  them has anything to say about this vehicle's data.
                </p>
              </Panel>
            ) : (
              <Panel className="integrity">
                <ul className="ilist">
                  {[...open, ...(showAll ? resolved : [])].map((a) => (
                    <li key={a.id} className={a.status === 'Open' ? undefined : 'is-resolved'}>
                      <div className="iw">
                        <IntegrityPill>{a.status === 'Open' ? a.severity : a.status}</IntegrityPill>
                        <span>{KIND[a.kind]?.title ?? a.message}</span>
                      </div>

                      {/* `Message` is the detector's own prose and it already names both figures — "Reading of
                          83,000 mi on 27 Jun 2026 is above the current 80,900 mi from 16 Jul 2026". `Detail`
                          is NOT prose: it is the machine-readable pair, `{"mileage":83000,"currentMileage":
                          80900}`, for tooling and for MCP. Rendering it raw put JSON on the page — which is
                          what the first version of this screen did, because the test mocked prose. */}
                      <div className="cmp num">{a.message}</div>

                      <p>{KIND[a.kind]?.why ?? ''}</p>

                      <div className="ifoot">
                        <span className="imeta">
                          Raised {when(a.createdAt)} · {a.entityType.toLowerCase()}
                          {a.entityId !== null && ` #${a.entityId}`}
                        </span>
                        {a.status === 'Open' ? (
                          <Mark onClick={() => setResolving(a)}>Resolve</Mark>
                        ) : (
                          <span className="imeta">
                            {a.status} {a.resolvedAt !== null && `· ${when(a.resolvedAt)}`}
                            {a.resolutionNote !== null && ` · "${a.resolutionNote}"`}
                          </span>
                        )}
                      </div>
                    </li>
                  ))}
                </ul>
              </Panel>
            )}

            {open.length > 0 && (
              <p className="ifootnote">
                The odometer, the averages and every countdown are computed as though these flags were not
                here — a flagged reading is not excluded from the log, it is questioned in it.{' '}
                <AppLink to="mileage" reg={reg}>
                  Mileage log
                </AppLink>
              </p>
            )}
          </Wrap>
        </Section>
      )}

      <ResolveSheet anomaly={resolving} onClose={() => setResolving(null)} reg={reg} />
    </AppShell>
  )
}

function ResolveSheet({
  anomaly,
  onClose,
  reg,
}: {
  anomaly: AnomalyItem | null
  onClose: () => void
  reg: string
}) {
  const [status, setStatus] = useState<Resolution>('Accepted')
  const [note, setNote] = useState('')
  const [errors, setErrors] = useState<FieldErrors>({})
  const queryClient = useQueryClient()
  const { toast } = useToast()

  // Resolution and note are always valid to submit (a note is optional, the status is a fixed pick), so there is
  // nothing to reject client-side — any server refusal falls to the footer banner.
  const FIELD_KEYS = [] as const

  const mutation = useMutation({
    mutationFn: async () => {
      const result = await apiRequest<AnomalyItem>(
        `/api/vehicles/${encodeURIComponent(reg)}/anomalies/${anomaly!.id}`,
        {
          method: 'PATCH',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ status, resolutionNote: note || null }),
        },
      )
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'anomalies'] })
      await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
      await queryClient.invalidateQueries({ queryKey: queryKeys.garage })
      toast(
        status === 'Corrected'
          ? 'Marked corrected · the detector re-checks on the next write'
          : `Marked ${status.toLowerCase()} · it stays down`,
      )
      setNote('')
      setErrors({})
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  const chosen = RESOLUTIONS.find((r) => r.status === status)!

  return (
    <Sheet
      open={anomaly !== null}
      onClose={onClose}
      title="Resolve flag"
      subtitle="the row stays; this records what you decided"
      onSubmit={() => mutation.mutate()}
      footer={
        <Btn type="submit" onClick={() => {}}>
          {mutation.isPending ? 'Saving…' : `Mark ${status.toLowerCase()}`}
        </Btn>
      }
    >
      {anomaly !== null && (
        <div className="field wide">
          <span className="hint">{anomaly.message}</span>
        </div>
      )}

      <Field label="Resolution" wide hint={chosen.help}>
        {(p) => (
          <select value={status} onChange={(e) => setStatus(e.target.value as Resolution)} {...p}>
            {RESOLUTIONS.map((r) => (
              <option key={r.status} value={r.status}>
                {r.label}
              </option>
            ))}
          </select>
        )}
      </Field>

      <Field label="Note" wide hint="why — this is the part a queue is for">
        {(p) => (
          <input
            type="text"
            placeholder="80,300 mistyped as 83,000; corrected on the record"
            value={note}
            onChange={(e) => setNote(e.target.value)}
            {...p}
          />
        )}
      </Field>

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
