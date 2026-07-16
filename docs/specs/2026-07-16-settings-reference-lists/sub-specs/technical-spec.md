# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-settings-reference-lists/spec.md

## Technical Requirements

### What already exists, and what does not

- **Entities are all present.** `Garage` (`Name`, `Contact`, `Address`, `Notes`), `WashLocation` (`Name`,
  `Notes`) and `ExpenseCategory` (`Name`, `DisplayOrder`, `IsSystem`) exist in `src/CarTracker.Data/`.
  `ExpenseCategory.IsSystem` is true for all thirteen seeded categories and means "seeded, undeletable"; its
  comment names Fuel specifically, "which auto-mirroring depends on". **No new entity, no schema change** unless
  a genuinely missing column surfaces (e.g. a wash-location `Active` flag) — and the default is not to add one.
- **`ReferenceWriter` only creates.** `src/CarTracker.Domain/ReferenceWriter.cs` has `EnsureGarageAsync` and
  `EnsureWashLocationAsync` — existence checks that add on first use and, deliberately, do not normalise. This
  spec adds the *edit and remove* side; it does not change create-on-first-use, which CLAUDE.md confirms is the
  design ("created as used"), not a workaround.
- **Only one read endpoint exists.** `ReferenceEndpoints` exposes `GET /api/reference/expense-categories` and
  nothing else. Garages and wash locations have no read path at all. This spec extends that group.
- **Check-definition CRUD is already wired.** `ChecksEndpoints` has `POST /definitions`, `PATCH
  /definitions/{id}` (name, cadence, interval, guidance, order, `IsActive`) and `DELETE /definitions/{id}` (a
  real cascade). The PATCH docstring says exactly this. So the check half is **UI over an existing API**, not new
  endpoints — the settings panel drives the PATCH.

### The referential-integrity guard — the load-bearing part

- A reference row is pointed at by foreign keys that look like free text: garages by `ServiceRecord.Garage`,
  `MaintenanceTask.AssignedGarage`, `Vehicle.DefaultGarage`; wash locations by `WashEntry.Location`; categories
  by `ExpenseEntry.Category`. Deleting a referenced row must not orphan them.
- **Before delete, count the references** across every column that points at the row. Then either:
  - **Block** with `409` and the count, if any records reference it ("3 records use this garage; re-home them
    first") — the honest default, and what the design's `delStub` toast describes; or
  - **Re-home**, if the request names a replacement row — re-point the referencing records to it in one
    transaction, then remove the original.
- **Rename never re-points and never orphans.** Because these are natural keys (name), a rename must cascade to
  the referencing rows or the FK breaks — so rename is itself a re-home to a new name, run in one transaction.
  This is why "K & P Motors" → "K&P Motors" is a rename (one row, its records follow), not a merge (two rows
  folded); merging is out of scope.
- **System rows are locked against delete, not rename.** `ExpenseCategory.IsSystem` rows may be renamed for
  display (the mirror resolves Fuel by the constant `FuelEntryFactory.FuelCategory`, so even a renamed-for-
  display Fuel must keep the name the domain matches — treat Fuel as rename-locked too, or the mirror silently
  stops filing). The **Fuel category is never offered for delete** and the endpoint refuses it regardless of
  reference count, because losing it re-opens the £163.16 gap from the reference side.

### Endpoints

- Extend `ReferenceEndpoints` (`/api/reference`) with garages and wash locations: `GET` (list), `POST` (create —
  the settings-side equivalent of `EnsureGarageAsync`, but explicit), `PATCH /{name}` (edit fields / rename with
  cascade), `DELETE /{name}` (guarded). Categories gain `GET` (already returns them, keep the `IsMirrorOnly`
  flag) and `PATCH /{name}` (rename, with the system/Fuel locks); category delete is guarded like the others.
  See `sub-specs/api-spec.md`.
- Deletes and renames run inside `Database.CreateExecutionStrategy().ExecuteAsync(...)` when they touch more than
  one row (re-home cascades), because Aspire's `EnrichNpgsqlDbContext` installs a retrying strategy that refuses
  a user-initiated transaction — the same trap `ServiceRecordFactory` documents.

### Screens

- Settings already has `settings/StatutoryPanel.tsx` and `settings/CheckDefinitionsPanel.tsx`. Add reference-list
  panels (garages, wash locations, categories) in the same folder, each an editable list with add / rename /
  delete. The delete affordance shows the reference count and asks first; a system/Fuel row shows a lock, not a
  delete. `removeItem`'s "existing records keep their saved value" is the re-home-then-remove outcome made
  visible.
- The check-definitions panel gains the design's columns (Cadence / Days / Active / Order): Active is a toggle
  bound to `IsActive` (retire, not delete — the panel leads with it), guidance and order edit inline, each a
  `PATCH /checks/definitions/{id}`. Delete stays available but framed as the rare "should never have existed"
  case, because it cascades logs.
- Every new component axe-swept or exempted with a reason in `coverage.test.ts`; `usePlate()` supplies the plate
  where a panel is vehicle-scoped (checks are; the reference lists are global — DEC-007, seeded once, shared).

## Verification

- Write-path tests (Testcontainers, real Postgres, FK behaviour honoured — the in-memory provider ignores it):
  deleting a referenced garage is refused with the reference count; a re-home re-points the records and removes
  the row in one transaction; renaming cascades to referencing rows; deleting Fuel is refused unconditionally;
  a non-system category with no entries deletes.
- Check panel: retiring via `IsActive = false` drops the check from the active count and leaves its logs; the
  guidance/order PATCH round-trips.
- Front-end tests (Vitest + RTL + axe): the delete guard asks first and shows the count; a locked row shows a
  lock, not a delete; renaming updates the pick-list.
- The codegen staleness gate stays green: `npm run gen:api` then `git diff --exit-code`, since the new
  reference endpoints reach the OpenAPI contract.
- Live on BT53: rename a garage entered during dogfooding, try to delete one a service record references (refused
  with the count), confirm Fuel cannot be deleted, and retire a check to watch it drop from the 18.

## External Dependencies (Conditional)

None. Every entity, the check-definition PATCH, and the settings-panel scaffold exist; this is the edit/remove
half of `ReferenceWriter`'s lists plus the UI that drives PATCHes already in place.
