import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { anomalyKeys, useAnomalies, type AnomalyEntityType, type AnomalyItem } from '../api/anomalies'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys } from '../api/queries'
import { Btn, Mark } from '../components/Btn'
import { IntegrityPill } from '../components/Pill'
import { Field, Sheet } from '../components/Sheet'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { ANOMALY_KIND, FIX_SCREEN } from '../lib/anomalyCopy'
import { formError, reportApiError, type FieldErrors } from '../lib/formErrors'
import { AppLink } from '../lib/link'
import { usePlate } from '../lib/usePlate'
import { useVehicleReg } from '../routes'
import type { ScreenId } from '../shell/nav'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { useToast } from '../shell/Toast'

type Resolution = 'Corrected' | 'Accepted' | 'Dismissed'

/** The three terminal statuses, and the difference that makes them worth distinguishing. */
const RESOLUTIONS: { status: Resolution; label: string; help: string }[] = [
  {
    status: 'Corrected',
    label: 'Corrected',
    help: 'I fixed the underlying data. You rarely need this: a fix made through the app retracts its own flag on the next write. Use it for a correction the detectors cannot see.',
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

  const { data, isPending, isError, error, refetch } = useAnomalies(reg, showAll ? 'all' : 'open')

  const open = (data ?? []).filter((a) => a.status === 'Open')
  const resolved = (data ?? []).filter((a) => a.status !== 'Open')

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="data-integrity"
      center={null}
      footer={
        <>
          A flag <b>never blocks a save</b>. The entry is recorded as given and then questioned — the
          alternative is an app that silently corrects your data, and a figure nobody can trace is worse than a
          figure you were asked about. Nothing here is deleted; a resolved flag keeps its row and its reason.{' '}
          <b>Fix this</b> opens the row that caused a flag on the screen that owns it, and correcting the data
          retracts the flag on the next write — so the queue stays a list of what is wrong <i>now</i>, without
          anyone having to tell it.
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
              Four detectors run on every write —<br />
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
                  Nothing flagged. The four detectors run on every write — mileage that goes backwards, an MPG
                  outside what the car can do, a fill whose cost does not match its litres, and kit with a
                  price but no purchase date — and none of them has anything to say about this vehicle's data.
                </p>
              </Panel>
            ) : (
              <Panel className="integrity">
                <ul className="ilist">
                  {[...open, ...(showAll ? resolved : [])].map((a) => {
                    // `entityType` is a plain string on the wire — the domain writes `nameof(T)` and nothing
                    // checks it against this client's union. So the lookup is widened to allow a miss, and a
                    // type this build does not know simply offers no Fix link rather than routing to
                    // `undefined`, which `hrefFor` would throw on.
                    const fixScreen = FIX_SCREEN[a.entityType as AnomalyEntityType] as ScreenId | undefined

                    return (
                    <li key={a.id} className={a.status === 'Open' ? undefined : 'is-resolved'}>
                      <div className="iw">
                        <IntegrityPill>{a.status === 'Open' ? a.severity : a.status}</IntegrityPill>
                        <span>{ANOMALY_KIND[a.kind].title}</span>
                      </div>

                      {/* `Message` is the detector's own prose and it already names both figures — "Reading of
                          83,000 mi on 27 Jun 2026 is above the current 80,900 mi from 16 Jul 2026". `Detail`
                          is NOT prose: it is the machine-readable pair, `{"mileage":83000,"currentMileage":
                          80900}`, for tooling and for MCP. Rendering it raw put JSON on the page — which is
                          what the first version of this screen did, because the test mocked prose. */}
                      <div className="cmp num">{a.message}</div>

                      <p>{ANOMALY_KIND[a.kind].why}</p>

                      <div className="ifoot">
                        <span className="imeta">
                          Raised {when(a.createdAt)} · {a.entityType.toLowerCase()}
                          {a.entityId !== null && ` #${a.entityId}`}
                        </span>
                        {a.status === 'Open' ? (
                          <>
                            {/* The action that actually changes something. It carries the flag's id, not the
                                row's, so the screen it lands on can check the flag belongs to the kind of row
                                it shows and refuse a link that does not — and can say why you are there
                                without re-deriving the reason. See `lib/useFlagFix.ts`.

                                Routed by the entity type rather than the kind, because one kind can name
                                different rows: a future-dated bill is a service record here and a fill there. */}
                            {fixScreen !== undefined && (
                              <AppLink to={fixScreen} reg={reg} query={{ flag: a.id }} className="mark">
                                Fix this →
                              </AppLink>
                            )}
                            <Mark onClick={() => setResolving(a)}>Resolve…</Mark>
                          </>
                        ) : (
                          <span className="imeta">
                            {a.status} {a.resolvedAt !== null && `· ${when(a.resolvedAt)}`}
                            {a.resolutionNote !== null && ` · "${a.resolutionNote}"`}
                          </span>
                        )}
                      </div>
                    </li>
                    )
                  })}
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
      await queryClient.invalidateQueries({ queryKey: anomalyKeys.all(reg) })
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
