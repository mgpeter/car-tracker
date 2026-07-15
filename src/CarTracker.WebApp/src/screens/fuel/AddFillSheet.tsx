import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { apiRequest } from '../../api/client'
import { ApiFailure, queryKeys } from '../../api/queries'
import { Btn } from '../../components/Btn'
import { Field, Sheet } from '../../components/Sheet'
import { useToast } from '../../shell/Toast'

interface AnomalyFlag {
  id: number
  kind: string
  severity: string
  message: string
  detail: string | null
}

interface AddFillResponse {
  id: number
  flags: AnomalyFlag[]
}

const LITRES_PER_GALLON = 4.54609

/** The domain's band, from FuelEconomyCalculator. Not the design's 18–45, which predates the fuel-basis spec. */
const MIN_PLAUSIBLE = 10
const MAX_PLAUSIBLE = 70

interface Props {
  open: boolean
  onClose: () => void
  reg: string
  lastMileage: number | null
  averageMpg: number | null
  today: string
}

/**
 * Add a fill — the daily loop's flagship write, and the sheet the design gets most wrong.
 *
 * Three things it does that this does not:
 *
 * 1. **It gates MPG on fill level**, printing "· ·" and "MPG withheld · partial fill — resumes at the next
 *    brimmed fill". The fuel-basis spec removed that rule. The litres on a partial fill are exactly as known
 *    as on any other — it is the same receipt — so the arithmetic is exactly as valid. Fill level is recorded
 *    and it is descriptive.
 * 2. **It hardcodes an 18–45 plausibility band.** The domain's is 10–70 and lives in one place; two bands
 *    means the preview can call a figure suspect that the server then accepts without comment.
 * 3. **Its Full/Partial toggle is not the domain's enum**, which is Full/Half/Quarter.
 *
 * The live MPG preview is worth keeping and is the reason the sheet is good: it computes as you type, so a
 * mistyped odometer shows up as an absurd figure before you save rather than in a chart next week. But the
 * preview is a courtesy — the server computes the real one, and this must agree with it or it is worse than
 * nothing.
 */
export function AddFillSheet({ open, onClose, reg, lastMileage, averageMpg, today }: Props) {
  const [v, setV] = useState<Record<string, string>>({})
  const [totalTouched, setTotalTouched] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const get = (k: string) => v[k] ?? ''
  const num = (k: string) => {
    const n = Number(get(k).replace(/[\s,]/g, ''))
    return Number.isFinite(n) && get(k) !== '' ? n : null
  }

  const set = (k: string, value: string) => {
    setV((p) => {
      const next = { ...p, [k]: value }
      if (k === 'totalCost') return next
      // The design's convenience, kept: total = litres × price until the user types a total, after which it is
      // theirs. Receipts round, so the product is a starting point and never an override.
      if ((k === 'litres' || k === 'pricePerLitre') && !totalTouched) {
        const l = Number(next['litres'])
        const p2 = Number(next['pricePerLitre'])
        next['totalCost'] = l > 0 && p2 > 0 ? (l * p2).toFixed(2) : ''
      }
      return next
    })
  }

  const mileage = num('mileage')
  const litres = num('litres')
  const miles = mileage !== null && lastMileage !== null ? mileage - lastMileage : null
  const preview =
    miles !== null && miles > 0 && litres !== null && litres > 0
      ? (miles * LITRES_PER_GALLON) / litres
      : null
  const implausible = preview !== null && (preview < MIN_PLAUSIBLE || preview > MAX_PLAUSIBLE)

  const mutation = useMutation({
    mutationFn: async () => {
      const result = await apiRequest<AddFillResponse>(`/api/vehicles/${encodeURIComponent(reg)}/fuel`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          entryDate: get('entryDate') || today,
          mileage,
          litres,
          pricePerLitre: num('pricePerLitre'),
          totalCost: num('totalCost'),
          station: get('station') || null,
          fillLevel: get('fillLevel') || null,
          notes: get('notes') || null,
        }),
      })
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async (res) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'fuel'] })
      // The mileage reading and the mirrored expense are written in the same transaction, so their screens are
      // stale the moment this returns.
      await queryClient.invalidateQueries({ queryKey: queryKeys.garage })

      // A flag never blocks the save (§5.3) — but saying only "Fill saved" when the detectors raised something
      // is the app quietly accepting what it does not believe.
      toast(
        res.flags.length > 0
          ? `Fill saved · ${res.flags.length === 1 ? res.flags[0]!.message : `${res.flags.length} flags raised`} · recorded, not silently accepted`
          : 'Fill saved · odometer, MPG and the expense mirror recomputed',
      )
      setV({})
      setTotalTouched(false)
      setError(null)
      onClose()
    },
    onError: (e) => setError(e instanceof Error ? e.message : 'Could not save.'),
  })

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title="Add fill"
      subtitle="mirrors into expenses automatically"
      onSubmit={() => mutation.mutate()}
      footer={
        // The Sheet's onSubmit does the work; the button only needs to submit the form it is in.
        <Btn type="submit" onClick={() => {}}>
          {mutation.isPending ? 'Saving…' : 'Save fill'}
        </Btn>
      }
    >
      <Field label="Date">
        {(p) => <input type="date" value={get('entryDate') || today} onChange={(e) => set('entryDate', e.target.value)} {...p} />}
      </Field>

      <Field
        label="Odometer"
        hint={
          lastMileage === null
            ? 'no previous reading — this fill has no interval to measure'
            : miles !== null && miles > 0
              ? `${miles.toLocaleString('en-GB')} mi since the last fill`
              : `last fill at ${lastMileage.toLocaleString('en-GB')} mi`
        }
      >
        {(p) => <input type="text" inputMode="numeric" placeholder="80,712" value={get('mileage')} onChange={(e) => set('mileage', e.target.value)} {...p} />}
      </Field>

      <Field label="Litres" hint="from the receipt — MPG rests on this alone">
        {(p) => <input type="text" inputMode="decimal" placeholder="47.03" value={get('litres')} onChange={(e) => set('litres', e.target.value)} {...p} />}
      </Field>

      <Field label="£ per litre">
        {(p) => <input type="text" inputMode="decimal" placeholder="1.799" value={get('pricePerLitre')} onChange={(e) => set('pricePerLitre', e.target.value)} {...p} />}
      </Field>

      <Field label="Total £" hint="filled from litres × price until you change it">
        {(p) => (
          <input
            type="text"
            inputMode="decimal"
            placeholder="84.61"
            value={get('totalCost')}
            onChange={(e) => {
              setTotalTouched(true)
              set('totalCost', e.target.value)
            }}
            {...p}
          />
        )}
      </Field>

      <Field label="Station">
        {(p) => <input type="text" placeholder="Shell Kingston" value={get('station')} onChange={(e) => set('station', e.target.value)} {...p} />}
      </Field>

      <Field label="Fill level" hint="descriptive — it does not affect MPG or whether one is shown">
        {(p) => (
          <select value={get('fillLevel')} onChange={(e) => set('fillLevel', e.target.value)} {...p}>
            <option value="">Not recorded</option>
            <option value="Full">Full</option>
            <option value="Half">Half</option>
            <option value="Quarter">Quarter</option>
          </select>
        )}
      </Field>

      <Field label="Notes" wide>
        {(p) => <input type="text" placeholder="V-Power · premium price" value={get('notes')} onChange={(e) => set('notes', e.target.value)} {...p} />}
      </Field>

      {/* The live preview. Computed from litres alone, like the server's — never gated on fill level. */}
      <div className="field wide">
        <div className={`mpgprev${implausible ? ' warn' : ''}`}>
          <span className="k">{preview === null ? 'MPG · computed live' : implausible ? 'MPG · outside the plausible band' : 'MPG · this tank'}</span>
          <span className="v num">{preview === null ? '—' : preview.toFixed(1)}</span>
          <span className="n">
            {preview === null ? (
              lastMileage === null
                ? 'the first fill has no previous reading to measure from'
                : 'enter an odometer reading above the last fill, and litres'
            ) : implausible ? (
              <>
                outside {MIN_PLAUSIBLE}–{MAX_PLAUSIBLE} MPG — a missed fill or a mistyped odometer. It saves
                either way and is flagged, not refused.
              </>
            ) : averageMpg !== null ? (
              `${miles?.toLocaleString('en-GB')} mi · ${preview - averageMpg >= 0 ? '+' : ''}${(preview - averageMpg).toFixed(1)} vs ${averageMpg.toFixed(1)} average`
            ) : (
              `${miles?.toLocaleString('en-GB')} mi · no average yet to compare against`
            )}
          </span>
        </div>
      </div>

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
