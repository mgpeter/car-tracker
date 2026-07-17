# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-17-log-entry-edit-remove/spec.md

## The rule-set (reused, not reinvented)

Every new `PATCH`/`DELETE` follows the shape `ServiceEndpoints`/`FuelEndpoints` already establish. None of
this is new invention — it is making four more logs obey the two that already do.

1. **Resolve `reg` → id via `VehicleLookup.FindIdAsync`** (`Endpoints/VehicleLookup.cs`); a miss is
   `VehicleLookup.NotFound(registration)` (404 ProblemDetails). A missing entity id after a valid vehicle is a
   plain 404.
2. **Write inside `Database.CreateExecutionStrategy().ExecuteAsync(...)`.** Aspire's `EnrichNpgsqlDbContext`
   installs a retrying strategy that refuses user-initiated transactions, so any handler that opens a
   transaction — which every multi-table edit does — must run inside the strategy. The three factories already
   do this for create; edit/delete must too. **The tests do not catch a missing wrapper** (the test context
   has no retry strategy), which is exactly how `VehicleFactory` passed 41 tests and threw on the first real
   request — see CLAUDE.md.
3. **Re-run `AnomalyScanner.ScanAsync(vehicleId, EntrySource.Web, ct)`** on every edit and delete. Editing a
   mileage down can clear a flag, editing it up can raise one; deleting a row can remove a flag's cause. A flag
   never blocks the save (§5.3) — the scan runs after the write, in the same transaction, and its result is
   returned, not thrown.
4. **Shadows cannot outlive their source.** A log that writes a `MileageReading` (fuel, service, tyres, wash)
   or a mirrored `ExpenseEntry` (fuel, service) updates those shadows on `PATCH` and removes them on `DELETE`.
   The existing shadow-matching convention is kept: the mileage reading is found by
   `Origin + ReadingDate + Mileage`, the mirrored expense by its source FK.
5. **`PATCH` DTOs keep the house convention** — every field nullable, `null` means "leave untouched, do not
   clear" (stated in `CheckDefinitionPatch`, `UpdateVehicleRequest`, `UpdateServiceRequest`). New `PATCH`
   requests (`UpdateFillRequest`, `UpdateReadingRequest`, `UpdateTyreReadingRequest`, `UpdateWashRequest`)
   follow it exactly.
6. **`ReferenceWriter` on edit.** A `PATCH` that changes a `Garage`/`AssignedGarage`/wash `Location` resolves
   it through `ReferenceWriter.Ensure*Async` — these are FK-backed columns that only look like free text and
   are created on first use. A raw string write is the bug DEC-008 left behind.

## Where update/delete logic lives — the structural decision

The domain factories expose `CreateAsync` only; today all edit/delete logic is inline in endpoints, and the
inline handlers re-implement the shadow bookkeeping by hand (fuel `DELETE` re-derives the matching reading by
`Origin+Date+Mileage`; service `PATCH` updates reading and expense inline). Adding four more logs of the same
would spread that bookkeeping across six handlers.

**Decision: `FuelEntryFactory` and `ServiceRecordFactory` grow `UpdateAsync`/`DeleteAsync`.** These two are the
only logs with real shadow complexity (a mileage reading *and* a mirrored expense, the expense conditional on
cost). Moving their edit/delete into the factory puts the mirror invariants and the execution-strategy wrapper
in one place, beside the `CreateAsync` that established them, and lets the endpoint handler shrink to
resolve → call factory → return flags. The simpler logs — **mileage, tyres, wash, equipment** — stay inline:
mileage and tyres/wash write at most a single mileage-reading shadow, equipment writes none, and a factory for
them would be ceremony. Fuel and service `DELETE`/`PATCH` handlers are refactored onto the new factory methods
as part of this work, so the pattern is not left half-applied.

## The three correctness fixes (in the blast radius)

1. **Expense mirror-refusal must also block service-mirrored rows.** `UpdateExpenseAsync` and
   `DeleteExpenseAsync` in `Endpoints/ExpenseEndpoints.cs` refuse with 409 (`MirroredRow`) only when
   `entry.FuelEntryId is not null`. A row carrying a `ServiceRecordId` — a service cost mirror — passes the
   guard and can be edited or deleted directly, silently desyncing it from its `ServiceRecord` (the reverse
   direction, service→expense, is already handled). Extend the guard to
   `FuelEntryId is not null || ServiceRecordId is not null`, and the refusal message points at the service
   record, mirroring the fuel wording.
2. **Expense `PATCH`/`DELETE` must re-run the scan.** `POST /expenses` runs `AnomalyScanner`; `PATCH`/`DELETE`
   do not — yet an expense can carry a mileage, and editing or removing it changes the mileage picture the
   detector reads. Add the same scan call the other write paths use. **While here:** an expense `POST` with a
   mileage spawns an `Origin=Manual` `MileageReading` that `DELETE` never removes — fold that reading into the
   delete cascade so the expense's own shadow dies with it, the same rule fuel and service already obey.
3. **Wrap multi-table edits in the execution strategy.** The existing `PATCH`/`DELETE` handlers (expense,
   service, task, issue, equipment) call a bare `context.SaveChangesAsync`, even service's, which touches three
   tables in one logical edit. Route every multi-table handler through the factory-style
   `CreateExecutionStrategy().ExecuteAsync(...)` wrapper so a transient retry cannot tear an edit in half. For
   fuel and service this falls out of the factory move (fix #above); for the inline ones it is added directly.

## Front-end

- **`<DataTable>` gains one prop: `onRowClick?(row: T)`** (`components/DataTable.tsx`). No actions column, no
  per-row control, no new column width to thread through every table — a clicked row opens the sheet. When
  `onRowClick` is set, rows become `role="button"`-style interactive (keyboard-activatable, focusable) and the
  cursor reflects it; when unset, the table is exactly as it is today.
- **Add-only sheets become dual add/edit** following the `TaskSheet`/`IssueSheet`/`EquipmentSheet` template
  already in the codebase: prop `entity: T | 'new' | null`, `const existing = entity !== 'new' && entity !== null ? entity : null`,
  fields seeded via `get(k, existing?.field ?? '')`, and the submit switches `existing === null` between
  `POST /…` and `PATCH /…/{id}`. Converts `AddFillSheet`, `AddExpenseSheet`, `AddReadingSheet`,
  `AddServiceSheet`, `AddTyreSheet`, `AddWashSheet` (each in its `screens/*` file).
- **A footer ghost Delete with a two-step inline confirm.** The precedent is `CheckDefinitionsPanel`'s footer
  Delete; this spec extracts it into a small reusable **`<ConfirmButton>`** (`components/`) so the two-state
  behaviour and its accessible labelling live in one place rather than being re-inlined per sheet. First
  activation swaps the label to a confirm that **names the cascade** ("Confirm — also removes the mirrored
  expense"); a blur or a second-elsewhere click resets it. No modal — the app deliberately avoids stacked
  dialogs over an open sheet.
- **Mirror-shadow expense rows are not editable in place.** On the expenses screen, a row with a `fuelEntryId`
  or `serviceRecordId` is not wired to `onRowClick` for editing; clicking it instead surfaces where its source
  lives ("this row mirrors a fill — edit the fill"), matching the endpoint's refusal so the UI never offers an
  action the API will 409.
- **Mutations.** Each screen's inline `useMutation`s gain an edit and a delete variant beside the existing add,
  each invalidating the keys the add path already invalidates: `['vehicle', reg, <resource>]`,
  `queryKeys.vehicleSummary(reg)`, and `queryKeys.garage`. No shared mutation layer is introduced — that is a
  larger refactor and out of scope; this spec matches the established per-screen shape.

## Tests & guards

- **API integration tests** run against real Postgres via Testcontainers, applying migrations — never the
  in-memory provider. Each new endpoint asserts: the edit re-runs the scan (a corrected litres clears an
  implausible-MPG flag); the delete cascades its shadow reading and expense; a service-mirrored expense is
  refused for direct edit and delete; and — via the reconcile prerequisite — deleting an Open flag's cause
  auto-resolves it to Corrected rather than leaving it open.
- **Execution-strategy regression.** At least one test exercises a multi-table edit through the strategy
  wrapper so the trap that "passed 41 tests and threw on the first real request" cannot recur silently. (The
  test context has no retry strategy, so this asserts the wrapper is *present*, not that it retries.)
- **Front-end.** Every converted sheet stays swept by `src/test/coverage.test.ts` (axe); dual-mode sheets keep
  their exemption reasons honest, and `<ConfirmButton>` earns its own axe coverage or an exemption. The
  `plate=\{reg\}` guard and greyscale properties are unaffected.
- **Codegen staleness gate stays green:** `npm run gen:api` then `git diff --exit-code` after the new DTOs land
  in the OpenAPI contract.

## Verification

- `dotnet test` — new integration tests green; the five-defect fixture untouched (no domain arithmetic added).
- `npm run test`, `npx tsc -b --force`, `npm run build` — all green; axe coverage guard passes.
- End to end against `dotnet run --project src/CarTracker.AppHost` (localhost:5080), by hand on BT53's real
  history, per the four Expected Deliverable outcomes in `spec.md`.

## External Dependencies

None. Every entity, factory, detector, FK and constraint this needs already exists — including
`expense_entries.service_record_id`, added by `2026-07-16-service-history-integrity`. This is wiring plus one
front-end prop and one small shared component, not acquisition.
