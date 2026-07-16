# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-07-16-settings-reference-lists/spec.md

All routes sit behind the gateway on one origin and require `X-Api-Key` (DEC-009). The reference lists are
**global** (DEC-007: seeded once, shared by every vehicle), so these routes are *not* vehicle-scoped — unlike the
check-definition routes, which are. Rows are keyed by name, so `{name}` is the path segment (URL-encoded).

## New endpoints — garages

### GET /api/reference/garages

**Purpose:** List garages for the settings editor and the pick-lists, each with the count of records that
reference it — so the UI can show what a delete would strand before it is attempted.
**Response:** `GarageItem[]` — `{ Name, Contact, Address, Notes, ReferenceCount }`.

### POST /api/reference/garages

**Purpose:** Create a garage explicitly (the settings-side equivalent of `ReferenceWriter.EnsureGarageAsync`,
which only runs on first use of a name in a write).
**Parameters:** `{ Name, Contact?, Address?, Notes? }`.
**Response:** `201`. **Errors:** `409` if the name already exists (keyed by name); `400` on a blank name.

### PATCH /api/reference/garages/{name}

**Purpose:** Edit a garage's fields, or rename it. A rename cascades to every referencing column
(`ServiceRecord.Garage`, `MaintenanceTask.AssignedGarage`, `Vehicle.DefaultGarage`) in one transaction, because
the name is the foreign key — an un-cascaded rename breaks the reference.
**Parameters:** `{ Name?, Contact?, Address?, Notes? }` — every field optional; a new `Name` is a rename.
**Response:** `200`. **Errors:** `404`; `409` if the new name collides with an existing garage.

### DELETE /api/reference/garages/{name}

**Purpose:** Remove a garage, guarded. Blocked when records reference it, unless a re-home target is named.
**Parameters:** `?rehomeTo={name}` (optional) — re-point referencing records to this garage, then delete.
**Response:** `204`.
**Errors:** `409` with the reference count when the garage is referenced and no `rehomeTo` is given ("3 records
use this garage; re-home them first"); `404` if unknown; `400` if `rehomeTo` is the row being deleted or does not
exist.

## New endpoints — wash locations

`GET / POST / PATCH /{name} / DELETE /{name}` on `/api/reference/wash-locations`, exactly mirroring garages.
`WashLocationItem { Name, Notes, ReferenceCount }`; the only referencing column is `WashEntry.Location`. Same
delete guard, same rename cascade.

## Expense categories

### GET /api/reference/expense-categories (existing — extended)

Already returns `ExpenseCategoryItem { Name, IsMirrorOnly }` in display order. **Add** `IsSystem` and
`ReferenceCount` so the editor can lock system rows and show what a delete would strand. `IsMirrorOnly` (Fuel
only, sourced from `FuelEntryFactory.FuelCategory`) stays — it is a different question from `IsSystem` and the
add-expense sheet already depends on it.

### PATCH /api/reference/expense-categories/{name}

**Purpose:** Rename a category for display, or reorder it. **The Fuel category is rename-locked**: the mirror
resolves it by the exact constant `FuelEntryFactory.FuelCategory`, so a renamed Fuel would silently stop filing
fills — the £163.16 gap re-opened. Other system rows rename freely.
**Parameters:** `{ Name?, DisplayOrder? }`.
**Response:** `200`. **Errors:** `404`; `400` if the target is Fuel and `Name` differs; `409` on a name
collision.

### DELETE /api/reference/expense-categories/{name}

**Purpose:** Remove a non-system category, guarded like the others.
**Response:** `204`.
**Errors:** `400` if `IsSystem` (system categories are "seeded, undeletable"; Fuel is never even offered);
`409` with the count when `ExpenseEntry` rows reference it and no `rehomeTo` is given; `404` if unknown.

## Check definitions — no new endpoints

`ChecksEndpoints` already exposes `POST /definitions`, `PATCH /definitions/{id}` (name, cadence, interval,
guidance, order, `IsActive`) and `DELETE /definitions/{id}` (a real, log-cascading delete). The check-definition
settings panel drives the **existing** PATCH — retire is `IsActive = false`, guidance and order are field
edits. This spec adds no check endpoint; it adds the UI that was missing.
