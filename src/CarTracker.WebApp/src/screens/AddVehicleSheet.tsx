import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys, useGarage, useMeta } from '../api/queries'
import { useStarterChecks, useVehicleChecks } from '../api/reference'
import { Btn } from '../components/Btn'
import { CheckSelectList, type SelectableCheck } from '../components/CheckSelectList'
import { Field, focusFirstInvalidField, Select, Sheet } from '../components/Sheet'
import { fieldError, formError, reportApiError, type FieldErrors } from '../lib/formErrors'
import { hrefFor } from '../lib/link'
import { useToast } from '../shell/Toast'

/** Mirrors the API's CheckSource. The wire names, so this stays an identity mapping. */
type CheckSource = 'None' | 'GenericStarterSet' | 'CopyFromVehicle'

interface Draft {
  registration: string
  make: string
  model: string
  variant: string
  year: string
  colour: string
  purchaseDate: string
  purchaseMileage: string
  purchasePrice: string
  /** The wire enum exactly — see the select's own note on why this list is not a free invention. */
  fuelType: 'Petrol' | 'Diesel' | 'Hybrid' | 'Electric' | 'LPG'
  checkSource: CheckSource
}

/**
 * What a registration lookup found and the form does not show as an editable field.
 *
 * `motExpiry` is a **seed**, carried into the create as `MotExpirySeed` and read only while the car has no MOT
 * record — the first logged pass supersedes it (DEC-015). It is deliberately not a form field: a settable MOT
 * expiry is the first of the five defects this project exists to fix.
 */
interface LookedUp {
  engineSizeCc: number | null
  motExpiry: string | null
  vedExpiry: string | null
  taxStatus: string | null
  motStatus: string | null
}

/**
 * A number, or null when the box is empty or holds something that is not one.
 *
 * The empty case is the point. `Number('')` is **0**, and this form used to send that: leaving "Mileage at
 * purchase" blank passed validation (`Number.isInteger(0) && 0 >= 0`) and created the car with a founding
 * odometer reading of zero — the number every mile since purchase is measured from, wrong from the first
 * second, with nothing on any screen to say so. Separators are stripped the way `AddFillSheet` strips them,
 * because an odometer is read off a dial and typed as "76,632" at least as often as "76632".
 */
function num(raw: string): number | null {
  const cleaned = raw.replace(/[\s,£]/g, '')
  if (cleaned === '') return null
  const parsed = Number(cleaned)
  return Number.isFinite(parsed) ? parsed : null
}

/** The fields this sheet renders an `error=` for. Anything else the server flags folds to the footer banner. */
const FIELD_KEYS = ['registration', 'make', 'model', 'year', 'purchaseDate', 'purchaseMileage', 'purchasePrice'] as const

const EMPTY: Draft = {
  registration: '',
  make: '',
  model: '',
  variant: '',
  year: '',
  colour: '',
  purchaseDate: '',
  purchaseMileage: '',
  purchasePrice: '',
  fuelType: 'Petrol',
  checkSource: 'GenericStarterSet',
}

/**
 * Add a vehicle.
 *
 * **The design's DVLA lookup leads this sheet, and only when there is one behind it.** Its plate input, "Look
 * up" button and the promise "Fetches make, model, year, colour, engine, MOT and tax status from the DVLA —
 * you confirm before anything is created" are all here (DEC-015) — but the credentials are absent on a fresh
 * checkout, on CI and on any deployment nobody has provisioned a VES key for, and there the endpoint answers
 * 503 `NotConfigured` whatever the plate. So the button renders only when `meta.vehicleLookupConfigured` says
 * it can work. A button that looks like the fast path and cannot take it is the settings drag-grips that do
 * not drag, and worse here because this is the first thing anyone does. The registration is still styled as a
 * plate, because it is a plate.
 */
export function AddVehicleSheet({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [draft, setDraft] = useState<Draft>(EMPTY)
  const [errors, setErrors] = useState<FieldErrors>({})
  // Which checks the owner has turned OFF. Tracking deselections (not selections) makes "all on" the default
  // with no dependence on when the list finishes loading, and lets the untouched case send nothing.
  const [deselected, setDeselected] = useState<Set<string>>(new Set())
  const [copyFromId, setCopyFromId] = useState<number | null>(null)
  const [looked, setLooked] = useState<LookedUp | null>(null)
  const [lookupError, setLookupError] = useState<string | null>(null)
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const { toast } = useToast()

  const isGeneric = draft.checkSource === 'GenericStarterSet'
  const isCopy = draft.checkSource === 'CopyFromVehicle'

  // Does this deployment hold a DVLA credential? Strictly `=== true`, so an in-flight `meta` hides the button
  // rather than offering one that might 503 on the first click. That is the opposite of the danger zone's
  // reading of the same flag, and deliberately: there, assuming "not configured" would *state* something false
  // about the deployment; here the unknown state says nothing at all and the form below is unchanged either way.
  const { data: meta } = useMeta()
  const canLookUp = meta?.vehicleLookupConfigured === true

  // Existing vehicles are the copy sources. Copy is only offered when there is one to copy from.
  const { data: garage } = useGarage()
  const sources = Array.isArray(garage) ? garage : []
  const effectiveCopyId = isCopy ? (copyFromId ?? sources[0]?.vehicleId ?? null) : null
  const copySourceReg = sources.find((v) => v.vehicleId === effectiveCopyId)?.registration ?? ''

  // The generic set (server-owned) or the source vehicle's ACTIVE definitions (copy is active-only, matching the
  // server). Each is fetched only when its source is the chosen one.
  const { data: starterChecks } = useStarterChecks(open && isGeneric)
  const { data: copyChecks } = useVehicleChecks(copySourceReg, open && isCopy)
  const activeChecks: SelectableCheck[] = isGeneric
    ? (starterChecks ?? [])
    : isCopy
      ? (copyChecks ?? []).filter((d) => d.isActive)
      : []
  const keptNames = activeChecks.map((c) => c.name).filter((n) => !deselected.has(n))

  const toggleCheck = (name: string) =>
    setDeselected((s) => {
      const next = new Set(s)
      if (next.has(name)) next.delete(name)
      else next.add(name)
      return next
    })

  const pickSource = (id: number) => {
    setCopyFromId(id)
    setDeselected(new Set()) // a different source vehicle means a different list; start it all-on.
  }

  const set = <K extends keyof Draft>(key: K, value: Draft[K]) => {
    // Switching source resets the selection — a deselection under one source means nothing under another.
    if (key === 'checkSource') setDeselected(new Set())
    setDraft((d) => ({ ...d, [key]: value }))
  }

  /**
   * Ask the DVLA what this plate is.
   *
   * A read that creates nothing — the design's promise is "you confirm before anything is created", so this
   * only fills fields and every one stays editable. A failure is not a dead end: the message says what went
   * wrong and the form below it is exactly as usable as it was before, which is why the lookup is an
   * accelerator rather than a step.
   */
  const lookup = useMutation({
    mutationFn: async () => {
      const reg = draft.registration.trim()
      const result = await apiRequest<{
        make: string | null
        model: string | null
        year: number | null
        colour: string | null
        engineSizeCc: number | null
        fuelType: Draft['fuelType'] | null
        motExpiry: string | null
        motStatus: string | null
        taxStatus: string | null
        vedExpiry: string | null
      }>(`/api/vehicles/lookup/${encodeURIComponent(reg)}`)
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: (found) => {
      setLookupError(null)
      // Only overwrite what came back. A null from the DVLA must not blank something already typed — VES
      // frequently omits the model, and losing a hand-typed "Freelander 1" to an absent field would make the
      // lookup destructive.
      setDraft((d) => ({
        ...d,
        make: found.make ?? d.make,
        model: found.model ?? d.model,
        year: found.year?.toString() ?? d.year,
        colour: found.colour ?? d.colour,
        fuelType: found.fuelType ?? d.fuelType,
      }))
      setLooked({
        engineSizeCc: found.engineSizeCc,
        motExpiry: found.motExpiry,
        vedExpiry: found.vedExpiry,
        taxStatus: found.taxStatus,
        motStatus: found.motStatus,
      })
    },
    onError: (error) => {
      setLooked(null)
      setLookupError(error instanceof Error ? error.message : 'Could not look that registration up.')
    },
  })

  const mutation = useMutation({
    mutationFn: async () => {
      const result = await apiRequest<{ id: number; registration: string }>('/api/vehicles', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          registration: draft.registration.trim(),
          make: draft.make.trim(),
          model: draft.model.trim(),
          variant: draft.variant.trim() || null,
          // Through `num`, the same reader `validate` uses — so what was checked is what is sent, and a blank
          // box cannot arrive as a zero.
          year: num(draft.year),
          colour: draft.colour.trim() || null,
          purchaseDate: draft.purchaseDate,
          purchaseMileage: num(draft.purchaseMileage),
          purchasePrice: num(draft.purchasePrice),
          fuelType: draft.fuelType,
          // Carried from the lookup, not shown as fields. motExpirySeed is a SEED — read only while the car
          // has no MOT record, superseded by the first logged pass (DEC-015). vedExpiry is a genuinely stored
          // input, because nothing in the app logs a road-tax payment.
          engineSizeCc: looked?.engineSizeCc ?? undefined,
          motExpirySeed: looked?.motExpiry ?? undefined,
          vedExpiry: looked?.vedExpiry ?? undefined,
          checkSource: draft.checkSource,
          // Copy needs its source vehicle; omitted for the other sources.
          copyChecksFromVehicleId: isCopy ? effectiveCopyId : undefined,
          // Generic or copy: omit (undefined → dropped by JSON.stringify) when every check is still selected, so
          // the untouched path applies the whole source. A strict subset sends the kept names; all deselected
          // sends [] → no checks.
          selectedCheckNames: (isGeneric || isCopy) && deselected.size > 0 ? keptNames : undefined,
        }),
      })
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async (created) => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.garage })
      toast(`${created.registration} added · opening reading recorded`)
      setDraft(EMPTY)
      setDeselected(new Set())
      setCopyFromId(null)
      setErrors({})
      onClose()
      // Straight to its dashboard. Adding a car is not the goal; looking at it is.
      navigate(hrefFor('dashboard', created.registration))
    },
    // The API's reason, not a generic failure — "A vehicle with registration 'BT53 AKJ' already exists" is
    // actionable and "Conflict" is not. Through `reportApiError` like every other sheet, so a 400's per-field
    // map lands *on the fields* instead of collapsing into one footer line that names none of them.
    onError: (error) => {
      setErrors(reportApiError(error, FIELD_KEYS))
      focusFirstInvalidField()
    },
  })

  const submit = () => {
    const found = validate(draft)
    setErrors(found)
    if (Object.keys(found).length === 0) mutation.mutate()
    // Otherwise the button looks broken: the sheet scrolls, its footer is pinned, and the first bad field can
    // be a screen above the button that was just pressed.
    else focusFirstInvalidField()
  }

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title="Add a vehicle"
      subtitle="its own logs, checks, budget and dashboard"
      onSubmit={submit}
      footer={
        <Btn onClick={() => {}} type="submit">
          {mutation.isPending ? 'Adding…' : 'Add vehicle'}
        </Btn>
      }
    >
      {/* Neutral examples throughout. A placeholder naming a real make and model reads as pre-filled data
          rather than a hint, and this form is the first thing a new account sees. */}
      <Field label="Registration" wide error={fieldError(errors, 'registration')} hint="e.g. AB12 CDE">
        {(p) => (
          <div className="lookup">
            <input
              type="text"
              className="reg-input"
              placeholder="REG PLATE"
              maxLength={8}
              autoFocus
              value={draft.registration}
              onChange={(e) => {
                set('registration', e.target.value.toUpperCase())
                // A new plate makes the old answer stale. Clearing here beats leaving a previous car's MOT
                // seed attached to a registration it has nothing to do with.
                setLooked(null)
                setLookupError(null)
              }}
              {...p}
            />
            {/* Guarded in the handler rather than disabled: `Btn` has no disabled state, and the sheet's own
                submit already follows the convention of changing its label while pending. The lookup is an
                idempotent GET, so a double click costs a repeated read and nothing else. */}
            {canLookUp && (
              <Btn
                type="button"
                onClick={() => {
                  if (draft.registration.trim() !== '' && !lookup.isPending) lookup.mutate()
                }}
              >
                {lookup.isPending ? 'Looking up…' : 'Look up'}
              </Btn>
            )}
          </div>
        )}
      </Field>

      {/* The design's verbatim promise: it says a lookup fills the form and creates nothing, which is exactly
          what it does. It goes with the button — a promise about a control that is not on screen describes a
          product this deployment does not have, and the plate field's own "e.g. AB12 CDE" is the whole hint a
          hand-typed registration needs. */}
      {canLookUp && (
        <div className="field wide">
          {lookupError !== null ? (
            <span className="hint err" role="alert">
              {lookupError} Fill the fields in below instead — nothing here depends on the lookup.
            </span>
          ) : looked !== null ? (
            <span className="hint" role="status">
              Filled from the DVLA — check every field before creating.
              {looked.taxStatus !== null && ` Tax: ${looked.taxStatus.toLowerCase()}.`}
              {looked.motExpiry !== null &&
                ' The MOT date seeds the countdown until a pass is logged, and a logged pass then wins.'}
            </span>
          ) : (
            <span className="hint">
              Fetches make, year, colour, engine and MOT/tax status from the DVLA — you confirm before anything
              is created.
            </span>
          )}
        </div>
      )}

      <Field label="Make" error={fieldError(errors, 'make')}>
        {(p) => <input type="text" placeholder="e.g. Ford" value={draft.make} onChange={(e) => set('make', e.target.value)} {...p} />}
      </Field>
      <Field label="Model" error={fieldError(errors, 'model')}>
        {(p) => <input type="text" placeholder="e.g. Focus" value={draft.model} onChange={(e) => set('model', e.target.value)} {...p} />}
      </Field>
      <Field label="Variant">
        {(p) => <input type="text" placeholder="e.g. 1.6 Zetec" value={draft.variant} onChange={(e) => set('variant', e.target.value)} {...p} />}
      </Field>
      <Field label="Year" error={fieldError(errors, 'year')}>
        {(p) => <input type="text" inputMode="numeric" placeholder="e.g. 2014" value={draft.year} onChange={(e) => set('year', e.target.value)} {...p} />}
      </Field>
      <Field label="Colour">
        {(p) => <input type="text" placeholder="e.g. Silver" value={draft.colour} onChange={(e) => set('colour', e.target.value)} {...p} />}
      </Field>
      <Field label="Fuel">
        {(p) => (
          <Select value={draft.fuelType} onChange={(e) => set('fuelType', e.target.value as Draft['fuelType'])} {...p}>
            <option value="Petrol">Petrol</option>
            <option value="Diesel">Diesel</option>
            <option value="Hybrid">Hybrid</option>
            <option value="Electric">Electric</option>
            {/* LPG, not "Plug-in hybrid". The wire enum is Petrol/Diesel/Hybrid/Electric/LPG — there is no
                PlugInHybrid member, so choosing it sent a value the server rejects. A select whose options do
                not come from the contract is a select that can offer a value nothing accepts. */}
            <option value="LPG">LPG</option>
          </Select>
        )}
      </Field>

      <Field label="Purchase date" error={fieldError(errors, 'purchaseDate')}>
        {(p) => <input type="date" value={draft.purchaseDate} onChange={(e) => set('purchaseDate', e.target.value)} {...p} />}
      </Field>
      <Field
        label="Mileage at purchase"
        error={fieldError(errors, 'purchaseMileage')}
        hint="becomes the opening odometer reading"
      >
        {/* "Mileage at purchase", not the design's "Current mileage". It is what the domain stores and what
            becomes the founding MileageReading — and for a car bought two years ago those are very different
            numbers. Asking for the wrong one would put a false reading at the bottom of the odometer's
            history, where everything else is measured from. */}
        {(p) => <input type="text" inputMode="numeric" placeholder="76632" value={draft.purchaseMileage} onChange={(e) => set('purchaseMileage', e.target.value)} {...p} />}
      </Field>
      <Field
        label="Purchase price £"
        error={fieldError(errors, 'purchasePrice')}
        hint="becomes an expense — counts toward total outlay, not running cost"
      >
        {/* The counterpart to the mileage hint above: that number founds the odometer, this one founds the
            expenses log. Both are load-bearing and neither used to say so — this field had no hint at all,
            while quietly being the largest line in the vehicle's spend. */}
        {(p) => <input type="text" inputMode="decimal" placeholder="1700" value={draft.purchasePrice} onChange={(e) => set('purchasePrice', e.target.value)} {...p} />}
      </Field>

      <Field
        label="Regular checks"
        wide
        hint="The starter set is 15 checks that apply to any car. Add the ones specific to yours afterwards."
      >
        {(p) => (
          <Select value={draft.checkSource} onChange={(e) => set('checkSource', e.target.value as CheckSource)} {...p}>
            <option value="GenericStarterSet">Generic starter set (15)</option>
            {/* Only when there is a car to copy from. */}
            {sources.length > 0 && <option value="CopyFromVehicle">Copy from another vehicle</option>}
            <option value="None">None — I will add my own</option>
          </Select>
        )}
      </Field>

      {isCopy && sources.length > 0 && (
        <Field label="Copy checks from" wide hint="its active checks — trim the ones this car does not need below">
          {(p) => (
            <Select value={String(effectiveCopyId ?? '')} onChange={(e) => pickSource(Number(e.target.value))} {...p}>
              {sources.map((v) => (
                <option key={v.vehicleId} value={v.vehicleId}>
                  {v.registration} — {v.name}
                </option>
              ))}
            </Select>
          )}
        </Field>
      )}

      {/* The set laid open: deselect the ones this car does not need (no air-con, electric-assist steering)
          before it is created, rather than pruning them from the checks screen afterward. Defaults all-on, so
          leaving it be gives exactly the whole source (the fifteen, or every active check on the copied car). */}
      {(isGeneric || isCopy) && activeChecks.length > 0 && (
        <CheckSelectList
          checks={activeChecks}
          deselected={deselected}
          onToggle={toggleCheck}
          header={isCopy ? 'copied to this car' : 'included in this car'}
        />
      )}

      {/* What could not be pinned to a field — a duplicate registration, a dropped connection. `.hint.err` is
          the house error tone; it used to be an inline style saying the same thing in its own words. */}
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

/**
 * Only what the API would refuse anyway, checked here so the answer is instant and beside the field.
 *
 * The server validates independently — this is a courtesy, not the gate. Keys are lowercase because
 * `fieldError` lowercases its lookup: the server cases these inconsistently, and one casing rule for both
 * sources is what lets a field render a client message and a server one through the same prop.
 */
function validate(draft: Draft): FieldErrors {
  const errors: FieldErrors = {}

  if (draft.registration.trim() === '') errors['registration'] = ['A car needs its registration.']
  if (draft.make.trim() === '') errors['make'] = ['Which make?']
  if (draft.model.trim() === '') errors['model'] = ['Which model?']

  const year = num(draft.year)
  if (year === null || !Number.isInteger(year) || year < 1900) errors['year'] = ['A four-digit year.']

  if (draft.purchaseDate === '') errors['purchasedate'] = ['When did you buy it?']

  // Two messages, not one: an empty box and a bad number are different mistakes, and "the reading the day you
  // bought it" does not tell someone who typed "80.5" what is wrong with it.
  const mileage = num(draft.purchaseMileage)
  if (mileage === null) errors['purchasemileage'] = ['The odometer reading the day you bought it.']
  else if (!Number.isInteger(mileage) || mileage < 0) errors['purchasemileage'] = ['Whole miles, and not negative.']

  // The price is optional — empty is a real answer. A typo in it is not: unchecked, a NaN reached
  // JSON.stringify, arrived as null, and the car was created as though no price had been given at all.
  if (draft.purchasePrice.trim() !== '') {
    const price = num(draft.purchasePrice)
    if (price === null || price < 0) errors['purchaseprice'] = ['A price like 1700, or leave it empty.']
  }

  return errors
}
