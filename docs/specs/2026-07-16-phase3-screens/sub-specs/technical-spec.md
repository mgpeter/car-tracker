# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-phase3-screens/spec.md

Recorded after implementation. Filenames and behaviours below are what shipped, not a plan.

## What was built

### API

- **`TaskEndpoints`** — GET/POST/PATCH/DELETE over `MaintenanceTask`. GET returns `TaskLog` with the derived
  `BundleCost` (open Workshop estimates), `BundleCount`, and `OpenEstimateTotal`. Status→Done stamps
  `CompletedDate`; moving off Done clears it.
- **`IssueEndpoints`** — GET/POST/PATCH/DELETE over `Issue`. GET returns `IssueLog` with derived
  `WorstCaseCost` (sum of what is still monitored). Status→Resolved stamps `ResolvedDate`.
- **`LogEndpoints`** — one file for tyres, washes and equipment, because they are one shape (vehicle-scoped list
  + create) and three files would be three copies. Tyres and washes write a `MileageReading` when an odometer
  is given. Equipment also has PATCH.
- **`ReferenceEndpoints`** — `GET /api/reference/expense-categories`, added to fix a shipped bug: the expenses
  sheet had hardcoded category names ("Repairs", "Road tax", …) that the endpoint's own validation rejected.
  The list is data; the front-end reads it. `IsMirrorOnly` comes from `FuelEntryFactory.FuelCategory`, not
  `ExpenseCategory.IsSystem` (which is true for all thirteen).
- **`GET /api/vehicles/{reg}`** (`VehicleDetail`) — the stored specs, which had no path to a screen. Uses
  `VehicleLookup.FindAsync` (tracked, unlike the id-only lookup, since a caller may PATCH).
- **Fuel `DELETE`** — an expense and a service record could both be deleted; a fill could not, which made a
  mistyped fill permanent. Deletes the fill and its shadows (mileage reading, mirrored expense) and re-scans.

### Shared reference-writing

- **`ReferenceWriter`** — `EnsureGarageAsync` / `EnsureWashLocationAsync`, creating a reference-list row on
  first use. `ServiceRecord.Garage`, `MaintenanceTask.AssignedGarage`, `Vehicle.DefaultGarage` and
  `WashEntry.Location` are all **foreign keys to keyed tables**, not the free text they look like — their
  comments said "upserted by the importer", and DEC-008 deleted the importer, so a new name was a 500. This
  generalises the fix rather than repeating it per write path. It deliberately does not normalise names:
  merging "K & P Motors" with "K&P Motors" is a reference-list-editor decision, not a write-path one.

### Screens

- Seven pages under `src/screens/`, each an `AppShell` + `PageHead` + derived panels, add/edit sheets from the
  `Sheet` family, tables via `<DataTable>` where there are columns to align (tyres, wash) and lists where there
  are not (issues, equipment). Status/severity/kind enums are `Record<Wire, …>` off the generated types, so a
  new member fails the build rather than rendering a raw enum name.
- **`usePlate()`** — the registration for the plate comes from the vehicle summary, never the URL slug. This
  fixed a bug that shipped twelve times: `plate={reg}` renders "BT53AKJ" because the route param is normalised
  for matching. `coverage.test.ts` now fails the build on `plate={reg}`.

## Things this session's build got wrong, then fixed

Recorded because the pattern is the lesson, not the individual bugs — every one was a hardcoded guess where the
source should have been read, and each is now sourced so the build breaks instead of the page lying:

- **Expense categories** hand-typed from the workbook's wording; 8 of 12 options 400'd on save. → reference
  endpoint.
- **`MileageOrigin`** guessed, missing `Tyre`/`Wash`/`Purchase`; the founding reading rendered a raw enum name.
  → `Record<Origin, string>` off the wire type.
- **`Garage`/`WashLocation` FK trap** — a 500 the first time a new name was typed. → `ReferenceWriter`.
- **The plate slug** — twelve times. → `usePlate()` + build guard.

## External Dependencies (Conditional)

None. Every entity already existed; this was endpoints, screens and one nullable FK
(`expense_entries.service_record_id`, which belongs to the service-history spec, not this one).
