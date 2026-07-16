# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-service-history-integrity/spec.md

## Technical Requirements

### The shared brain

- `DataAnomaly` joins `VehicleMetricsData` and `VehicleMetricsLoader`. The loader already counts open flags
  (`CountOpenAnomaliesAsync`, for the garage card) but never loads one, so the app can say how many flags a
  vehicle has and nothing about what any of them is. The count is not the gap; the content is.
- `VehicleSummary` grows an **open-flag count only**, not the flags themselves. The dashboard's panel needs a
  handful; the queue needs all of them with their detail, and putting the full list on the summary would make
  every screen that reads a headline figure pay for it. The queue has its own endpoint.
- **No new calculator.** `AnomalyDetector` and `AnomalyScanner` already exist and are tested; this spec gives
  them a reader, not a rewrite.
- Service records already reach the domain — `RenewalCalculator` reads them for the MOT and next-service dates,
  and `SpendCalculator` does not (a service record's cost is mirrored into expenses, like a fill).
- **`ServiceRecord.Type` is free text**, deliberately: it holds the workbook's varied descriptions, and the MOT
  derivation matches on `Type = "MOT"`. The add sheet must therefore offer MOT as a *choice* rather than trust
  it to be typed — a record whose type reads "MOT test" or "mot" derives no expiry at all, and the failure is
  silent.

### Service history

- `ServiceEndpoints` follows `FuelEndpoints` exactly: resolve reg → id via `VehicleLookup`, write through a
  factory inside `Database.CreateExecutionStrategy().ExecuteAsync(...)`, run `AnomalyScanner`, return the flags
  raised. A flag never blocks the save (§5.3).
- **Each record writes a `MileageReading` with `Origin = Service`** in the same transaction. That is what makes
  the 83,000 row reach the mileage log at all, and it is why the detector fires.
- **Each record with a cost mirrors into expenses**, as fills do, and the mirrored row is refused for edit at
  the expense endpoint, which already enforces that for fuel. This needs `expense_entries.service_record_id`,
  which does not exist — see `database-schema.md`, added during execution because the spec checked that both
  entities existed and not that anything could point at them. `SpendCalculator` reads expenses only, so without
  it a £603.99 cambelt would move no figure anywhere.
- The MOT record's `NextDueDate` is what `RenewalCalculator` reads. The add sheet must make that obvious: it is
  the field that ends "MOT · Not set", and it is *not* a copy of the certificate's expiry typed twice.
- `PATCH`/`DELETE` must re-run the scan, because editing a mileage down can clear an anomaly and editing one up
  can raise one. Deleting a record deletes its mirrored reading and expense: the shadow cannot outlive its
  source.

### Data integrity

- `GET /anomalies` returns open flags by default; `?status=all` includes resolved ones. The queue's default view
  is work to do, not history.
- Each flag renders **the two figures that disagree**, not a message string. `AnomalyDetector` already stores
  `Detail`; the screen must show the comparison (`83,000 mi on 27 Jun` → `current 80,712`) because "mileage not
  monotonic" alone tells the reader nothing they can act on.
- **Blue only.** Integrity is its own axis (`lib/status.ts`): `<IntegrityPill>`, never `<Pill tone="due">`.
  Severity orders the queue; it does not become green/amber/rust, and the design's DETECTORS panel putting
  "Check never logged" here is the conflation this codebase already rejected.
- Resolving is `PATCH /anomalies/{id}` with `{ status, resolutionNote }`. The check constraint
  `ck_anomalies_resolved_iff_terminal` requires `ResolvedAt` to be set exactly when the status is terminal —
  the endpoint sets it from `TimeProvider`, never the caller.
- **Corrected re-raises; Accepted and Dismissed do not.** This is already in `AnomalyDetector`'s re-raise rule
  and must not be re-implemented in the endpoint.

### Screens

- Service history uses `<DataTable>` — its fourth consumer. If a fourth reveals that the `Column` shape is
  wrong, fix the shape; do not fork the table.
- The integrity queue is a **list, not a table**: each flag is a paragraph with a comparison and two actions.
  Checks stayed a list for the same reason, and forcing a table on prose is what the seam exists to avoid.
- The dashboard's integrity panel shows open flags with a link to the queue, and renders nothing at all when
  there are none — an empty panel headed "Data integrity" implies a question was asked and answered, which is
  different from there being nothing to say.
- Every new component is swept by axe or exempted with a reason in `coverage.test.ts`; the guard fails the build
  otherwise.

### Verification

- The five defects' fixture is untouched: this spec adds no domain arithmetic.
- End to end against `dotnet run --project src/CarTracker.AppHost`, entering BT53's real records by hand —
  the MOT pass and the 83,000 row are the two that matter, and both come from the workbook's Service History
  sheet.
- The codegen staleness gate must stay green: `npm run gen:api` then `git diff --exit-code`.

## External Dependencies (Conditional)

None. Every entity, detector and component this spec needs already exists; it is wiring plus one nullable
foreign key, not acquisition.
