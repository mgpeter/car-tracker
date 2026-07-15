import type { GarageItem } from '../api/client'
import { AppLink } from '../lib/link'
import { Contours } from './Contours'
import { Icon } from './Icon'
import { Kv } from './Kv'
import { IntegrityPill, Pill } from './Pill'
import { RegPlate } from './RegPlate'

/** "80,712 mi" — grouped, because a six-digit odometer without separators is a number you have to parse. */
const miles = (n: number | null) => (n === null ? null : `${n.toLocaleString('en-GB')} mi`)
const money = (n: number | null) => (n === null ? null : `£${n.toFixed(2)}`)

/** True when nothing on the pill row would have anything to say. */
const quiet = (item: GarageItem) =>
  item.overdueCheckCount === 0 &&
  item.neverLoggedCheckCount === 0 &&
  item.openAnomalyCount === 0 &&
  item.renewalsOk

/**
 * One vehicle in the garage.
 *
 * Every figure is a projection of that car's summary (`GarageItem`), so the card and the dashboard behind it
 * cannot disagree — which is the whole reason the API shapes it that way rather than the card computing
 * anything.
 */
export function VehicleCard({ item }: { item: GarageItem }) {
  return (
    <AppLink
      to="dashboard"
      reg={item.registration}
      className="car"
      // The design makes the whole card one link, which is right for the thumb and wrong for the ear: without
      // this, the accessible name is every word on the card — "Active BT53 AKJ Land Rover Freelander 1
      // Odometer 80,712 mi 4,080 since purchase Running cost…" — read out as the name of a link. The figures
      // are still in the DOM and still read; this just gives the control a name a person would recognise.
      aria-label={`${item.registration}, ${item.name} — open dashboard`}
    >
      <div className="car-top">
        <Contours variant="card" />
        {item.status !== 'Active' && <span className="car-active">{item.status}</span>}
        {item.status === 'Active' && <span className="car-active">Active</span>}
        <RegPlate reg={item.registration} size="lg" />
        <div className="car-name">{item.name}</div>
      </div>

      <div className="car-body">
        <div className="car-kv num">
          <Kv
            label="Odometer"
            // Null is a real state — a car with no readings yet. "—" says so; "0 mi" would be a lie about a
            // car that has never been read.
            value={miles(item.currentMileage) ?? '—'}
            note={item.milesSincePurchase === null ? 'no readings yet' : `${item.milesSincePurchase.toLocaleString('en-GB')} since purchase`}
          />
          <Kv
            label="Running cost"
            value={item.costPerMile === null ? '—' : `£${item.costPerMile.toFixed(2)}/mi`}
            note={item.monthlyAverage === null ? 'no spend yet' : `${money(item.monthlyAverage)}/month`}
          />
          <Kv
            label="MOT"
            value={item.mot.daysRemaining === null ? '—' : `${item.mot.daysRemaining} days`}
            // "not recorded" rather than a blank. An unknown MOT is a thing to go and find out, and the card
            // should not let it look like a figure that happens to be missing.
            note={item.mot.expiryDate ?? 'not recorded'}
          />
          <Kv
            label="Fuel"
            value={item.averageMpg === null ? '—' : `${item.averageMpg.toFixed(1)} MPG`}
            // The last fill's own figure, not the average — the two differ and the card must not imply the
            // car is currently doing better than the last tank did.
            note={item.latestMpg === null ? 'no measurable fills' : `latest ${item.latestMpg.toFixed(1)}`}
          />
        </div>

        {/* One row, one job: what is wrong. A quiet car says so once rather than listing the things that are
            not wrong. */}
        <div className="car-stat">
          {quiet(item) && <Pill tone="ok">Nothing needs attention</Pill>}
          {item.overdueCheckCount > 0 && (
            <Pill tone="due">
              {item.overdueCheckCount} {item.overdueCheckCount === 1 ? 'check' : 'checks'} overdue
            </Pill>
          )}
          {item.neverLoggedCheckCount > 0 && (
            <Pill tone="never">
              {item.neverLoggedCheckCount} never logged
            </Pill>
          )}
          {/* Data integrity is its own axis — blue, and never a due state. <IntegrityPill> is the only thing
              that can render it, which is exactly why <Pill> has no `info` tone. */}
          {item.openAnomalyCount > 0 && (
            <IntegrityPill>
              {item.openAnomalyCount} integrity flag{item.openAnomalyCount === 1 ? '' : 's'}
            </IntegrityPill>
          )}
          {!item.renewalsOk && <Pill tone="soon">Renewal needs attention</Pill>}
        </div>
      </div>

      {/* Just the call to action. The design's footer names a specific concern — "Head-gasket watch: 2 weekly
          checks lapsed" — which this app cannot say: nothing models WHICH checks are the head-gasket watch.
          Two attempts at filling the space produced echoes instead: first the pills restated ("7 checks
          overdue" in both places), then the odometer restated the figure directly above it. A card this small
          has no room for a second voice saying the same thing. Tagging a check as a watch item is a real
          feature, and it would earn this line back with something to say. */}
      <div className="car-foot">
        <b>
          Open dashboard <Icon name="arrow-right" />
        </b>
      </div>
    </AppLink>
  )
}
