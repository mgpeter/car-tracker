import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import type { VehicleSummary } from '../../api/client'
import { apiRequest } from '../../api/client'
import { ApiFailure, queryKeys } from '../../api/queries'
import { Btn, Mark } from '../../components/Btn'
import { Panel } from '../../components/layout'
import { Field, Sheet } from '../../components/Sheet'
import { fieldError, formError, reportApiError, type FieldErrors } from '../../lib/formErrors'
import { useToast } from '../../shell/Toast'

/** Only the one fluid field this panel edits; the rest of VehicleDetail is VehicleInfoPage's concern. */
interface TankDetail {
  fluids: { fuelTankCapacityLitres: number | null }
}

/**
 * Fuel-tank capacity — the one stored figure behind the dashboard's full-tank range.
 *
 * Range is derived (average MPG x capacity) and never stored, but the capacity is a fact about the car, so it
 * lives here with the other stored inputs. Nullable and never defaulted: clear it and the range disappears
 * rather than falling back to a guessed tank size in the same typeface as the derived figures.
 */
export function FuelTankPanel({ reg }: { reg: string }) {
  const [editing, setEditing] = useState(false)

  const { data } = useQuery({
    queryKey: ['vehicle', reg, 'detail'] as const,
    queryFn: async () => {
      const r = await apiRequest<TankDetail>(`/api/vehicles/${encodeURIComponent(reg)}`)
      if (!r.ok) throw new ApiFailure(r.error)
      return r.value
    },
  })

  const capacity = data?.fluids?.fuelTankCapacityLitres ?? null

  return (
    <>
      <Panel>
        <div className="setrow num">
          <span className="sk">Fuel tank</span>
          <span className="sv">
            {capacity === null ? 'not recorded' : `${capacity} L`}
            <i>
              {capacity === null
                ? 'set it to show a full-tank range on the dashboard'
                : 'drives the dashboard full-tank range'}
            </i>
          </span>
          <Mark onClick={() => setEditing(true)}>{capacity === null ? 'Set' : 'Edit'}</Mark>
        </div>
      </Panel>

      <EditSheet reg={reg} open={editing} onClose={() => setEditing(false)} capacity={capacity} />
    </>
  )
}

function EditSheet({
  reg,
  open,
  onClose,
  capacity,
}: {
  reg: string
  open: boolean
  onClose: () => void
  capacity: number | null
}) {
  const [value, setValue] = useState<string | null>(null)
  const [errors, setErrors] = useState<FieldErrors>({})
  const queryClient = useQueryClient()
  const { toast } = useToast()

  const FIELD_KEYS = ['fueltankcapacitylitres'] as const

  // The user's edit if they have typed one this open, else the stored value. Reset the local edit whenever the
  // sheet is (re)opened so it seeds from the current capacity.
  const field = value ?? (capacity === null ? '' : String(capacity))

  // A blank clears the capacity (the range then vanishes rather than guessing a size), so blank is valid. Any
  // value that is present, though, must be a positive number of litres.
  const validate = (): FieldErrors => {
    const e: FieldErrors = {}
    if (field.trim() !== '' && !(Number(field) > 0)) e['fueltankcapacitylitres'] = ['Litres must be a positive number, or blank to clear it.']
    return e
  }

  const submit = () => {
    const found = validate()
    setErrors(found)
    if (Object.keys(found).length === 0) mutation.mutate()
  }

  const mutation = useMutation({
    mutationFn: async () => {
      const litres = field.trim() === '' ? null : Number(field)
      const result = await apiRequest<VehicleSummary>(`/api/vehicles/${encodeURIComponent(reg)}`, {
        method: 'PATCH',
        headers: { 'Content-Type': 'application/json' },
        // A fluids block sets the field authoritatively — a blank clears it, so the range vanishes.
        body: JSON.stringify({ fluids: { fuelTankCapacityLitres: litres } }),
      })
      if (!result.ok) throw new ApiFailure(result.error)
      return result.value
    },
    onSuccess: async () => {
      // The summary drives the dashboard range; the garage card projects the same summary; the detail feeds
      // this panel and the vehicle-info screen.
      await queryClient.invalidateQueries({ queryKey: queryKeys.vehicleSummary(reg) })
      await queryClient.invalidateQueries({ queryKey: queryKeys.garage })
      await queryClient.invalidateQueries({ queryKey: ['vehicle', reg, 'detail'] })
      toast('Saved · the full-tank range recomputed')
      setValue(null)
      setErrors({})
      onClose()
    },
    onError: (e) => setErrors(reportApiError(e, FIELD_KEYS)),
  })

  return (
    <Sheet
      open={open}
      onClose={onClose}
      title="Fuel tank capacity"
      subtitle="drives the dashboard full-tank range"
      onSubmit={submit}
      footer={
        <Btn onClick={() => {}} type="submit">
          {mutation.isPending ? 'Saving…' : 'Save'}
        </Btn>
      }
    >
      <Field
        label="Capacity"
        error={fieldError(errors, 'fueltankcapacitylitres')}
        hint="litres — leave blank to clear it and hide the range rather than guess a size"
      >
        {(p) => (
          <input
            type="text"
            inputMode="decimal"
            placeholder="59"
            value={field}
            onChange={(e) => setValue(e.target.value)}
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
