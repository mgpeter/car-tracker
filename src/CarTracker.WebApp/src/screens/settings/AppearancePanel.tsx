import { Seg } from '../../components/Seg'
import { Panel } from '../../components/layout'
import { setFuelUnit, useFuelUnit, type FuelUnit } from '../../lib/fuelUnit'
import { useToast } from '../../shell/Toast'

const UNIT_OPTIONS: ReadonlyArray<{ value: FuelUnit; label: string }> = [
  { value: 'mpg', label: 'MPG' },
  { value: 'l100', label: 'L/100 km' },
]

/**
 * Display preferences — client-side, like the theme.
 *
 * The fuel-economy unit is a render choice, not a stored value: every fill already carries both MPG and
 * L/100 km, computed server-side, so switching recomputes nothing and changes no stored figure. It persists in
 * localStorage beside the theme.
 */
export function AppearancePanel() {
  const unit = useFuelUnit()
  const { toast } = useToast()

  const change = (next: FuelUnit) => {
    if (next === unit) return
    setFuelUnit(next)
    // The design's own copy for this switch: it names the equivalence so the change reads as a display choice,
    // not a recomputation.
    toast(
      next === 'l100'
        ? 'Display switches to L/100 km — 28.7 MPG renders as 9.8'
        : 'Display switches to MPG',
    )
  }

  return (
    <Panel>
      <div className="setrow">
        <span className="sk">Fuel economy</span>
        <span className="sv">
          <Seg label="Fuel economy units" options={UNIT_OPTIONS} value={unit} onChange={change} />
          <i>both units are computed per fill — this only chooses which is shown</i>
        </span>
      </div>
    </Panel>
  )
}
