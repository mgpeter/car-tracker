# Spec Tasks

## Tasks

- [ ] 1. Fill level becomes descriptive
  - [ ] 1.1 Write a test asserting no type in `CarTracker.Domain` references `FillLevel` — IL-based, in the manner of `NoDirectClockAccessTests`, and verified to fail before the change lands
  - [ ] 1.2 Make `FuelEntry.FillLevel` nullable and drop `IsRequired()` from its configuration
  - [ ] 1.3 Remove the fill-level gate from `FuelEconomyCalculator`; drop `MpgUnreliableReason.PartialFill`
  - [ ] 1.4 Delete the three `PartialFill` theory cases; update the fixture comment to note `FillLevel.Full` is now inert
  - [ ] 1.5 Verify all tests pass

- [ ] 2. Plausibility band
  - [ ] 2.1 Write tests: a 5 L splash after 300 miles yields ~272 mpg with `IsPlausible = false`; all 12 real workbook intervals (25.4–32.2) pass
  - [ ] 2.2 Write a test proving an implausible figure is excluded from average/best/worst but still present on its entry
  - [ ] 2.3 Add `MinPlausibleMpg = 10` / `MaxPlausibleMpg = 70` and `IsPlausible` on `FuelEntryMetrics`
  - [ ] 2.4 Write a test for a *plausible* figure from a mistyped odometer — the case the old fill-level gate could never catch
  - [ ] 2.5 Verify all tests pass

- [ ] 3. Cumulative average MPG
  - [ ] 3.1 Write the test: the workbook fixture yields **29.19** (3,175 mi ÷ 494.47 L)
  - [ ] 3.2 Write a test proving the first fill's litres are excluded from the burned total, with the arithmetic in a comment
  - [ ] 3.3 Implement cumulative `AverageMpg`; expose `PerFillAverageMpg` alongside
  - [ ] 3.4 Write a test asserting the two agree within 0.1 mpg on the fixture — a divergence is a real signal
  - [ ] 3.5 Write tests for zero fills, one fill, and a zero span
  - [ ] 3.6 Update `DashboardReproductionTests` — expected average moves from the per-fill mean to 29.19
  - [ ] 3.7 Verify all tests pass

- [ ] 4. Opening mileage reading
  - [ ] 4.1 Write tests: creating a vehicle yields a current mileage equal to its purchase mileage, not null
  - [ ] 4.2 Write a test asserting exactly one `Purchase` reading per vehicle, at the purchase date and mileage
  - [ ] 4.3 Add `MileageOrigin.Purchase` and update the `CHECK` constraint
  - [ ] 4.4 Add the vehicle-creation domain service writing both rows in one transaction
  - [ ] 4.5 Write a test proving the two rows are written atomically — no vehicle without its opening reading
  - [ ] 4.6 Verify all tests pass

- [ ] 5. Migration and end-to-end verification
  - [ ] 5.1 Generate the `FuelBasisAndInitialMileage` migration
  - [ ] 5.2 Review the emitted SQL against `sub-specs/database-schema.md` — `fill_level` nullable, `origin` CHECK gains `'purchase'`
  - [ ] 5.3 Write a Testcontainers test proving a NULL `fill_level` inserts and a `'purchase'` origin is accepted
  - [ ] 5.4 Re-run the four defect assertions — they must be untouched by any of this
  - [ ] 5.5 Verify the full suite passes and `dotnet ef database update` applies cleanly

## Notes

**Nothing here should move the four defect figures.** MOT 8 Jul 2027, litres 556.47, fuel YTD £888.86 and
mileage 80,712 are all independent of fill level and of the average-MPG method. If task 3 changes any of them,
something is wrong with the change, not with the assertion.

**Average MPG is the one Dashboard figure that moves**, from ~29.14 to 29.19. Both differ from the sheet's
28.78, which is dragged down by the invented first interval (see the derived-metrics spec, finding 2).

**Task 4 is the domain half of the add-car flow** (Phase 2, DEC-007). The UI calls this service; it does not
reimplement the rule.
