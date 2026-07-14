# Spec Tasks

## Tasks

- [x] 1. Domain scaffolding, time, and result types
  - [x] 1.1 Create `src/CarTracker.Domain` and `tests/CarTracker.Domain.Tests` targeting .NET 10
  - [x] 1.2 Add the Europe/London date resolver over `TimeProvider`, and a test proving a BST-boundary instant resolves to the expected local date
  - [x] 1.3 Add a lint or architecture test failing the build on any `DateTime.Now`/`UtcNow`/`Today` reference inside `CarTracker.Domain`
  - [x] 1.4 Define result records in `CarTracker.Shared` with nullable figures and no sentinel values
  - [x] 1.5 Define the exact constants: `LitresPerImperialGallon = 4.54609m`, `KmPerMile = 1.609344m`
  - [x] 1.6 Verify all tests pass — 36 domain tests green (68 across the solution)

  **Notes, 2026-07-14:**
  - `Clock` wraps `TimeProvider` and is the single point where the domain may ask what day it is. Ten tests cover both 2026 BST transitions; the load-bearing one is that 23:30 UTC on 13 July is already 14 July in London — computing in UTC would put every countdown off by one for the last hour of every summer day.
  - `NoDirectClockAccessTests` reads the **compiled IL**, not the source text: a grep over `.cs` is defeated by an alias or a using-static, and what is actually called is what matters. **Verified to fail**: a temporary `DateTime.UtcNow` in the domain was caught and named. A guard that has never fired is worth nothing.
  - `ResultTypeTests` enforces the no-sentinel rule by reflection over the 17 genuinely-optional figures, rather than leaving it as prose that erodes.
  - `Units.MpgTimesLitresPer100Km` (≈282.4809) is defined here so task 3.2's property test has something to assert against.
  - The worked-example test caught a mistake in **my own arithmetic**, not the code's (29.97, not 29.98). Exactly why the spec asks for the working in a comment.

- [x] 2. Mileage calculator
  - [x] 2.1 Write tests: most-recent-by-date wins over max; the 83,000 mi row yields 80,712 with `HasNonMonotonicHistory = true`
  - [x] 2.2 Write tests for the tie-breaks — same date, different mileage; same date and mileage, different `created_at`
  - [x] 2.3 Implement `MileageCalculator` returning `MileageResult`
  - [x] 2.4 Implement miles-since-purchase, asserting 80,712 − 76,632 = 4,080
  - [x] 2.5 Write tests for zero readings and a single reading
  - [x] 2.6 Verify all tests pass — 10 tests

  Note: a reading below purchase mileage yields a **negative** miles-since-purchase rather than a clamped zero. It is physically impossible, therefore bad data, and clamping would hide it.

- [x] 3. Fuel economy calculator
  - [x] 3.1 Write hand-computed MPG and L/100km tests with the arithmetic shown in comments
  - [x] 3.2 Write the property test: `mpg * litresPer100Km == 282.4809...` across every interval
  - [x] 3.3 Implement per-fill metrics: miles since last, MPG, L/100km
  - [x] 3.4 Implement reliability — `NoPreviousFill`, `PartialFill`, `NonMonotonicMileage` — and test each
  - [x] 3.5 Write a test proving a partial fill's inflated MPG is excluded from best/worst
  - [x] 3.6 Implement fleet aggregates over reliable intervals only; the doubling test asserts a plain sum (the real 556.47 assertion needs the workbook fixture — task 5)
  - [x] 3.7 Implement volume-weighted average price per litre
  - [x] 3.8 Verify all tests pass — 16 tests. **The average-price comparison against the Dashboard is deferred to task 5**, which is where the real figures arrive; the unit test proves the formula is volume-weighted (£1.433, not £1.50).

  **A partial fill invalidates two intervals, not one.** The one ending at it (litres added ≠ fuel burned) and the one starting from it (the tank was not full at the start, so those litres cover part of the previous leg too). My first draft of the test expected two reliable intervals from four fills with one partial; the code was right and the expectation was wrong. Worth knowing before reading `ReliableIntervalCount` against the real 13 fills.

- [x] 4. Spend, renewals, checks, and budget
  - [x] 4.1 Write tests for spend grouping per §3.1; fuel YTD comes from mirrored expense rows (the £888.86 assertion needs the workbook fixture — task 5)
  - [x] 4.2 Implement `SpendCalculator` with cost-per-mile, monthly average, and both purchase-inclusive and purchase-excluding totals; guard zero miles
  - [x] 4.3 Write tests for MOT derivation: **8 Jul 2027 and 359 days** at the 2026-07-14 reference date, not 6 Aug 2026 / 23 days
  - [x] 4.4 Implement `RenewalCalculator` with the seed fallback and the 30/60-day urgency thresholds, returning urgency not colour
  - [x] 4.5 Write tests for all four check states, asserting the counts sum to 18 with exactly one `NeverLogged`
  - [x] 4.6 Implement `CheckStatusCalculator` with the scaled due-soon window
  - [x] 4.7 Implement `BudgetCalculator` with the three periods, guarding a zero budget and surfacing unbudgeted spend
  - [x] 4.8 Verify all tests pass — 114 domain tests, 146 across the solution

  **Notes, 2026-07-14:**
  - The MOT test asserts both directions: derived gives 8 Jul 2027 / 359 days / Ok, and the seed-only path reproduces the sheet's stale 6 Aug 2026 / 23 days / Red — proving the fallback works *and* that it is not what the derived path returns.
  - Threshold boundaries are pinned by theory: exactly 30 days is Amber (not Red), exactly 60 is Ok. "Under 30" and "under 60" are exclusive.
  - A missing expiry yields **null urgency**, not Red. Unknown is not due; defaulting to Red would cry wolf on every unconfigured field.
  - An MOT record's next-due does not leak into the next-service countdown — an MOT is not a service.
  - `BudgetCalculator` throws on an unknown period rather than silently returning an empty summary.

- [ ] 5. Facade and workbook validation
  - [ ] 5.1 Write the query layer loading each calculator's inputs by vehicle id
  - [ ] 5.2 Implement `IDerivedMetricsService` composing the calculators, and register it in DI
  - [ ] 5.3 Transcribe the real workbook rows into a C# fixture (DEC-008), citing source sheet and row per block; check the transcription against `archive/…Freelander_BT53AKJ_Tracker.xlsx` a second time before relying on it
  - [ ] 5.4 Write an integration test running the facade against that fixture with a fake `TimeProvider` fixed at 2026-07-14, asserting all four defect resolutions together: MOT 8 Jul 2027, litres 556.47, fuel YTD £888.86, mileage 80,712
  - [ ] 5.5 Assert every Dashboard figure the sheet got right is reproduced; investigate and report any further mismatch
  - [ ] 5.6 Write tests for the edge cases: zero fills, one fill, zero miles since purchase, zero budget
  - [ ] 5.7 Verify all tests pass
