import { useState } from 'react'
import { useReferenceSuggestions } from '../../api/reference'
import { BLANK_CLEARS, useVehiclePatch, type VehicleField } from '../../api/vehicle'
import type { VehicleDetail, VehicleSummary } from '../../api/client'
import { Btn } from '../../components/Btn'
import { Combobox } from '../../components/Combobox'
import { Field, Select, Sheet } from '../../components/Sheet'
import { fieldError, formError, reportApiError, type FieldErrors } from '../../lib/formErrors'
import { useToast } from '../../shell/Toast'

/**
 * Every editor on the vehicle screen, as data.
 *
 * There are nine, and they differ only in which fields they collect, how each coerces, and what the sheet is
 * called. Everything else - read the stored value, send a partial block, invalidate, toast, close - is
 * identical, so it is written once. Nine sheet components would be nine chances to get the last part wrong,
 * and the two that existed before this file **already disagreed**: the fuel-tank editor invalidated the
 * vehicle detail and the statutory editor did not, which on a merged page means editing the insurer and
 * watching the rows below it keep the old values.
 */
export type EditorId =
  | 'motSeed'
  | 'ved'
  | 'insurance'
  | 'breakdown'
  | 'fluids'
  | 'tyres'
  | 'identity'
  | 'purchase'
  | 'notes'

type FieldKind = 'text' | 'number' | 'int' | 'date' | 'bool' | 'garage' | 'longtext'

interface FieldSpec {
  key: string
  label: string
  kind: FieldKind
  /** The contract member a blank sends in `clear`, when the field had a stored value. */
  clears: VehicleField
  hint?: string
  placeholder?: string
  wide?: boolean
}

interface EditorSpec {
  title: string
  subtitle: string
  /**
   * The patch block these fields nest under. Omitted means they sit at the root of the patch - which is
   * exactly the distinction that put Notes in the wrong section of this screen for a year: it is a root
   * field, sibling to `insurance`, not a member of it.
   */
  block?: 'insurance' | 'fluids' | 'tyres' | 'breakdown'
  fields: FieldSpec[]
  /**
   * Where the stored values come from.
   *
   * Not derivable from `block` alone, because two of the statutory dates are not on the vehicle detail at all:
   * `vedExpiry` and the MOT seed come back on the derived *summary*'s renewals, which is why this screen runs
   * both queries.
   */
  seed: (detail: VehicleDetail, summary: VehicleSummary | undefined) => Record<string, unknown>
}

const str = (v: unknown) => (v === null || v === undefined ? '' : String(v))

/** Everything a `<Field>` needs, keyed by editor. Adding a field is one line. */
export const EDITORS: Record<EditorId, EditorSpec> = {
  motSeed: {
    title: 'Seed the MOT expiry',
    subtitle: 'used only until an MOT record exists',
    fields: [
      {
        key: 'motExpirySeed', clears: 'MotExpirySeed',
        label: 'MOT expires',
        kind: 'date',
        wide: true,
        hint: 'A stand-in until the pass record is logged. The record always wins - this is never an override.',
      },
    ],
    seed: (_d, s) => ({ motExpirySeed: s?.renewals.mot.expiryDate }),
  },

  ved: {
    title: 'Road tax · VED',
    subtitle: 'drives the dashboard countdown',
    fields: [
      { key: 'vedExpiry', clears: 'VedExpiry', label: 'Expires', kind: 'date', hint: 'the countdown runs to this date' },
      { key: 'vedAnnualCost', clears: 'VedAnnualCost', label: 'Annual cost £', kind: 'number', placeholder: '430' },
    ],
    seed: (d, s) => ({ vedExpiry: s?.renewals.roadTax.expiryDate, vedAnnualCost: d.vedAnnualCost }),
  },

  insurance: {
    title: 'Insurance',
    subtitle: 'drives the dashboard countdown',
    block: 'insurance',
    fields: [
      { key: 'insurer', clears: 'InsuranceInsurer', label: 'Insurer', kind: 'text', placeholder: 'Admiral' },
      { key: 'policyNumber', clears: 'InsurancePolicyNumber', label: 'Policy number', kind: 'text', placeholder: 'P77904683' },
      { key: 'periodStart', clears: 'InsurancePeriodStart', label: 'Cover from', kind: 'date' },
      { key: 'periodEnd', clears: 'InsurancePeriodEnd', label: 'Cover to', kind: 'date', hint: 'the countdown runs to this date' },
      { key: 'coverType', clears: 'InsuranceCoverType', label: 'Cover type', kind: 'text', placeholder: 'Comprehensive' },
      { key: 'premium', clears: 'InsurancePremium', label: 'Premium £/yr', kind: 'number', placeholder: '517.14' },
      { key: 'excessCompulsory', clears: 'InsuranceExcessCompulsory', label: 'Excess · compulsory £', kind: 'number', placeholder: '250' },
      { key: 'excessVoluntary', clears: 'InsuranceExcessVoluntary', label: 'Excess · voluntary £', kind: 'number', placeholder: '250' },
      { key: 'ncbYears', clears: 'InsuranceNcbYears', label: 'No-claims years', kind: 'int', placeholder: '9' },
    ],
    seed: (d) => ({ ...d.insurance }),
  },

  breakdown: {
    title: 'Breakdown cover',
    subtitle: 'stored, because nothing logs a recovery callout',
    block: 'breakdown',
    fields: [
      { key: 'provider', clears: 'BreakdownProvider', label: 'Provider', kind: 'text', placeholder: 'Green Flag' },
      { key: 'policyNumber', clears: 'BreakdownPolicyNumber', label: 'Policy number', kind: 'text' },
      {
        key: 'expiry', clears: 'BreakdownExpiry',
        label: 'Expires',
        kind: 'date',
        hint: 'reference only - breakdown cover drives no countdown on the dashboard',
      },
    ],
    seed: (d) => ({ ...d.breakdown }),
  },

  fluids: {
    title: 'Fluids & parts',
    subtitle: 'what the manual says goes in',
    block: 'fluids',
    fields: [
      { key: 'oilSpec', clears: 'FluidsOilSpec', label: 'Engine oil', kind: 'text', placeholder: '5W-30 A3/B4' },
      { key: 'oilCapacityLitres', clears: 'FluidsOilCapacityLitres', label: 'Oil capacity L', kind: 'number', placeholder: '4.5' },
      {
        key: 'coolantSpec', clears: 'FluidsCoolantSpec',
        label: 'Coolant',
        kind: 'text',
        placeholder: 'OAT (red/pink)',
        hint: 'OAT only, never mixed with IAT',
      },
      { key: 'coolantCapacityLitres', clears: 'FluidsCoolantCapacityLitres', label: 'Coolant capacity L', kind: 'number', placeholder: '7' },
      {
        key: 'fuelTankCapacityLitres', clears: 'FluidsFuelTankCapacityLitres',
        label: 'Fuel tank L',
        kind: 'number',
        placeholder: '59',
        hint: 'the dashboard full-tank range derives from this',
      },
      { key: 'brakeFluidSpec', clears: 'FluidsBrakeFluidSpec', label: 'Brake fluid', kind: 'text', placeholder: 'DOT 4' },
      { key: 'transmissionOilSpec', clears: 'FluidsTransmissionOilSpec', label: 'Transmission oil', kind: 'text' },
      { key: 'sparkPlugPart', clears: 'FluidsSparkPlugPart', label: 'Spark plugs', kind: 'text' },
      { key: 'oilFilterPart', clears: 'FluidsOilFilterPart', label: 'Oil filter', kind: 'text', placeholder: 'W712/75' },
      { key: 'airFilterPart', clears: 'FluidsAirFilterPart', label: 'Air filter', kind: 'text' },
      { key: 'fuelFilterPart', clears: 'FluidsFuelFilterPart', label: 'Fuel filter', kind: 'text' },
      { key: 'cabinFilterPart', clears: 'FluidsCabinFilterPart', label: 'Cabin filter', kind: 'text' },
    ],
    seed: (d) => ({ ...d.fluids }),
  },

  tyres: {
    title: 'Tyre specs',
    subtitle: 'the targets, not a reading - readings live in the tyre log',
    block: 'tyres',
    fields: [
      { key: 'tyreSize', clears: 'TyresTyreSize', label: 'Size', kind: 'text', placeholder: '215/65 R16' },
      { key: 'pressureFrontPsi', clears: 'TyresPressureFrontPsi', label: 'Front psi', kind: 'number', placeholder: '30' },
      { key: 'pressureRearPsi', clears: 'TyresPressureRearPsi', label: 'Rear psi', kind: 'number', placeholder: '33' },
      { key: 'pressureFrontLadenPsi', clears: 'TyresPressureFrontLadenPsi', label: 'Front psi · laden', kind: 'number' },
      { key: 'pressureRearLadenPsi', clears: 'TyresPressureRearLadenPsi', label: 'Rear psi · laden', kind: 'number' },
      { key: 'minTreadMm', clears: 'TyresMinTreadMm', label: 'Minimum tread mm', kind: 'number', placeholder: '1.6', hint: 'MOT limit is 1.6 mm' },
    ],
    seed: (d) => ({ ...d.tyres }),
  },

  identity: {
    title: 'Identity',
    subtitle: 'the facts a logbook carries',
    fields: [
      { key: 'colour', clears: 'Colour', label: 'Colour', kind: 'text', placeholder: 'Epsom Green' },
      { key: 'bodyStyle', clears: 'BodyStyle', label: 'Body style', kind: 'text', placeholder: '5-door SUV' },
      { key: 'vin', clears: 'Vin', label: 'VIN', kind: 'text', wide: true },
      { key: 'ulezCompliant', clears: 'UlezCompliant', label: 'ULEZ', kind: 'bool' },
    ],
    // Make, model, year, engine, transmission and drivetrain are not here because the API does not accept
    // them: they are what the car *is*, fixed at create. The rows still render, without a control.
    seed: (d) => ({
      colour: d.colour,
      bodyStyle: d.bodyStyle,
      vin: d.vin,
      // The select's own vocabulary, not `String(true)` - `coerce` reads it back the same way.
      ulezCompliant: d.ulezCompliant === null || d.ulezCompliant === undefined ? null : d.ulezCompliant ? 'yes' : 'no',
    }),
  },

  purchase: {
    title: 'Purchase',
    subtitle: 'the price is load-bearing - it mirrors into an expense',
    fields: [
      {
        key: 'purchasePrice', clears: 'PurchasePrice',
        label: 'Price £',
        kind: 'number',
        placeholder: '1750',
        hint: 'moves total outlay and cost per mile - it is mirrored as a Purchase expense',
      },
      { key: 'seller', clears: 'Seller', label: 'Seller', kind: 'text' },
      { key: 'defaultGarage', clears: 'DefaultGarage', label: 'Default garage', kind: 'garage', placeholder: 'K & P Motors' },
    ],
    // Purchase date and odometer-at-purchase are absent deliberately: they are the vehicle's founding facts,
    // and the odometer one also seeded a MileageReading that every mile-since figure is measured from.
    seed: (d) => ({ purchasePrice: d.purchasePrice, seller: d.seller, defaultGarage: d.defaultGarage }),
  },

  notes: {
    title: 'Notes',
    subtitle: 'about the car as a whole',
    fields: [
      {
        key: 'notes', clears: 'Notes',
        label: 'Notes',
        kind: 'longtext',
        wide: true,
        hint: 'anything about this car that is not one of the fields above',
      },
    ],
    seed: (d) => ({ notes: d.notes }),
  },
}

/**
 * The value a field sends, or null for a blank. `submit` decides what a null means: a field that had a stored
 * value goes in `clear`, one that was already empty is left out. A null is never sent, because the server reads
 * it as "unchanged".
 */
function coerce(kind: FieldKind, raw: string): unknown {
  const v = raw.trim()
  if (v === '') return null
  switch (kind) {
    case 'number':
      return Number(v.replace(/[\s,£]/g, ''))
    case 'int':
      return Math.trunc(Number(v))
    case 'bool':
      return v === 'yes'
    default:
      return v
  }
}

export function VehicleEditSheet({
  reg,
  which,
  detail,
  summary,
  onClose,
}: {
  reg: string
  which: EditorId | null
  detail: VehicleDetail
  summary: VehicleSummary | undefined
  onClose: () => void
}) {
  const [values, setValues] = useState<Record<string, string>>({})
  const [errors, setErrors] = useState<FieldErrors>({})
  const { toast } = useToast()
  const garages = useReferenceSuggestions('garages')
  const mutation = useVehiclePatch(reg)

  const spec = which === null ? null : EDITORS[which]
  const stored = spec === null ? {} : spec.seed(detail, summary)

  // What the input shows: the edit if there is one, else the stored value, else empty. `field` doubles as the
  // effective value, so a preloaded field the user never touched sends its stored value rather than a blank.
  const field = (k: string) => values[k] ?? str(stored[k])
  const set = (k: string, v: string) => setValues((p) => ({ ...p, [k]: v }))

  const close = () => {
    setValues({})
    setErrors({})
    onClose()
  }

  const submit = () => {
    if (spec === null) return
    const body: Record<string, unknown> = {}
    const clear: VehicleField[] = []
    for (const f of spec.fields) {
      const value = coerce(f.kind, field(f.key))
      if (value !== null) body[f.key] = value
      // Blank now and something stored before: the owner removed it. Blank and nothing stored is no change.
      else if (str(stored[f.key]).trim() !== '') clear.push(f.clears)
    }
    // A block with nothing left in it is left out rather than sent as `{}`.
    const patch: Record<string, unknown> =
      spec.block === undefined ? body : Object.keys(body).length > 0 ? { [spec.block]: body } : {}
    if (clear.length > 0) patch['clear'] = clear
    mutation.mutate(patch, {
      onSuccess: () => {
        toast(`Saved · ${spec.title.toLowerCase()}`)
        close()
      },
      // The server's keys here are a mix of flat (`coverType`) and dotted (`Insurance.PeriodEnd`); anything
      // that does not match a field on this sheet folds into the footer banner rather than being dropped.
      onError: (e) => setErrors(reportApiError(e, spec.fields.map((f) => f.key))),
    })
  }

  return (
    <Sheet
      open={spec !== null}
      onClose={close}
      title={spec?.title ?? ''}
      subtitle={spec?.subtitle ?? ''}
      onSubmit={submit}
      footer={
        <Btn onClick={() => {}} type="submit">
          {mutation.isPending ? 'Saving…' : 'Save'}
        </Btn>
      }
    >
      {spec?.fields.map((f) => (
        <Field
          key={f.key}
          label={f.label}
          {...(f.wide === true && { wide: true })}
          hint={f.hint ?? BLANK_CLEARS}
          error={fieldError(errors, f.key)}
        >
          {(p) => renderInput(f, field(f.key), (v) => set(f.key, v), p, garages)}
        </Field>
      ))}

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

/** Exactly what `<Field>`'s render prop hands over - `aria-invalid` is `true`-or-absent, never `false`. */
type FieldProps = { id: string; 'aria-describedby'?: string; 'aria-invalid'?: true }

function renderInput(
  f: FieldSpec,
  value: string,
  onChange: (v: string) => void,
  p: FieldProps,
  garages: { value: string; hint?: string }[],
) {
  if (f.kind === 'garage') {
    // Free text here silently creates a reference row (VehicleUpdateService calls EnsureGarageAsync on an
    // unseen name), so offering the existing ones first is not just a convenience.
    return (
      <Combobox
        {...p}
        value={value}
        onChange={onChange}
        suggestions={garages}
        {...(f.placeholder !== undefined && { placeholder: f.placeholder })}
      />
    )
  }

  if (f.kind === 'bool') {
    return (
      <Select {...p} value={value} onChange={(e) => onChange(e.target.value)}>
        <option value="">not recorded</option>
        <option value="yes">Compliant</option>
        <option value="no">Not compliant</option>
      </Select>
    )
  }

  if (f.kind === 'longtext') {
    return <textarea {...p} rows={5} value={value} onChange={(e) => onChange(e.target.value)} />
  }

  return (
    <input
      {...p}
      type={f.kind === 'date' ? 'date' : 'text'}
      {...(f.kind === 'number' || f.kind === 'int' ? { inputMode: 'decimal' as const } : {})}
      {...(f.placeholder !== undefined && { placeholder: f.placeholder })}
      value={value}
      onChange={(e) => onChange(e.target.value)}
    />
  )
}
