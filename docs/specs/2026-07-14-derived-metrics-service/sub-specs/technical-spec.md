# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-14-derived-metrics-service/spec.md

## Technical Requirements

### Shape and placement

- Lives in `src/CarTracker.Domain`. Depends on `CarTracker.Data` for entities and `CarTracker.Shared` for enums. Nothing depends on it except the API (Phase 2) and the MCP server (Phase 4).
- `IDerivedMetricsService` is the single public facade README §4 requires. Behind it sit focused calculators: `MileageCalculator`, `FuelEconomyCalculator`, `SpendCalculator`, `RenewalCalculator`, `CheckStatusCalculator`, `BudgetCalculator`.
- **Calculators are pure functions over loaded data.** They take collections and a reference date, and return results. No `DbContext`, no `IQueryable`, no async. A query layer loads; calculators compute.

  This is what makes the test suite possible: hand-computed fixtures go in, figures come out, no database required. Only the facade and the query layer need integration tests.
- Every method takes a `vehicleId` (DEC-002; multi-vehicle is active scope per DEC-007).
- Metrics compute for any vehicle regardless of its lifecycle `status` — a Sold car's history still answers
  questions. Filtering Sold/SORN out of attention surfaces is presentation, done by the garage and list UIs,
  never inside a calculator.

### Time

- Inject `TimeProvider`. Never call `DateTime.Now`, `DateTime.UtcNow`, or `DateOnly.FromDateTime(DateTime.Today)` anywhere in the domain.

  Non-negotiable: every expected figure in this spec is stated at a reference date of 2026-07-14. A service that reads the system clock cannot be tested against them, and "days to renewal" is untestable in principle without a fixed now.
- **All date arithmetic is in Europe/London local dates.** A renewal expires on a day, not at an instant. Computing days-to-renewal in UTC puts the answer off by one for part of the year, and a countdown that flips a day early around a BST boundary is the kind of bug that gets noticed exactly once, in the worst way.
- Convert `TimeProvider.GetUtcNow()` to `DateOnly` via the Europe/London zone at the single entry point; pass `DateOnly` down. Calculators never see a `DateTimeOffset`.

### Numeric handling

- `decimal` throughout. No `double`, no `float`, including for MPG. Money must be exact, and there is no performance argument at this scale.
- **Never round intermediates.** Round only at presentation. Rounding per-fill MPG to 1dp and then averaging gives a different fleet average than averaging then rounding.
- Presentation precision: money 2dp, MPG 1dp, L/100km 1dp, litres 2dp, price/L 3dp, cost-per-mile 3dp.
- Round half away from zero (`MidpointRounding.AwayFromZero`), not banker's rounding. .NET's default is banker's, which surprises everyone reading a fuel receipt.

### Current mileage: resolving the "max/most-recent" ambiguity

README §2 says current mileage is the "max/most-recent" reading. **In this data those disagree**, so the spec must choose.

- The Service History row dated 27 Jun 2026 logs 83,000 mi. The latest reading, dated 8 Jul 2026, is 80,712.
- `MAX(mileage)` returns 83,000 — wrong.
- Most recent by date returns 80,712 — the verified value.

**Rule: current mileage is the mileage of the most recent reading by date.** Tie-break by higher mileage, then by `created_at`.

Rationale: an odometer only advances, so max and most-recent normally agree. When they disagree, one row is wrong. The most recent reading is the one most likely to reflect the odometer as it actually reads, and a historical typo must not permanently inflate current mileage — which is what `MAX` would do, forever, for every downstream figure.

The result type carries the disagreement rather than hiding it:

```
MileageResult {
  CurrentMileage        // 80,712
  AsOfDate              // 2026-07-08
  MilesSincePurchase    // 4,080
  HasNonMonotonicHistory // true
  HighestRecordedMileage // 83,000 — present only when it exceeds CurrentMileage
}
```

`HasNonMonotonicHistory` is what lets the Dashboard show a data-integrity flag (blue axis per the design language, distinct from due-status) instead of silently picking a number.

### Fuel economy

**UK MPG.** 1 imperial gallon = 4.54609 litres exactly.

```
milesSinceLast = current.Mileage - previous.Mileage
mpg            = milesSinceLast * 4.54609m / current.Litres
litresPer100Km = current.Litres * 100m / (milesSinceLast * 1.609344m)
```

1 mile = 1.609344 km exactly. Both constants are exact definitions, not approximations — declare them as named `const decimal`, never inline.

**Invariant worth testing:** `mpg * litresPer100Km == 4.54609 * 100 / 1.609344 == 282.4809...` for any non-zero interval. One property test over the whole fuel history catches a transposed constant or an inverted formula in either direction.

**MPG is only valid full-to-full.** The interval's litres only equal the fuel consumed if the tank was full at both ends.

```
IsReliable = previous.FillLevel == Full && current.FillLevel == Full
```

- The first fill has no predecessor: no MPG, `UnreliableReason = NoPreviousFill`.
- Either end partial: MPG computed but `IsReliable = false`, `UnreliableReason = PartialFill`.
- **Fleet average, best, and worst are computed over reliable intervals only.** A partial fill yields a meaningless figure that would otherwise become "best MPG" and sit on the Dashboard as good news. This is the single most likely way for this feature to lie.
- `milesSinceLast <= 0` → no MPG, `UnreliableReason = NonMonotonicMileage`. Never divide by zero or return a negative MPG.

**Fleet aggregates:**

```
totalLitres  = SUM(litres)                    -- 556.47 over 13 fills, NOT 1,112.94
avgPricePerL = SUM(totalCost) / SUM(litres)   -- volume-weighted
```

**Open question — average price per litre.** This spec computes it volume-weighted, which is the correct answer to "what did fuel cost me per litre": a 50 L fill at £1.40 and a 10 L fill at £1.60 average to £1.433, not £1.50. The workbook may well use a simple `AVERAGE` of the price column.

If the Dashboard's figure disagrees, that is a **fifth finding — a definition difference, not a defect** — and it must be reported to the owner rather than silently resolved in either direction. Do not change the formula to match the sheet without a decision. Record the outcome as a decision entry.

### Spend rollups

Groups per README §3.1, over `expense_entries`:

| Group | Categories |
|---|---|
| Fuel | `Fuel` |
| Service and repairs | `Service`, `Repair`, `Parts` |
| Statutory | `Insurance`, `Tax`, `MOT` |
| Total | all |

- Fuel spend comes from `expense_entries` where `category = 'Fuel'` — which, after mirroring, *are* the fuel entries. Never sum `fuel_entries.total_cost` separately; that is a second code path to the same number and is exactly how the £163.16 gap happened.
- YTD is the current calendar year (1 Jan to the reference date), per README §3.5.
- Since-purchase runs from `vehicle.PurchaseDate`.
- `costPerMile = totalSpendSincePurchase / milesSincePurchase`. Guard `milesSincePurchase == 0` — a vehicle bought today has no cost-per-mile, and the answer is null, not zero and not infinity.
- `monthlyAverage = totalSpendSincePurchase / monthsSincePurchase`, where `monthsSincePurchase = daysSincePurchase / 365.25m * 12m`, floored at 1 month. At the reference date that is 122 days → 4.008 months, so the figure is not distorted by a part-month.
- The purchase price itself (`category = 'Purchase'`) is included in total spend since purchase. It is a real cost. Cost-per-mile including a £-thousands purchase over 4,080 miles is a large number, and that is the honest figure — but expose `totalSpendExcludingPurchase` alongside it, because "what does it cost me to run" is the more useful question and both are legitimate.

### Renewals

```
daysRemaining = expiryDate.DayNumber - referenceDate.DayNumber
```

Sources:

| Renewal | Source |
|---|---|
| MOT | `MAX(next_due_date)` over `service_records` where `type = 'MOT'`; falls back to `vehicle.MotExpirySeed` only when no such record exists |
| Insurance | `vehicle.InsurancePeriodEnd` |
| Tax | `vehicle.VedExpiry` |
| Next service (date) | `MAX(next_due_date)` over non-MOT `service_records` |
| Next service (miles) | `next_due_mileage` on the latest service record, minus current mileage |

MOT at the reference date: expiry 8 Jul 2027, `daysRemaining = 359`. Not the Dashboard's 6 Aug 2026 / 23 days — that figure is stale, superseded by the MOT pass logged 8 Jul 2026 at 80,705 mi, and it would show a red countdown for a renewal already done.

Thresholds per README §3.1: `Red` under 30 days, `Amber` under 60, otherwise `Ok`. Expired (negative) is `Red`, and the result carries the negative day count — "expired 12 days ago" is actionable in a way "0 days" is not.

The service returns the day count and a `RenewalUrgency` enum. It does **not** return colours. Mapping urgency to `--rust` / `#C79A22` / `--green` is the UI's job; a domain service that knows hex codes has the layering wrong.

### Check status

```
if (no logs)              -> NeverLogged
nextDue       = lastLog.PerformedOn.AddDays(interval_days)
daysRemaining = nextDue.DayNumber - referenceDate.DayNumber
if (daysRemaining < 0)                 -> Overdue
if (daysRemaining <= dueSoonWindow)    -> DueSoon
else                                   -> Ok

dueSoonWindow = Max(1, Ceiling(interval_days * 0.2))
```

`NeverLogged` is a **first-class fourth state, not an error and not a default.** The workbook has 18 check definitions but its Dashboard counts 17: *Spare tyre pressure* has never been logged and silently falls out of the OK/due-soon/overdue buckets. Collapsing it into any of the three reproduces that bug. `CheckStatusSummary` therefore carries four counts — `OkCount`, `DueSoonCount`, `OverdueCount`, `NeverLoggedCount` — and they must sum to 18.

The due-soon window scales with the interval because a fixed window is wrong at both ends: 7 days' notice on a 7-day check means it is always due soon, and on an annual check it is useless. 20% gives ~1.4 days on a weekly check and ~73 on an annual one. Only `is_active` definitions are counted.

### Budget

```
ytdActual = SUM(expense_entries.amount) WHERE category = X AND entry_date within period
remaining = annualBudget - ytdActual        -- negative when over
percentUsed = annualBudget == 0 ? null : ytdActual / annualBudget * 100
isOverBudget = ytdActual > annualBudget
```

Period is a parameter (`CalendarYear` | `Rolling12Months` | `SincePurchase`) per README §3.5, defaulting to `CalendarYear`. Guard division by zero: a category with a zero budget has no meaningful % used, and the answer is null.

A category with a budget but no spend returns zero actual, not absent. A category with spend but no budget appears with a null budget — unbudgeted spend must be visible, not filtered out.

### Result types

Records in `CarTracker.Shared`, shared by the API and MCP so both serialise identical shapes. Nullable where a figure is genuinely unavailable — no sentinel values, no `-1`, no `0` standing in for "unknown".

`VehicleSummary`, `MileageResult`, `FuelEconomySummary`, `FuelEntryMetrics`, `SpendSummary`, `RenewalSummary`, `CheckStatusSummary`, `BudgetSummary`.

### Testing

- xUnit. Calculator tests need no database — pure functions, hand-built fixtures.
- Every formula gets a hand-computed test with the arithmetic shown in a comment, so a failure distinguishes "the code is wrong" from "the expectation is wrong".
- Integration tests run the facade against the imported workbook at a fixed 2026-07-14 via a fake `TimeProvider`, asserting the four defect resolutions.
- Edge cases that must have tests: zero fills, one fill, zero miles since purchase, a check with no logs, a zero budget, non-monotonic mileage, a partial fill mid-history, and the first fill.

## External Dependencies (Conditional)

None. This spec adds no packages — `TimeProvider` is in the BCL, and xUnit and Testcontainers arrive with the core data model spec.
