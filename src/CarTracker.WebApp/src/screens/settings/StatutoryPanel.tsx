import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import type { VehicleSummary } from '../../api/client'
import { apiRequest } from '../../api/client'
import { ApiFailure, queryKeys } from '../../api/queries'
import { Btn, Mark } from '../../components/Btn'
import { Panel } from '../../components/layout'
import { Field, Sheet } from '../../components/Sheet'
import { AppLink } from '../../lib/link'
import { Icon } from '../../components/Icon'
import { useToast } from '../../shell/Toast'

const date = (iso: string | null) =>
  iso === null ? null : new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

type EditKey = 'ved' | 'insurance' | 'motSeed' | null

/**
 * Statutory dates and the insurance policy — the inputs the dashboard's countdowns run on.
 *
 * Until this existed, `CreateVehicleRequest` reached 11 of the Vehicle's ~30 fields and none of the four
 * `RenewalCalculator` reads, so no vehicle could ever show a renewal.
 */
/** Only the stored inputs the edit sheet seeds from — the rest of VehicleDetail is VehicleInfoPage's concern. */
interface StatutoryDetail {
  vedAnnualCost: number | null
  insurance: Record<string, string | number | null>
}

export function StatutoryPanel({ reg, summary }: { reg: string; summary: VehicleSummary | undefined }) {
  const [editing, setEditing] = useState<EditKey>(null)

  // The stored inputs (insurer, policy number, cover dates, premium, VED cost) live on the vehicle record, not
  // the derived summary — so the edit sheet reads them from here rather than opening blank behind placeholders.
  const { data: detail } = useQuery({
    queryKey: ['vehicle', reg, 'detail'] as const,
    queryFn: async () => {
      const r = await apiRequest<StatutoryDetail>(`/api/vehicles/${encodeURIComponent(reg)}`)
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
  })

  if (summary === undefined) return null

  const { mot, insurance, roadTax } = summary.renewals

  // The seed the edit sheet preloads. Expiry dates come from the summary (the countdowns already derive them);
  // the insurer, policy and premium come from the vehicle detail. `str` keeps a null out of the input.
  const str = (v: string | number | null | undefined) => (v == null ? '' : String(v))
  const ins = detail?.insurance ?? {}
  const seed: Record<string, string> = {
    motExpirySeed: str(mot.expiryDate),
    vedExpiry: str(roadTax.expiryDate),
    vedAnnualCost: str(detail?.vedAnnualCost),
    insurer: str(ins['insurer']),
    policyNumber: str(ins['policyNumber']),
    periodStart: str(ins['periodStart']),
    periodEnd: str(ins['periodEnd']),
    coverType: str(ins['coverType']),
    premium: str(ins['premium']),
  }

  return (
    <>
      <Panel>
        {/* The MOT is not a row with an Edit button, and that is the point of the whole project in one
            control. It derives from the latest pass record; a stored copy is exactly how the spreadsheet came
            to show a red 23-day countdown for a test that had already passed. There is no field for it in the
            API either — see UpdateVehicleRequest. */}
        <div className="derived num">
          <span className="lockico">Derived · read-only</span>
          <span>
            MOT expiry <b>{date(mot.expiryDate) ?? 'no record yet'}</b>
          </span>
          <span style={{ flexBasis: '100%', color: 'var(--muted)' }}>
            {mot.source === null ? (
              <>
                No MOT record yet, and no seed. Add the pass record and this fills itself in — or seed it below
                until you do.
              </>
            ) : (
              <>
                {mot.source}. It cannot be typed here — a stored copy is how the old spreadsheet ended up
                showing a red countdown for a test that had already passed.{' '}
                <AppLink to="service" reg={reg}>
                  Source record <Icon name="arrow-right" />
                </AppLink>
              </>
            )}
          </span>
        </div>

        {/* The seed, and only while there is no record to derive from.
         *
         * This is a narrow escape hatch, not a way to type the MOT: RenewalCalculator consults the seed ONLY
         * when the vehicle has no MOT record, and a pass record always wins. That asymmetry is what makes it
         * safe — the failure it protects against is a stored copy OVERRIDING a real record, which is precisely
         * what the spreadsheet did. Once a record exists this row disappears, because there is nothing left
         * for it to answer. */}
        {mot.source === null && (
          <div className="setrow num">
            <span className="sk">MOT expiry · seed</span>
            <span className="sv">
              not seeded
              <i>used only until an MOT record exists — a pass record always wins</i>
            </span>
            <Mark onClick={() => setEditing('motSeed')}>Seed</Mark>
          </div>
        )}

        <div className="setrow num">
          <span className="sk">Road tax · VED</span>
          <span className="sv">
            {date(roadTax.expiryDate) ?? 'not recorded'}
            <i>{roadTax.daysRemaining === null ? 'no renewal date' : `${roadTax.daysRemaining} days`}</i>
          </span>
          <Mark onClick={() => setEditing('ved')}>Edit</Mark>
        </div>

        <div className="setrow num">
          <span className="sk">Insurance</span>
          <span className="sv">
            {insurance.expiryDate === null ? 'not recorded' : date(insurance.expiryDate)}
            {/* The countdown first, in the same register as the row above it — both drive the same dashboard
                panel, so both should say the same kind of thing. The insurer is additive, not a substitute:
                naming it where the neighbouring row states days left reads as "no countdown available". */}
            <i>
              {insurance.daysRemaining === null ? 'no renewal date' : `${insurance.daysRemaining} days`}
              {insurance.source !== null && ` · ${insurance.source}`}
            </i>
          </span>
          <Mark onClick={() => setEditing('insurance')}>Edit</Mark>
        </div>
      </Panel>

      <EditSheet reg={reg} which={editing} onClose={() => setEditing(null)} seed={seed} />
    </>
  )
}

function EditSheet({
  reg,
  which,
  onClose,
  seed,
}: {
  reg: string
  which: EditKey
  onClose: () => void
  seed: Record<string, string>
}) {
  const [values, setValues] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const set = (k: string, v: string) => setValues((p) => ({ ...p, [k]: v }))
  // What the input shows: the user's edit if any, else the stored value, else empty. `field` doubles as the
  // effective value, so submit can send what is on screen rather than treating a preloaded field as blank.
  const field = (k: string) => values[k] ?? seed[k] ?? ''
  const blank = (k: string) => field(k) === ''

  const mutation = useMutation({
    mutationFn: async (body: Record<string, unknown>) => {
      const result = await apiRequest<VehicleSummary>(`/api/vehicles/${encodeURIComponent(reg)}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async () => {
      // Both, because the garage card projects the same summary and its MOT figure would otherwise sit stale
      // behind a back button.
      await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
      await queryClient.invalidateQueries({ queryKey: queryKeys.garage })
      toast('Saved · the countdowns recomputed')
      setValues({})
      setError(null)
      onClose()
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Could not save.'),
  })

  const submit = () => {
    // `field(k)` (edit or preloaded value), never raw `values[k]` — a preloaded field the user did not touch
    // must send its stored value, not undefined/NaN. A genuinely empty field sends null, which the PATCH
    // leaves unchanged (InsurancePatch is null=leave).
    if (which === 'motSeed') {
      mutation.mutate({ motExpirySeed: blank('motExpirySeed') ? null : field('motExpirySeed') })
    } else if (which === 'ved') {
      mutation.mutate({
        vedExpiry: blank('vedExpiry') ? null : field('vedExpiry'),
        vedAnnualCost: blank('vedAnnualCost') ? null : Number(field('vedAnnualCost')),
      })
    } else if (which === 'insurance') {
      mutation.mutate({
        insurance: {
          insurer: blank('insurer') ? null : field('insurer'),
          policyNumber: blank('policyNumber') ? null : field('policyNumber'),
          periodStart: blank('periodStart') ? null : field('periodStart'),
          periodEnd: blank('periodEnd') ? null : field('periodEnd'),
          coverType: blank('coverType') ? null : field('coverType'),
          premium: blank('premium') ? null : Number(field('premium')),
        },
      })
    }
  }

  return (
    <Sheet
      open={which !== null}
      onClose={onClose}
      title={which === 'ved' ? 'Road tax · VED' : which === 'motSeed' ? 'Seed the MOT expiry' : 'Insurance'}
      subtitle="drives the dashboard countdown"
      onSubmit={submit}
      footer={
        <Btn onClick={() => {}} type="submit">
          {mutation.isPending ? 'Saving…' : 'Save'}
        </Btn>
      }
    >
      {which === 'motSeed' && (
        <Field
          label="MOT expires"
          wide
          hint="A stand-in until the pass record is logged. The record always wins — this is never an override."
        >
          {(p) => <input type="date" value={field('motExpirySeed')} onChange={(e) => set('motExpirySeed', e.target.value)} {...p} />}
        </Field>
      )}

      {which === 'ved' && (
        <>
          <Field label="Expires" hint="the countdown runs to this date">
            {(p) => <input type="date" value={field('vedExpiry')} onChange={(e) => set('vedExpiry', e.target.value)} {...p} />}
          </Field>
          <Field label="Annual cost £">
            {(p) => <input type="text" inputMode="decimal" placeholder="430" value={field('vedAnnualCost')} onChange={(e) => set('vedAnnualCost', e.target.value)} {...p} />}
          </Field>
        </>
      )}

      {which === 'insurance' && (
        <>
          <Field label="Insurer">
            {(p) => <input type="text" placeholder="Admiral" value={field('insurer')} onChange={(e) => set('insurer', e.target.value)} {...p} />}
          </Field>
          <Field label="Policy number">
            {(p) => <input type="text" placeholder="P77904683" value={field('policyNumber')} onChange={(e) => set('policyNumber', e.target.value)} {...p} />}
          </Field>
          <Field label="Cover from">
            {(p) => <input type="date" value={field('periodStart')} onChange={(e) => set('periodStart', e.target.value)} {...p} />}
          </Field>
          <Field label="Cover to" hint="the countdown runs to this date">
            {(p) => <input type="date" value={field('periodEnd')} onChange={(e) => set('periodEnd', e.target.value)} {...p} />}
          </Field>
          <Field label="Cover type">
            {(p) => <input type="text" placeholder="Comprehensive" value={field('coverType')} onChange={(e) => set('coverType', e.target.value)} {...p} />}
          </Field>
          <Field label="Premium £/yr">
            {(p) => <input type="text" inputMode="decimal" placeholder="517.14" value={field('premium')} onChange={(e) => set('premium', e.target.value)} {...p} />}
          </Field>
        </>
      )}

      {error !== null && (
        <div className="field wide">
          <span className="hint" style={{ color: 'var(--due)' }} role="alert">
            {error}
          </span>
        </div>
      )}
    </Sheet>
  )
}
