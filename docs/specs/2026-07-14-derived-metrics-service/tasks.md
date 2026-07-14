# Spec Tasks

## Tasks

- [ ] 1. Domain scaffolding, time, and result types
  - [ ] 1.1 Create `src/CarTracker.Domain` and `tests/CarTracker.Domain.Tests` targeting .NET 10
  - [ ] 1.2 Add the Europe/London date resolver over `TimeProvider`, and a test proving a BST-boundary instant resolves to the expected local date
  - [ ] 1.3 Add a lint or architecture test failing the build on any `DateTime.Now`/`UtcNow`/`Today` reference inside `CarTracker.Domain`
  - [ ] 1.4 Define result records in `CarTracker.Shared` with nullable figures and no sentinel values
  - [ ] 1.5 Define the exact constants: `LitresPerImperialGallon = 4.54609m`, `KmPerMile = 1.609344m`
  - [ ] 1.6 Verify all tests pass

- [ ] 2. Mileage calculator
  - [ ] 2.1 Write tests: most-recent-by-date wins over max; the 83,000 mi row yields 80,712 with `HasNonMonotonicHistory = true`
  - [ ] 2.2 Write tests for the tie-breaks — same date, different mileage; same date and mileage, different `created_at`
  - [ ] 2.3 Implement `MileageCalculator` returning `MileageResult`
  - [ ] 2.4 Implement miles-since-purchase, asserting 80,712 − 76,632 = 4,080
  - [ ] 2.5 Write tests for zero readings and a single reading
  - [ ] 2.6 Verify all tests pass

- [ ] 3. Fuel economy calculator
  - [ ] 3.1 Write hand-computed MPG and L/100km tests with the arithmetic shown in comments
  - [ ] 3.2 Write the property test: `mpg * litresPer100Km == 282.4809...` across every interval
  - [ ] 3.3 Implement per-fill metrics: miles since last, MPG, L/100km
  - [ ] 3.4 Implement reliability — `NoPreviousFill`, `PartialFill`, `NonMonotonicMileage` — and test each
  - [ ] 3.5 Write a test proving a partial fill's inflated MPG is excluded from best/worst
  - [ ] 3.6 Implement fleet aggregates over reliable intervals only; assert total litres = 556.47, not 1,112.94
  - [ ] 3.7 Implement volume-weighted average price per litre, and compare against the Dashboard's figure
  - [ ] 3.8 Verify all tests pass, and report the average-price-per-litre comparison for a decision

- [ ] 4. Spend, renewals, checks, and budget
  - [ ] 4.1 Write tests for spend grouping per §3.1, asserting fuel YTD = £888.86 from mirrored expenses
  - [ ] 4.2 Implement `SpendCalculator` with cost-per-mile, monthly average, and both purchase-inclusive and purchase-excluding totals; guard zero miles
  - [ ] 4.3 Write tests for MOT derivation: 8 Jul 2027 and 359 days at the 2026-07-14 reference date, not 6 Aug 2026 / 23 days
  - [ ] 4.4 Implement `RenewalCalculator` with the seed fallback and the 30/60-day urgency thresholds, returning urgency not colour
  - [ ] 4.5 Write tests for all four check states, asserting the counts sum to 18 with exactly one `NeverLogged`
  - [ ] 4.6 Implement `CheckStatusCalculator` with the scaled due-soon window
  - [ ] 4.7 Implement `BudgetCalculator` with the three periods, guarding a zero budget and surfacing unbudgeted spend
  - [ ] 4.8 Verify all tests pass

- [ ] 5. Facade and workbook validation
  - [ ] 5.1 Write the query layer loading each calculator's inputs by vehicle id
  - [ ] 5.2 Implement `IDerivedMetricsService` composing the calculators, and register it in DI
  - [ ] 5.3 Write an integration test running the facade against the imported workbook with a fake `TimeProvider` fixed at 2026-07-14
  - [ ] 5.4 Assert all four defect resolutions together: MOT 8 Jul 2027, litres 556.47, fuel YTD £888.86, mileage 80,712
  - [ ] 5.5 Assert every Dashboard figure the sheet got right is reproduced; investigate and report any further mismatch
  - [ ] 5.6 Write tests for the edge cases: zero fills, one fill, zero miles since purchase, zero budget
  - [ ] 5.7 Verify all tests pass
