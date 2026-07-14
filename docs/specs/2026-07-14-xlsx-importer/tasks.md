# Spec Tasks

## Tasks

- [ ] 1. Reader foundations and date handling
  - [ ] 1.1 Write the anchor test: Excel serial `46217` converts to `2026-07-14`
  - [ ] 1.2 Add ClosedXML and create `src/CarTracker.Data/Import` with a workbook-opening wrapper
  - [ ] 1.3 Implement `ExcelSerialDate.ToDateOnly` using the 1899-12-30 epoch, rejecting serials outside 2000–2050
  - [ ] 1.4 Implement the populated-date row filter and test it against a sheet with trailing formula rows
  - [ ] 1.5 Add typed cell accessors that return a parse failure rather than throwing, so one bad cell cannot abort a sheet
  - [ ] 1.6 Verify all tests pass

- [ ] 2. Import tracking and anomaly recording
  - [ ] 2.1 Write tests for the anomaly lifecycle constraint — `resolved_at` set iff status is terminal
  - [ ] 2.2 Add `ImportRun` and `DataAnomaly` entities with configurations per `sub-specs/database-schema.md`
  - [ ] 2.3 Generate the `AddImportTracking` migration
  - [ ] 2.4 Implement an anomaly recorder that collects flags during a run and writes them with the run id
  - [ ] 2.5 Implement the non-empty-database guard requiring `--force`
  - [ ] 2.6 Verify all tests pass

- [ ] 3. Sheet importers
  - [ ] 3.1 Write tests for each sheet importer against a small fixture workbook, not the real file
  - [ ] 3.2 Import Vehicle Info to `vehicles` with owned blocks, plus the manual 80,705 mileage reading
  - [ ] 3.3 Import the reference side-columns first: expense categories, garages, wash locations
  - [ ] 3.4 Import Fuel Log to `fuel_entries`, mirroring each fill to an `expense_entry` and a mileage reading
  - [ ] 3.5 Import Expenses Log, detecting and skipping the lumped fuel row — abort if not exactly one candidate
  - [ ] 3.6 Import Service History, DIY To-Do, Workshop To-Do, and Regular Checks (definitions plus logs)
  - [ ] 3.7 Import Wash Log, Tyre Log, Budget, Issues Watchlist, and Equipment
  - [ ] 3.8 Verify all tests pass

- [ ] 4. Anomaly detection and mileage generation
  - [ ] 4.1 Write tests: a non-monotonic reading produces exactly one `MileageNonMonotonic` Error
  - [ ] 4.2 Generate `mileage_readings` from every mileage-bearing log row, de-duplicating identical (date, mileage, origin) triples
  - [ ] 4.3 Implement monotonicity detection across the assembled readings
  - [ ] 4.4 Implement `FuelCostDiscrepancy` detection at the 2p threshold
  - [ ] 4.5 Implement `UnparseableValue` and `MissingReference` flagging
  - [ ] 4.6 Wrap the whole run in one transaction and wire the console summary
  - [ ] 4.7 Verify all tests pass

- [ ] 5. Dashboard validation harness
  - [ ] 5.1 Write a fixture reader for the Dashboard sheet — used only by tests, never by the importer
  - [ ] 5.2 Assert MOT expiry resolves to 8 Jul 2027, not the Dashboard's 6 Aug 2026
  - [ ] 5.3 Assert total litres resolves to 556.47, not the Dashboard's 1,112.94 — and that the ratio is exactly 2.0000
  - [ ] 5.4 Assert fuel YTD resolves to £888.86, not the Dashboard's £725.70, and that mirrored expenses account for the £163.16
  - [ ] 5.5 Assert current mileage resolves to 80,712, not the manual 80,705
  - [ ] 5.6 Assert the two expected anomalies exist: one `MileageNonMonotonic`, one `SupersededByMirror`
  - [ ] 5.7 Assert *Spare tyre pressure* exists as a definition with zero logs, giving 18 definitions where the Dashboard counted 17
  - [ ] 5.8 Run against the real workbook, record the baseline `FuelCostDiscrepancy` and `ImplausibleMpg` counts, and verify all tests pass
