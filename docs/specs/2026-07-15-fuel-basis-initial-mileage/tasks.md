# Spec Tasks

## Tasks

- [x] 1. Fill level becomes descriptive
  - [x] 1.1 Write a test asserting no type in `CarTracker.Domain` references `FillLevel` — IL-based, in the manner of `NoDirectClockAccessTests`, and verified to fail before the change lands
  - [x] 1.2 Make `FuelEntry.FillLevel` nullable and drop `IsRequired()` from its configuration
  - [x] 1.3 Remove the fill-level gate from `FuelEconomyCalculator`; drop `MpgUnreliableReason.PartialFill`
  - [x] 1.4 Delete the three `PartialFill` theory cases; update the fixture comment to note `FillLevel.Full` is now inert
  - [x] 1.5 Verify all tests pass

- [x] 2. Plausibility band
  - [x] 2.1 Write tests: a 5 L splash after 300 miles yields ~272 mpg with `IsPlausible = false`; all 12 real workbook intervals (25.4–32.2) pass
  - [x] 2.2 Write a test proving an implausible figure is excluded from average/best/worst but still present on its entry
  - [x] 2.3 Add `MinPlausibleMpg = 10` / `MaxPlausibleMpg = 70` and `IsPlausible` on `FuelEntryMetrics`
  - [x] 2.4 Write a test for a *plausible* figure from a mistyped odometer — the case the old fill-level gate could never catch
  - [x] 2.5 Verify all tests pass

- [x] 3. Cumulative average MPG
  - [x] 3.1 Write the test: the workbook fixture yields **29.19** (3,175 mi ÷ 494.47 L)
  - [x] 3.2 Write a test proving the first fill's litres are excluded from the burned total, with the arithmetic in a comment
  - [x] 3.3 Implement cumulative `AverageMpg`; expose `PerFillAverageMpg` alongside
  - [x] 3.4 Write a test asserting the two agree within 0.1 mpg on the fixture — a divergence is a real signal
  - [x] 3.5 Write tests for zero fills, one fill, and a zero span
  - [x] 3.6 Update `DashboardReproductionTests` — expected average moves from the per-fill mean to 29.19
  - [x] 3.7 Verify all tests pass

- [x] 4. Opening mileage reading
  - [x] 4.1 Write tests: creating a vehicle yields a current mileage equal to its purchase mileage, not null
  - [x] 4.2 Write a test asserting exactly one `Purchase` reading per vehicle, at the purchase date and mileage
  - [x] 4.3 Add `MileageOrigin.Purchase` and update the `CHECK` constraint
  - [x] 4.4 Add the vehicle-creation domain service writing both rows in one transaction
  - [x] 4.5 Write a test proving the two rows are written atomically — no vehicle without its opening reading
  - [x] 4.6 Verify all tests pass

- [x] 5. Migration and end-to-end verification
  - [x] 5.1 Generate the `FuelBasisAndInitialMileage` migration
  - [x] 5.2 Review the emitted SQL against `sub-specs/database-schema.md` — `fill_level` nullable, `origin` CHECK gains `'purchase'`
  - [x] 5.3 Write a Testcontainers test proving a NULL `fill_level` inserts and a `'purchase'` origin is accepted
  - [x] 5.4 Re-run the four defect assertions — they must be untouched by any of this
  - [x] 5.5 Verify the full suite passes and `dotnet ef database update` applies cleanly

## Notes

**Nothing here should move the four defect figures.** MOT 8 Jul 2027, litres 556.47, fuel YTD £888.86 and
mileage 80,712 are all independent of fill level and of the average-MPG method. If task 3 changes any of them,
something is wrong with the change, not with the assertion.

**Average MPG is the one Dashboard figure that moves**, from ~29.14 to 29.19. Both differ from the sheet's
28.78, which is dragged down by the invented first interval (see the derived-metrics spec, finding 2).

**Task 4 is the domain half of the add-car flow** (Phase 2, DEC-007). The UI calls this service; it does not
reimplement the rule.

## Outcome, 2026-07-15

**195 tests green** (154 domain, 41 data), zero warnings. The four defects re-assert unchanged, as predicted:
this change touched nothing they depend on.

Verified in psql after `dotnet ef database update`: `fill_level` is nullable, and the origin CHECK reads
`origin IN ('purchase', 'manual', 'fuel', 'tyre', 'wash', 'service')`.

**The guard was proven before it was trusted.** `NoFillLevelInCalculationsTests` failed against the old
calculator, naming `FillLevel` — then passed once the gate was gone. It also asserts the enum still exists, so
deleting it could not make the guard pass vacuously.

**Confirmed on the real history:** cumulative **29.19** against per-fill **29.14** — agreeing to 0.05 mpg,
which is the tank-level noise washing out across 3,175 miles, and is why the cumulative figure needs no fill
level. All 12 real intervals sit inside the 10–70 band (`ImplausibleCount = 0`), so the band is a genuine
anomaly signal rather than a nag.

**The plausibility band catches strictly more than the old gate.**
`A_mistyped_odometer_is_caught_even_though_nothing_was_said_about_the_tank` proves it: 80,900 fat-fingered as
89,000 gives 909 mpg with **both tanks marked Full** — the fill-level gate declared that reliable and folded it
into the average. The band rejects it.

**Deviations:**

- `IsReliable` was kept rather than removed. It now means "a figure exists" and is orthogonal to `IsPlausible`
  ("the figure makes sense"). Two questions the old design conflated; collapsing them again would lose the
  distinction between *no interval* and *a nonsense interval*.
- `ReliableIntervalCount` → `MeasuredIntervalCount`, since it no longer implies a quality judgement.
  `ImplausibleCount` added alongside.
- `VehicleFactory.CreateAsync` uses an explicit transaction and two saves, because `MileageReading` has a plain
  `VehicleId` with no navigation property, so the key must exist before the reading can reference it. Adding a
  navigation property would let EF fix it up in one save — but the explicit transaction makes the atomicity
  visible, which is the property the rule is about.
- The `IsPlausible` flag is computed here; **writing the `ImplausibleMpg` anomaly row remains core-data-model
  task 6**, which is still unbuilt.
