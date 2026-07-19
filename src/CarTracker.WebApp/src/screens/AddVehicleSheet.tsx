import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { apiRequest } from '../api/client'
import { ApiFailure, queryKeys, useGarage } from '../api/queries'
import { useStarterChecks, useVehicleChecks } from '../api/reference'
import { Btn } from '../components/Btn'
import { CheckSelectList, type SelectableCheck } from '../components/CheckSelectList'
import { Field, Select, Sheet } from '../components/Sheet'
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
  fuelType: 'Petrol' | 'Diesel' | 'Hybrid' | 'Electric' | 'PlugInHybrid'
  checkSource: CheckSource
}

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
 * **The design's DVLA lookup is not here, deliberately.** Its sheet leads with a GB plate input, a "Look up"
 * button and the promise "Fetches make, model, year, colour, engine, MOT and tax status from the DVLA — you
 * confirm before anything is created". No such thing exists: DVLA lookup sits unscheduled in the §8 backlog.
 * Porting the button would make the reg field look like the fast path and leave someone waiting for a fill-in
 * that never comes — the same fault as the settings drag-grips that do not drag, and worse here because it is
 * the first thing anyone does. The registration is still styled as a plate, because it is a plate. When
 * lookup is built, the button arrives with it.
 */
export function AddVehicleSheet({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [draft, setDraft] = useState<Draft>(EMPTY)
  const [errors, setErrors] = useState<Record<string, string[]>>({})
  // Which checks the owner has turned OFF. Tracking deselections (not selections) makes "all on" the default
  // with no dependence on when the list finishes loading, and lets the untouched case send nothing.
  const [deselected, setDeselected] = useState<Set<string>>(new Set())
  const [copyFromId, setCopyFromId] = useState<number | null>(null)
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const { toast } = useToast()

  const isGeneric = draft.checkSource === 'GenericStarterSet'
  const isCopy = draft.checkSource === 'CopyFromVehicle'

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
          year: Number(draft.year),
          colour: draft.colour.trim() || null,
          purchaseDate: draft.purchaseDate,
          purchaseMileage: Number(draft.purchaseMileage),
          purchasePrice: draft.purchasePrice === '' ? null : Number(draft.purchasePrice),
          fuelType: draft.fuelType,
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
    onError: (error) => {
      // The API's reason, not a generic failure — "A vehicle with registration 'BT53 AKJ' already exists" is
      // actionable and "Conflict" is not.
      setErrors({ _: [error instanceof Error ? error.message : 'Could not add the vehicle.'] })
    },
  })

  const submit = () => {
    const found = validate(draft)
    setErrors(found)
    if (Object.keys(found).length === 0) mutation.mutate()
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
      <Field label="Registration" wide hint={errors['registration']?.[0] ?? 'e.g. BT53 AKJ'}>
        {(p) => (
          <input
            type="text"
            className="reg-input"
            placeholder="REG PLATE"
            maxLength={8}
            autoFocus
            value={draft.registration}
            onChange={(e) => set('registration', e.target.value.toUpperCase())}
            {...p}
          />
        )}
      </Field>

      <Field label="Make" hint={errors['make']?.[0]}>
        {(p) => <input type="text" placeholder="Land Rover" value={draft.make} onChange={(e) => set('make', e.target.value)} {...p} />}
      </Field>
      <Field label="Model" hint={errors['model']?.[0]}>
        {(p) => <input type="text" placeholder="Freelander 1" value={draft.model} onChange={(e) => set('model', e.target.value)} {...p} />}
      </Field>
      <Field label="Variant">
        {(p) => <input type="text" placeholder="1.8 SE Station Wagon" value={draft.variant} onChange={(e) => set('variant', e.target.value)} {...p} />}
      </Field>
      <Field label="Year" hint={errors['year']?.[0]}>
        {(p) => <input type="text" inputMode="numeric" placeholder="2003" value={draft.year} onChange={(e) => set('year', e.target.value)} {...p} />}
      </Field>
      <Field label="Colour">
        {(p) => <input type="text" placeholder="Navy blue" value={draft.colour} onChange={(e) => set('colour', e.target.value)} {...p} />}
      </Field>
      <Field label="Fuel">
        {(p) => (
          <Select value={draft.fuelType} onChange={(e) => set('fuelType', e.target.value as Draft['fuelType'])} {...p}>
            <option value="Petrol">Petrol</option>
            <option value="Diesel">Diesel</option>
            <option value="Hybrid">Hybrid</option>
            <option value="PlugInHybrid">Plug-in hybrid</option>
            <option value="Electric">Electric</option>
          </Select>
        )}
      </Field>

      <Field label="Purchase date" hint={errors['purchaseDate']?.[0]}>
        {(p) => <input type="date" value={draft.purchaseDate} onChange={(e) => set('purchaseDate', e.target.value)} {...p} />}
      </Field>
      <Field
        label="Mileage at purchase"
        hint={errors['purchaseMileage']?.[0] ?? 'becomes the opening odometer reading'}
      >
        {/* "Mileage at purchase", not the design's "Current mileage". It is what the domain stores and what
            becomes the founding MileageReading — and for a car bought two years ago those are very different
            numbers. Asking for the wrong one would put a false reading at the bottom of the odometer's
            history, where everything else is measured from. */}
        {(p) => <input type="text" inputMode="numeric" placeholder="76632" value={draft.purchaseMileage} onChange={(e) => set('purchaseMileage', e.target.value)} {...p} />}
      </Field>
      <Field label="Purchase price £">
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

      {errors['_'] && (
        <div className="field wide">
          <span className="hint" style={{ color: 'var(--due)' }} role="alert">
            {errors['_'][0]}
          </span>
        </div>
      )}
    </Sheet>
  )
}

/**
 * Only what the API would refuse anyway, checked here so the answer is instant and beside the field.
 *
 * The server validates independently — this is a courtesy, not the gate.
 */
function validate(draft: Draft): Record<string, string[]> {
  const errors: Record<string, string[]> = {}

  if (draft.registration.trim() === '') errors['registration'] = ['A car needs its registration.']
  if (draft.make.trim() === '') errors['make'] = ['Which make?']
  if (draft.model.trim() === '') errors['model'] = ['Which model?']

  const year = Number(draft.year)
  if (!Number.isInteger(year) || year < 1900) errors['year'] = ['A four-digit year.']

  if (draft.purchaseDate === '') errors['purchaseDate'] = ['When did you buy it?']

  const mileage = Number(draft.purchaseMileage)
  if (!Number.isInteger(mileage) || mileage < 0) {
    errors['purchaseMileage'] = ['The odometer reading the day you bought it.']
  }

  return errors
}
