import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../../api/client'
import { ApiFailure } from '../../api/queries'
import {
  useVehicleDeletion,
  useVehiclePatch,
  type VehicleDeletionSummary,
} from '../../api/vehicle'
import { Btn } from '../../components/Btn'
import { Panel } from '../../components/layout'
import { Seg } from '../../components/Seg'
import { Field, Sheet } from '../../components/Sheet'
import { fieldError, formError, reportApiError, type FieldErrors } from '../../lib/formErrors'
import { hrefFor } from '../../lib/link'
import { useNavigate } from 'react-router-dom'
import { useToast } from '../../shell/Toast'

/** The enum member names exactly. `SORN` is all caps in `VehicleStatus.cs` and the JSON round-trips by name. */
type Status = 'Active' | 'Sold' | 'SORN'

const STATUS_OPTIONS: { value: Status; label: string }[] = [
  { value: 'Active', label: 'Active' },
  { value: 'Sold', label: 'Sold' },
  { value: 'SORN', label: 'SORN' },
]

const plural = (n: number, one: string, many = `${one}s`) => `${n} ${n === 1 ? one : many}`

/** The database's own rule, so the gate agrees with the unique index and with every other screen. */
const normalise = (registration: string) => registration.replace(/\s+/g, '').toUpperCase()

/**
 * LIFECYCLE - what this car is now, and how to be rid of it.
 *
 * **Two blocks, separated, and only the second is dangerous.** Marking a car Sold is a reversible edit that
 * keeps every row; deleting it is neither. Putting them under one red heading would teach the reader that Sold
 * is frightening and that delete is one of a pair of similar things. The account screen makes the same split
 * for the same reason - export above, deletion last, one rule apart.
 *
 * The status control shipped years after the field did. `VehicleStatus` has been stored, check-constrained and
 * patchable since DEC-007, and `VehicleMetricsLoader` has said all along that "hiding Sold or SORN is
 * presentation, and the garage surfaces do it" - but no screen ever offered a way to set it, so no car was
 * ever anything but Active and the garage had nothing to hide.
 */
export function VehicleLifecyclePanel({ reg, status }: { reg: string; status: Status }) {
  const [confirming, setConfirming] = useState(false)

  const summary = useQuery({
    queryKey: ['vehicle', reg, 'deletion-summary'] as const,
    queryFn: async () => {
      const r = await apiRequest<VehicleDeletionSummary>(
        `/api/vehicles/${encodeURIComponent(reg)}/deletion-summary`,
      )
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
  })

  return (
    <Panel>
      <div style={{ padding: 18, display: 'grid', gap: 22, gridTemplateColumns: 'minmax(0, 1fr)' }}>
        <StatusBlock reg={reg} status={status} />

        <div
          style={{
            display: 'grid',
            gap: 10,
            gridTemplateColumns: 'minmax(0, 1fr)',
            borderTop: '1px solid var(--line)',
            paddingTop: 22,
          }}
        >
          <h3 style={{ margin: 0, fontSize: 15 }}>Delete this vehicle</h3>
          <p style={{ margin: 0, color: 'var(--muted)', fontSize: 13, maxWidth: '56ch' }}>
            Erases the car and its whole history - every fuel fill, expense, service record, check, task, issue
            and uploaded document. It cannot be undone and there is no copy kept.{' '}
            <b>If you have simply stopped using it, mark it Sold or SORN above</b>: that keeps everything and
            takes it out of your garage.
          </p>

          <div>
            {/* Disabled until the counts arrive, the same rule the account panel follows: a sheet that opened
                blank would be asking for consent on nothing. */}
            <Btn variant="danger" onClick={() => setConfirming(true)} disabled={summary.data === undefined}>
              Delete vehicle…
            </Btn>
          </div>
        </div>
      </div>

      {summary.data !== undefined && (
        <DeleteSheet
          open={confirming}
          onClose={() => setConfirming(false)}
          reg={reg}
          summary={summary.data}
        />
      )}
    </Panel>
  )
}

/**
 * The status control.
 *
 * A `<Seg>` rather than an editor in the vehicle sheet: `Seg`'s own doc calls it "one choice among N", which
 * is what a lifecycle is, and every editor in `VehicleEditSheet` is a form of typed fields under a "blank
 * leaves the stored value" merge contract that a three-way immediate choice does not fit.
 *
 * **It deliberately does not touch the default vehicle.** Clearing `IsDefault` on a car you marked Sold would
 * leave the account with no default at all, which is a state it can enter and never leave - `VehicleFactory`
 * sets the flag only for an owner's first vehicle and nothing else sets it. Worse, this is a *reversible*
 * operation, so setting the status back would not put the default back. Status and default are independent
 * axes, and only deleting a vehicle - which is not reversible - promotes a replacement.
 */
function StatusBlock({ reg, status }: { reg: string; status: Status }) {
  const { toast } = useToast()
  const patch = useVehiclePatch(reg)
  const [failure, setFailure] = useState<string | null>(null)

  const change = (next: Status) => {
    if (next === status) return

    setFailure(null)
    patch.mutate(
      { status: next },
      {
        onSuccess: () =>
          toast(
            next === 'Active'
              ? 'Back in your active garage'
              : `Marked ${next} · kept in full, hidden from the garage`,
          ),
        onError: (e) => setFailure(e instanceof Error ? e.message : 'Could not change the status.'),
      },
    )
  }

  return (
    <div style={{ display: 'grid', gap: 10, gridTemplateColumns: 'minmax(0, 1fr)' }}>
      <h3 style={{ margin: 0, fontSize: 15 }}>Status</h3>
      <p style={{ margin: 0, color: 'var(--muted)', fontSize: 13, maxWidth: '56ch' }}>
        A car marked <b>Sold</b> or <b>SORN</b> keeps its whole history and every figure derived from it. It
        drops out of your garage&rsquo;s default view and off the screens that ask for your attention, and you
        can bring it back at any time.
      </p>

      <div>
        <Seg label="Vehicle status" options={STATUS_OPTIONS} value={status} onChange={change} />
      </div>

      {failure !== null && (
        <p className="hint err" role="alert" style={{ margin: 0 }}>
          {failure}
        </p>
      )}
    </div>
  )
}

/**
 * The confirmation.
 *
 * **`ConfirmButton` is deliberately not used**, for the reason the account panel gives: its two-step is
 * calibrated for deleting one fuel fill from a table, which is the right weight for a mistake that takes
 * thirty seconds to re-enter. Four years of history is not that. The sheet states how much is about to go and
 * will not arm until the registration is typed out.
 */
function DeleteSheet({
  open,
  onClose,
  reg,
  summary,
}: {
  open: boolean
  onClose: () => void
  reg: string
  summary: VehicleDeletionSummary
}) {
  const navigate = useNavigate()
  const { toast } = useToast()
  const remove = useVehicleDeletion(reg)
  const [typed, setTyped] = useState('')
  const [errors, setErrors] = useState<FieldErrors>({})

  // Normalised on both sides, matching what the server compares and what the unique index considers one car.
  // Being stricter here than the server costs a re-type; being looser arms a button the server then refuses.
  const matches = normalise(typed) === normalise(summary.registration)

  const submit = () => {
    if (!matches) return

    remove.mutate(typed.trim(), {
      onSuccess: (result) => {
        // Navigate first. The cache removal in the hook would otherwise refetch every key this screen is still
        // observing, against a vehicle that has just stopped existing.
        navigate(hrefFor('garage'))
        toast(
          result.promotedRegistration === null || result.promotedRegistration === undefined
            ? `${summary.registration} deleted · its whole history went with it`
            : `${summary.registration} deleted · ${result.promotedRegistration} is now your default`,
        )
      },
      onError: (e) => setErrors(reportApiError(e, ['confirmRegistration'])),
    })
  }

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title={`Delete ${summary.registration}`}
      subtitle="Irreversible. Nothing is kept and nothing can be restored."
      onSubmit={submit}
      footer={
        <>
          <Btn variant="ghost" onClick={onClose}>
            Cancel
          </Btn>
          <Btn variant="danger" type="submit" onClick={() => {}} disabled={!matches || remove.isPending}>
            {remove.isPending ? 'Deleting…' : 'Delete vehicle'}
          </Btn>
        </>
      }
    >
      <div style={{ gridColumn: '1 / -1', display: 'grid', gap: 10 }}>
        <p style={{ margin: 0 }}>
          {summary.name} · {plural(summary.logEntryCount, 'log entry', 'log entries')},{' '}
          {plural(summary.checkDefinitionCount, 'check')}, {plural(summary.issueCount, 'issue')} and{' '}
          {plural(summary.documentCount, 'document')}.
        </p>
        <p style={{ margin: 0, color: 'var(--muted)', fontSize: 13 }}>
          All of it goes: every figure this car&rsquo;s screens compute, the uploaded document files
          themselves, the checks and budget groups you set up, and its place in your spend totals. Your
          garages, wash locations and expense categories are shared and stay.
          {summary.isDefault && (
            <>
              {' '}
              This is your <b>default vehicle</b>, so another car will take that place.
            </>
          )}
        </p>
      </div>

      <Field
        label="Type the registration to confirm"
        wide
        error={fieldError(errors, 'confirmRegistration')}
        hint={`Type ${summary.registration} exactly. The button below stays disabled until it matches.`}
      >
        {(p) => (
          <input
            type="text"
            autoComplete="off"
            value={typed}
            onChange={(e) => setTyped(e.target.value)}
            placeholder={summary.registration}
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
