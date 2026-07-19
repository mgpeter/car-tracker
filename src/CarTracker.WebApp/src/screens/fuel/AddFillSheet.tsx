import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import type { VehicleSummary } from '../../api/client'
import { apiRequest } from '../../api/client'
import { ApiFailure, queryKeys } from '../../api/queries'
import { Btn } from '../../components/Btn'
import { Combobox } from '../../components/Combobox'
import { ConfirmButton } from '../../components/ConfirmButton'
import { Field, Sheet } from '../../components/Sheet'
import { fieldError, formError, reportApiError, type FieldErrors } from '../../lib/formErrors'
import { useToast } from '../../shell/Toast'

type Entry = VehicleSummary['fuel']['entries'][number]

interface AnomalyFlag {
  id: number
  kind: string
  severity: string
  message: string
  detail: string | null
}

interface FillResponse {
  id: number
  flags: AnomalyFlag[]
}

const LITRES_PER_GALLON = 4.54609

/** The domain's band, from FuelEconomyCalculator. Not the design's 18–45, which predates the fuel-basis spec. */
const MIN_PLAUSIBLE = 10
const MAX_PLAUSIBLE = 70

interface Props {
  /** `'new'` opens a blank add; an entry opens it seeded for edit; `null` is closed. */
  editing: Entry | 'new' | null
  onClose: () => void
  reg: string
  lastMileage: number | null
  averageMpg: number | null
  today: string
  /**
   * Distinct recent stations, newest-first — offered as combobox choices, never a constraint. Optional so the
   * dashboard's quick-add can open the same sheet without wiring a suggestion source; the fuel log passes them.
   */
  stationSuggestions?: { value: string }[]
}

/**
 * Add or edit a fill — the daily loop's flagship write, and the sheet the design gets most wrong.
 *
 * The add half keeps the live MPG preview (a courtesy that catches a mistyped odometer before it reaches a
 * chart) and rejects three of the design's rules: MPG is not gated on fill level, the plausibility band is the
 * domain's 10–70 not a hardcoded 18–45, and the fill-level enum is Full/Half/Quarter.
 *
 * The edit half is why this spec exists: a mistyped fill was permanent, moving the odometer and the MPG average
 * forever, because a fill could be deleted but not corrected. Now it opens seeded, and saving re-derives MPG
 * and drags the mirrored expense and mileage reading along server-side. The footer Delete removes the fill and
 * both its shadows, behind a two-step confirm that names the cascade.
 */
export function AddFillSheet({ editing, onClose, reg, lastMileage, averageMpg, today, stationSuggestions = [] }: Props) {
  const existing = editing !== 'new' && editing !== null ? editing : null
  const [v, setV] = useState<Record<string, string>>({})
  const [totalTouched, setTotalTouched] = useState(false)
  const [errors, setErrors] = useState<FieldErrors>({})
  const queryClient = useQueryClient()
  const { toast } = useToast()

  // Seed the form the first time a given fill (or the blank add) opens it.
  const [seededFor, setSeededFor] = useState<number | 'new' | null>(null)
  const key = existing?.fuelEntryId ?? (editing === 'new' ? ('new' as const) : null)
  if (key !== null && key !== seededFor) {
    setSeededFor(key)
    setV(
      existing === null
        ? // Most fills are to the brim, so the blank sheet asserts the normal case. A Full fill closes the tank
          // and measures MPG; the driver flips it to Half/Quarter only on the rare partial.
          { fillLevel: 'Full' }
        : {
            entryDate: existing.entryDate,
            mileage: String(existing.mileage),
            litres: existing.litres.toFixed(2),
            pricePerLitre: existing.pricePerLitre.toFixed(3),
            totalCost: existing.totalCost.toFixed(2),
            station: existing.station ?? '',
            fillLevel: existing.fillLevel ?? '',
            notes: existing.notes ?? '',
          },
    )
    // On an existing fill the total is the receipt as saved — do not overwrite it from litres × price.
    setTotalTouched(existing !== null)
    setErrors({})
  }

  // The fields the server can flag on a fill — anything else it returns falls to the footer banner.
  const FIELD_KEYS = ['litres', 'pricePerLitre', 'mileage', 'totalCost'] as const

  const get = (k: string) => v[k] ?? ''
  const num = (k: string) => {
    const n = Number(get(k).replace(/[\s,]/g, ''))
    return Number.isFinite(n) && get(k) !== '' ? n : null
  }

  const set = (k: string, value: string) => {
    setV((p) => {
      const next = { ...p, [k]: value }
      if (k === 'totalCost') return next
      // total = litres × price until the user types a total, after which it is theirs. Receipts round, so the
      // product is a starting point and never an override.
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
  // A partial fill defers its MPG to the next fill to full, so the preview must too — otherwise the sheet shows
  // a number the server will not compute. Only "closes vs not" matters: Half and Quarter both defer.
  const defersMpg = get('fillLevel') === 'Half' || get('fillLevel') === 'Quarter'
  const preview =
    !defersMpg && miles !== null && miles > 0 && litres !== null && litres > 0
      ? (miles * LITRES_PER_GALLON) / litres
      : null
  const implausible = preview !== null && (preview < MIN_PLAUSIBLE || preview > MAX_PLAUSIBLE)

  // Checked here so the answer is instant and beside the field; the server validates independently. Mirrors the
  // fuel endpoint's own rules (litres, price and odometer must be positive).
  const validate = (): FieldErrors => {
    const e: FieldErrors = {}
    if (litres === null || litres <= 0) e['litres'] = ['A fill must have litres — MPG rests on this.']
    const price = num('pricePerLitre')
    if (price === null || price <= 0) e['priceperlitre'] = ['What did it cost per litre?']
    if (mileage === null || mileage <= 0) e['mileage'] = ['The odometer reading at this fill.']
    return e
  }

  const submit = () => {
    const found = validate()
    setErrors(found)
    if (Object.keys(found).length === 0) mutation.mutate()
  }

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
    await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'fuel'] })
    // The mirrored expense and the odometer reading move in the same transaction, so their screens are stale.
    await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'expenses'] })
    await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'mileage'] })
    await queryClient.invalidateQueries({ queryKey: queryKeys.garage })
  }

  const mutation = useMutation({
    mutationFn: async () => {
      const body = {
        entryDate: get('entryDate') || today,
        mileage,
        litres,
        pricePerLitre: num('pricePerLitre'),
        totalCost: num('totalCost'),
        station: get('station') || null,
        fillLevel: get('fillLevel') || null,
        notes: get('notes') || null,
      }
      const result = await apiRequest<FillResponse>(
        existing === null
          ? `/api/vehicles/${encodeURIComponent(reg)}/fuel`
          : `/api/vehicles/${encodeURIComponent(reg)}/fuel/${existing.fuelEntryId}`,
        {
          method: existing === null ? 'POST' : 'PATCH',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(body),
        },
      )
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async (res) => {
      await invalidate()
      // A flag never blocks the save (§5.3) — but saying only "saved" when the detectors raised something is
      // the app quietly accepting what it does not believe.
      const raised =
        res.flags.length > 0
          ? ` · ${res.flags.length === 1 ? res.flags[0]!.message : `${res.flags.length} flags raised`}`
          : ''
      toast(
        existing === null
          ? `Fill saved · odometer, MPG and the expense mirror recomputed${raised}`
          : `Fill updated · MPG and the mirrored expense recomputed${raised}`,
      )
      setV({})
      setSeededFor(null)
      setTotalTouched(false)
      setErrors({})
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  const remove = useMutation({
    mutationFn: async () => {
      if (existing === null) return
      const result = await apiRequest<null>(
        `/api/vehicles/${encodeURIComponent(reg)}/fuel/${existing.fuelEntryId}`,
        { method: 'DELETE' },
      )
      if (!result.ok) throw new ApiFailure(result.error)
    },
    onSuccess: async () => {
      await invalidate()
      toast('Fill deleted · its mileage reading and mirrored expense went with it')
      setV({})
      setSeededFor(null)
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  return (
    <Sheet
      open={editing !== null}
      onClose={onClose}
      title={existing === null ? 'Add fill' : 'Edit fill'}
      subtitle="mirrors into expenses automatically"
      onSubmit={submit}
      footer={
        <>
          {existing !== null && (
            <ConfirmButton
              onConfirm={() => remove.mutate()}
              pending={remove.isPending}
              cascade="with its expense & reading"
            />
          )}
          <Btn type="submit" onClick={() => {}}>
            {mutation.isPending ? 'Saving…' : existing === null ? 'Save fill' : 'Save changes'}
          </Btn>
        </>
      }
    >
      <Field label="Date">
        {(p) => <input type="date" value={get('entryDate') || today} onChange={(e) => set('entryDate', e.target.value)} {...p} />}
      </Field>

      <Field
        label="Odometer"
        error={fieldError(errors, 'mileage')}
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

      <Field label="Litres" error={fieldError(errors, 'litres')} hint="from the receipt — MPG rests on this alone">
        {(p) => <input type="text" inputMode="decimal" placeholder="47.03" value={get('litres')} onChange={(e) => set('litres', e.target.value)} {...p} />}
      </Field>

      <Field label="£ per litre" error={fieldError(errors, 'pricePerLitre')}>
        {(p) => <input type="text" inputMode="decimal" placeholder="1.799" value={get('pricePerLitre')} onChange={(e) => set('pricePerLitre', e.target.value)} {...p} />}
      </Field>

      <Field label="Total £" error={fieldError(errors, 'totalCost')} hint="filled from litres × price until you change it">
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
        {(p) => (
          <Combobox
            {...p}
            value={get('station')}
            onChange={(val) => set('station', val)}
            suggestions={stationSuggestions}
            placeholder="Shell Kingston"
          />
        )}
      </Field>

      <Field
        label="Fill level"
        hint="Full closes the tank and measures MPG; Half/Quarter defers it to your next full fill"
      >
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

      {/* The live preview. Computed from litres alone, like the server's — and, like the server, deferred when
          this fill is a partial, because a partial measures nothing until the next fill to full. */}
      <div className="field wide">
        <div className={`mpgprev${implausible ? ' warn' : ''}`}>
          <span className="k">
            {defersMpg
              ? 'MPG · pending'
              : preview === null
                ? 'MPG · computed live'
                : implausible
                  ? 'MPG · outside the plausible band'
                  : 'MPG · this tank'}
          </span>
          <span className="v num">{defersMpg || preview === null ? '—' : preview.toFixed(1)}</span>
          <span className="n">
            {defersMpg ? (
              'a partial fill defers its MPG to your next fill to full, where its litres are counted'
            ) : preview === null ? (
              lastMileage === null ? (
                'the first fill has no previous reading to measure from'
              ) : (
                'enter an odometer reading above the last fill, and litres'
              )
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
