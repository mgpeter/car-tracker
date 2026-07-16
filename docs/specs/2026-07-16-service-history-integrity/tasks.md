# Spec Tasks

## Tasks

- [ ] 1. Service history API
  - [ ] 1.1 Write tests for `ServiceRecordFactory` — three rows in one transaction, inside an execution strategy
  - [ ] 1.2 `ServiceRecordFactory`: record + `MileageReading` (`Origin = Service`) + mirrored `ExpenseEntry` when a cost is given
  - [ ] 1.3 `ServiceEndpoints`: GET / POST / PATCH / DELETE, following `FuelEndpoints`; every write runs `AnomalyScanner`
  - [ ] 1.4 PATCH/DELETE re-run the scan and update or remove the shadows
  - [ ] 1.5 Regenerate the contract and TS types; verify the staleness gate is green
  - [ ] 1.6 Verify all tests pass

- [ ] 2. Anomaly read path
  - [ ] 2.1 Write tests for the loader change and the resolve rules
  - [ ] 2.2 `DataAnomaly` joins `VehicleMetricsData` / `VehicleMetricsLoader`
  - [ ] 2.3 `VehicleSummary` grows `Integrity { OpenCount, HighestSeverity }` — a headline, not the list
  - [ ] 2.4 `AnomalyEndpoints`: GET (open by default, `?status=all`) and PATCH to resolve; `ResolvedAt` from `TimeProvider`
  - [ ] 2.5 Assert the re-raise rule end to end: Corrected re-raises, Accepted/Dismissed stay down
  - [ ] 2.6 Verify all tests pass

- [ ] 3. Service history screen
  - [ ] 3.1 Write tests: the MOT row derives the countdown; a free-text type that is not exactly "MOT" derives nothing
  - [ ] 3.2 `ServiceHistoryPage` on `<DataTable>` (its fourth consumer) + add/edit sheet
  - [ ] 3.3 The MOT type is a choice, not typed — `Type` is free text and "MOT test" would fail silently
  - [ ] 3.4 Route, axe sweep, coverage-guard exemptions with reasons
  - [ ] 3.5 Verify all tests pass

- [ ] 4. Data integrity screen and dashboard panel
  - [ ] 4.1 Write tests: each flag shows the two figures that disagree; blue only; resolve clears it
  - [ ] 4.2 `DataIntegrityPage` — a list, not a table; grouped by status; resolve/dismiss with a note
  - [ ] 4.3 Dashboard integrity panel — renders nothing at all when there are no flags
  - [ ] 4.4 The garage card's `OpenAnomalyCount` becomes a link to the queue
  - [ ] 4.5 Verify all tests pass

- [ ] 5. Prove it end to end on BT53
  - [ ] 5.1 Enter the MOT pass (8 Jul 2026, 80,705 mi, next due 8 Jul 2027) through the UI
  - [ ] 5.2 Confirm the dashboard's MOT goes from "Not set" to 8 Jul 2027 · 359 days, and the settings seed row disappears
  - [ ] 5.3 Enter the 83,000 mi record; confirm exactly one flag, the odometer stays at 80,712, the row is kept
  - [ ] 5.4 Resolve it as Accepted with a note; confirm it clears from the dashboard without deleting the row
  - [ ] 5.5 Full suite, both builds, codegen gate; update roadmap and CLAUDE.md
