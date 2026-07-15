import type { VehicleSummary } from '../../api/client'
import { Contours } from '../../components/Contours'
import { Odometer } from '../../components/Odometer'
import { RegPlate } from '../../components/RegPlate'
import { Wrap } from '../../components/layout'

const longDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' })

const shortDate = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

/** A chip only exists if there is something to put in it. An empty one is a worse answer than no chip. */
function Chip({ label, value }: { label: string; value: string | null | undefined }) {
  if (value === null || value === undefined || value === '') return null
  return (
    <span className="chip">
      {label} <b>{value}</b>
    </span>
  )
}

/**
 * The dashboard's header. Not `<PageHead>` — see the note on that component.
 *
 * The design hardcodes all five chips and every line of the odometer's sub-block. Each is derived here, and two
 * of them stop existing on a car that is not BT53: a vehicle added this morning has no colour, no garage, and
 * an odometer sub-line with nothing to say. The chips vanish rather than render "Colour —".
 */
export function Dossier({ summary }: { summary: VehicleSummary }) {
  const { identity: id, mileage } = summary

  return (
    <header className="dossier">
      <Contours variant="dossier" />
      <Wrap className="dossier-in">
        <div>
          <div className="eyebrow">Dashboard · computed live · {longDate(summary.asOfDate)}</div>
          <RegPlate reg={summary.registration} size="lg" />
          <h1>
            {summary.name}
            {id.variant !== null && id.variant !== '' && <span className="thin">{id.variant}</span>}
          </h1>
          <div className="chips">
            <Chip label="Year" value={id.year > 0 ? String(id.year) : null} />
            <Chip label="Colour" value={id.colour} />
            <Chip label="Drivetrain" value={[id.drivetrain, id.transmission].filter(Boolean).join(' · ') || null} />
            {/* Derived, and the one chip that must be: "owned 122 days" is wrong by morning if stored. */}
            <Chip label="Owned" value={`${id.daysOwned.toLocaleString('en-GB')} days`} />
            <Chip label="Garage" value={id.defaultGarage} />
          </div>
        </div>

        <div className="odo">
          <div className="odo-label">Odometer · miles</div>
          {mileage.currentMileage === null ? (
            // No readings at all. The design cannot render this — its odometer is a string literal — but it is
            // the state every new vehicle starts in, and six zero drums would be a reading of zero.
            <p className="odo-none">No mileage logged yet</p>
          ) : (
            <Odometer miles={mileage.currentMileage} />
          )}
          <div className="odo-sub">
            {mileage.asOfDate !== null && (
              <>
                Latest logged <b>{shortDate(mileage.asOfDate)}</b>
                <br />
              </>
            )}
            {/* The workbook's "current mileage (manual) 80,705 is behind latest logged 80,712" made visible.
                Only shown when the two actually disagree — on a clean history there is nothing to say, and
                the design's hardcoded "· 7 mi behind" says it regardless. */}
            {mileage.highestRecordedMileage !== null &&
              mileage.currentMileage !== null &&
              mileage.highestRecordedMileage > mileage.currentMileage && (
                <>
                  Highest recorded <b>{mileage.highestRecordedMileage.toLocaleString('en-GB')}</b> ·{' '}
                  {(mileage.highestRecordedMileage - mileage.currentMileage).toLocaleString('en-GB')} mi above
                  the latest reading
                  <br />
                </>
              )}
            {mileage.milesSincePurchase !== null && (
              <>
                Since purchase <b>{mileage.milesSincePurchase.toLocaleString('en-GB')} mi</b>
                {id.milesPerDay !== null && ` · ${id.milesPerDay} mi/day`}
              </>
            )}
          </div>
        </div>
      </Wrap>
    </header>
  )
}
