import type { AnomalyEntityType, AnomalyKind } from '../api/anomalies'
import type { ScreenId } from '../shell/nav'

/**
 * What each detector is looking for, in the reader's terms rather than the enum's.
 *
 * `Record<AnomalyKind, …>` off the **wire enum**, so a fifth detector fails the build here instead of
 * rendering its enum name — the mistake the mileage screen's hand-guessed origin map already made once.
 *
 * The integrity queue's own comment claimed exactly that while its declaration read `Record<string, …>`, and
 * the fourth detector duly shipped with no entry: `EquipmentCostWithoutDate` rows rendered their raw message
 * as the title, printed it again in the comparison block below, and left the explanation an empty paragraph.
 *
 * It lives here rather than on the queue screen because the **fix banner** shows the same title on whichever
 * log you were sent to. Two copies of this map would let the queue and the banner describe one flag two ways,
 * which is the drift the whole integrity axis exists to catch.
 *
 * Not in `api/anomalies.ts`: that module is the wire type and its query, and `FIX_SCREEN` below needs
 * `ScreenId` from the shell — an `api/` module importing the nav table would be the wrong way round.
 */
export const ANOMALY_KIND: Record<AnomalyKind, { title: string; why: string }> = {
  MileageNonMonotonic: {
    title: 'A reading is above a later one',
    why: 'A mileage cannot go down, so one of the two is a typo. Which one is not ours to guess — the odometer keeps deriving from the newest reading by date, and nothing has been changed.',
  },
  ImplausibleMpg: {
    title: 'An MPG outside what the car can do',
    why: 'Computed correctly from exact litres and still not real — usually a missed fill or a mistyped odometer. It is excluded from the averages and kept on the entry: marked, not deleted.',
  },
  FuelCostDiscrepancy: {
    title: 'A fill costs what its litres and price do not',
    why: 'Litres times price per litre does not reach the total on the receipt. Receipts round, so a penny is normal and this is not that.',
  },
  EquipmentCostWithoutDate: {
    title: 'Money the app holds and counts nowhere',
    why: 'An item you own — or have on order — reaches spend through a mirrored expense, and that expense needs a date to sit in the right month. So a costed item with no purchase date is absent from spend, from cost-per-mile and from the Equipment & Tools budget, while still showing a price on its row. Adding the date is the whole fix. Kit still on the shopping list is exempt: a To-order price is an estimate, not money.',
  },
  FutureDatedEntry: {
    title: 'Money dated on a day that has not happened',
    why: 'Spend counts it, because a bill paid in advance is spent — the rule that stopped totals at today once hid £1,183 of tyres while still counting the odometer reading from the same service. Counting it means a mistyped year would inflate a total rather than shrink one, so the date is questioned here instead of adjusted. If you paid ahead, nothing needs doing: the flag clears itself on the day.',
  },
}

/**
 * Where a flag is fixed — **keyed on the row, not the finding**.
 *
 * `EntityType` says which table the detector named; this says which screen shows it. It was keyed on the kind
 * until `FutureDatedEntry` arrived, which is one finding that can land on a service record, a fill, an item, a
 * wash or a hand-typed expense depending on which row carries the date — so a kind→screen map could not route
 * it. Re-keying lost nothing: all four earlier kinds mapped to exactly the screen their entity type does.
 *
 * `Record<AnomalyEntityType, ScreenId>` so a new entity type cannot ship without somewhere to send its owner.
 */
export const FIX_SCREEN: Record<AnomalyEntityType, ScreenId> = {
  MileageReading: 'mileage',
  FuelEntry: 'fuel',
  EquipmentItem: 'equipment',
  ServiceRecord: 'service',
  WashEntry: 'wash',
  ExpenseEntry: 'expenses',
}
