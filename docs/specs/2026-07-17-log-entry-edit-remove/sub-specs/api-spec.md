# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-07-17-log-entry-edit-remove/spec.md

All routes are vehicle-scoped under `/api/vehicles/{registration}/…`, `reg`→id resolved by
`VehicleLookup.FindIdAsync`. `PATCH` requests are all-nullable (`null` = leave untouched). Every write runs
inside the execution strategy and re-runs `AnomalyScanner`; a flag never blocks the save. `PATCH` returns the
updated item plus the flags raised; `DELETE` returns `204`.

## New endpoints

### PATCH /api/vehicles/{registration}/fuel/{id}

**Purpose:** Correct a fill. Re-derives its MPG/L-100km, updates the mirrored `MileageReading` (`Origin=Fuel`)
and mirrored `ExpenseEntry` (`Category=Fuel`, `FuelEntryId`) in the same transaction, and re-runs the scan —
correcting the litres that tripped an implausible-MPG flag clears it. Routed through
`FuelEntryFactory.UpdateAsync`.
**Parameters:** `UpdateFillRequest { EntryDate?, Mileage?, Litres?, PricePerLitre?, TotalCost?, Station?, FillLevel?, Notes? }`.
**Response:** `200` — `{ item, flags }`.
**Errors:** `404` unknown vehicle or fill; `400` validation (e.g. non-positive litres).

### PATCH /api/vehicles/{registration}/mileage/{id}

**Purpose:** Correct a reading. Re-runs the scan (a non-monotonic edit raises/clears a flag). Current mileage is
derived from the newest reading by date, so editing a non-latest reading does not move the odometer.
**Parameters:** `UpdateReadingRequest { ReadingDate?, Mileage?, Notes? }`.
**Response:** `200` — `{ item, flags }`.
**Errors:** `404` unknown vehicle or reading. Editing a reading whose `Origin` is not `Manual` (a fuel/service
shadow) is refused `409` — edit the source, as with mirrored expenses.

### DELETE /api/vehicles/{registration}/mileage/{id}

**Purpose:** Remove a manual reading; re-runs the scan. Deleting the latest falls current mileage back to the
prior reading.
**Response:** `204`.
**Errors:** `404` unknown; `409` if the reading is a fuel/service shadow (delete the source).

### PATCH /api/vehicles/{registration}/tyres/{id}

**Purpose:** Correct a tyre reading. When a mileage was supplied, its `MileageReading` (`Origin=Tyre`) is kept
in step and the scan re-runs.
**Parameters:** `UpdateTyreReadingRequest` — all-nullable mirror of `AddTyreReadingRequest`:
`{ ReadingDate?, Mileage?, PsiFrontLeft?, PsiFrontRight?, PsiRearLeft?, PsiRearRight?, PsiSpare?, TreadFrontLeft?, TreadFrontRight?, TreadRearLeft?, TreadRearRight?, Location?, Notes? }`
(per-corner pressures and tread depths — the tyre reading is one row across all five corners, not a row per corner).
**Response:** `200` — `{ item, flags }`.
**Errors:** `404` unknown vehicle or reading.

### DELETE /api/vehicles/{registration}/tyres/{id}

**Purpose:** Remove a tyre reading and its mileage shadow (if any); re-runs the scan.
**Response:** `204`. **Errors:** `404`.

### PATCH /api/vehicles/{registration}/washes/{id}

**Purpose:** Correct a wash. Resolves `Location` through `ReferenceWriter.EnsureWashLocationAsync` (FK-backed);
keeps its mileage shadow in step when present; re-runs the scan.
**Parameters:** `UpdateWashRequest { WashDate?, Location?, WashType?, Cost?, Mileage?, Notes? }`
(fields per the existing `AddWashRequest`).
**Response:** `200` — `{ item, flags }`. **Errors:** `404`.

### DELETE /api/vehicles/{registration}/washes/{id}

**Purpose:** Remove a wash and its mileage shadow (if any); re-runs the scan.
**Response:** `204`. **Errors:** `404`.

### DELETE /api/vehicles/{registration}/equipment/{id}

**Purpose:** Remove an equipment item. No shadows; no scan (equipment does not touch the anomaly surface).
Closes the CRUD gap — equipment already has `PATCH`.
**Response:** `204`. **Errors:** `404`.

## Changed endpoints

### PATCH /api/vehicles/{registration}/service/{id} · DELETE …/service/{id}

Unchanged in contract; **refactored** onto `ServiceRecordFactory.UpdateAsync`/`DeleteAsync` so the shadow and
execution-strategy invariants live beside `CreateAsync`. Behaviour is identical: shadows kept in step / removed,
scan re-run.

### PATCH /api/vehicles/{registration}/expenses/{id} · DELETE …/expenses/{id}

Two fixes, no contract change:
- **Mirror-refusal extended** — the existing `409 MirroredRow` now fires when
  `FuelEntryId is not null || ServiceRecordId is not null` (previously fuel only), so a service-mirrored row is
  refused for direct edit and delete, pointing at the service record.
- **Scan re-run added** — both handlers now run `AnomalyScanner` (previously only `POST` did). `DELETE`
  additionally removes the `Origin=Manual` `MileageReading` an expense's own mileage created, folding it into
  the cascade.
- Both handlers wrapped in the execution strategy.

### DELETE /api/vehicles/{registration}/fuel/{id}

Unchanged in contract; refactored onto `FuelEntryFactory.DeleteAsync` alongside the new `PATCH`.

## Not touched

- **Check logs, budget targets, vehicle DELETE** — out of scope per `spec.md`.
- **Tasks / issues `PATCH`/`DELETE`** — contract unchanged; only wrapped in the execution strategy (trap #3).
  Their terminal-status stamping (`CompletedDate`/`ResolvedDate` via `TimeProvider`) is already correct and is
  left as is.
