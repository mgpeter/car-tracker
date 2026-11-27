# Spec Tasks

These are the tasks to be completed for the spec detailed in
@docs/specs/2026-11-27-clear-vehicle-fields/spec.md

> Domain first, then contract, then screen. The rule (clear after merge, conflict is a 400, a missing owned block
> stays missing) lives in `VehicleUpdateService` and is proved against real PostgreSQL before anything else
> reads it, because there is no `CarTracker.WebApi.Tests` project. The contract regenerates only once the C#
> types exist. The screen comes last because it types `clears` against the generated `VehicleField`.
>
> The enumerated tests in 1.1 and 3.1 are the ones that matter: one proves every `VehicleField` member clears
> its own column and nothing else, the other that every member is reachable from exactly one editor field. A
> new member that misses either fails a test.

## Tasks

- [x] 1. **Clear list and conflict rule in the domain**
  - [x] 1.1 Write integration tests against real PostgreSQL (Testcontainers) in
        `tests/CarTracker.Data.Tests/VehicleUpdateServiceTests.cs`, with `ReferenceWriter` pinned by
        `TestOwner.As(_ownerId)` as the file already does:
        clearing `FluidsOilSpec` nulls the column and leaves the other eleven fluids as stored (after a reload);
        every `Enum.GetValues<VehicleField>()` member clears exactly its own column on a fully populated vehicle
        and nothing else;
        set-and-clear on `FluidsOilSpec` returns `Validation` keyed `oilSpec` with "Engine oil cannot be both
        set and cleared." and the row is unchanged after a reload;
        clearing `PurchasePrice` removes the `IsVehiclePurchase` expense in the same save, and the returned
        summary's `TotalSincePurchase` equals `TotalSincePurchaseExcludingPurchase` (the dashboard's "car
        itself" clause, deliverable 2);
        clearing an already-null field succeeds and changes nothing;
        a duplicate entry in `Clear` is not an error;
        clearing an insurance or breakdown field on a vehicle with neither block does not create an empty
        `InsurancePolicy` or `BreakdownCover`;
        clearing `DefaultGarage` nulls the column and leaves the garage reference row in place;
        a patch with `Clear = null` behaves as before (regression for the MCP settings tools).
        Watch them fail for the missing type and handler, not for a fixture problem. (assumed: A7)
  - [x] 1.2 Bump `VERSION` minor, 0.29.3 to 0.30.0 (`./scripts/release.ps1 -Minor` or by hand), in this task's
        commit
  - [x] 1.3 Add `VehicleField` to `src/CarTracker.Shared/Logs/VehicleDtos.cs` with the 41 block-prefixed members
        the technical spec lists (no `Status`, no `IsDefault`), and append
        `IReadOnlyList<VehicleField>? Clear = null` last on `VehiclePatch`. Rewrite the `VehiclePatch`,
        `BreakdownPatch` and `FluidsPatch` doc comments that say an omitted field cannot be cleared, so they
        point at `Clear`
  - [x] 1.4 In `VehicleUpdateService.ApplyAsync`, after the existing validations and before any write: for each
        `Clear` entry whose field carries a non-null value in the patch, return
        `WriteResult.Invalid(<form key>, "<Label> cannot be both set and cleared.")`. One table per member holds
        the form key and label (assumed: A3)
  - [x] 1.5 Apply clears after the merge and before `purchaseMirror.SyncAsync`, using a `switch` or a
        `Dictionary<VehicleField, Action<Vehicle>>`; skip the insurance and breakdown members when the owned
        block is null. Replace the comment saying a price cannot be cleared through PATCH, and the stale
        "DefaultGarage is a foreign key" comment (DEC-018 removed the FK) (assumed: A2)
  - [x] 1.6 Verify all tests pass (`dotnet test`, baseline 811/817 with 6 skipped)

- [x] 2. **`clear` on the PATCH contract and the generated client**
  - [x] 2.1 Write unit tests in `src/CarTracker.WebApp/src/api/contract.test.ts` over `api-contract/v1.json`:
        `UpdateVehicleRequest.clear` is an optional array of `VehicleField`; `VehicleField` is a string enum
        containing `FluidsOilSpec` and `PurchasePrice` and not `Status` or `IsDefault`; plus a type assertion
        that `components['schemas']['UpdateVehicleRequest']['clear']` accepts `['FluidsOilSpec']`
        (assumed: A4)
  - [x] 2.2 Append `IReadOnlyList<VehicleField>? Clear = null` last on `UpdateVehicleRequest` in
        `src/CarTracker.WebApi/Endpoints/VehicleEndpoints.cs`, with a doc comment saying `null` elsewhere still
        means unchanged; update the `UpdateVehicleAsync` remarks to name the `clear` list
  - [x] 2.3 Pass `request.Clear` through in `UpdateVehicleAsync`, switching the `VehiclePatch` construction to
        named arguments so a dropped `Clear:` shows in review (assumed: A4)
  - [x] 2.4 `dotnet build` to rewrite `api-contract/v1.json`, then `npm run gen:api` to regenerate
        `src/api/generated/schema.d.ts`; confirm both diffs are additive (one new schema, one new property)
  - [x] 2.5 Manual check against the running AppHost: a PATCH that sets and clears `fluids.oilSpec` answers 400
        keyed `oilSpec` and writes nothing; `{"clear":["FluidsOilSpec"]}` nulls the field (assumed: A4, A8)
  - [x] 2.6 Verify all tests pass (`dotnet test`, vitest with `NODE_OPTIONS=--no-webstorage`, `npx tsc -b`)

- [ ] 3. **Vehicle screen editors send clears**
  - [ ] 3.1 Write component tests in `src/CarTracker.WebApp/src/screens/VehicleInfoPage.test.tsx` that assert the
        request body, not the DOM:
        replace "sends null for a blank, which the server merge leaves unchanged" with: blanking the preloaded
        "Engine oil" sends `clear: ['FluidsOilSpec']` and no `oilSpec` key in `fluids` (deliverable 1);
        an untouched sheet sends no `clear` key;
        a field that was empty and stays empty (Brake fluid) is neither sent nor cleared;
        blanking "Price £" on the Purchase sheet sends `clear: ['PurchasePrice']` at the root (deliverable 2);
        choosing "not recorded" on a recorded ULEZ value sends `clear: ['UlezCompliant']`;
        clearing every set field of a block leaves that block key out of the body (assumed: A6);
        a 400 keyed `oilSpec` renders on the Engine oil input through `reportApiError` (deliverable 3);
        every `VehicleField` member is the `clears` of exactly one editor field, using a
        `Record<VehicleField, true>` in the test (assumed: A5);
        hints read "leave blank to clear it" and the footer no longer says a blank leaves the stored value
        (assumed: A2)
  - [ ] 3.2 Add `clears: VehicleField` to `FieldSpec` in `src/screens/vehicle/VehicleEditSheet.tsx`, typed from
        `components['schemas']['VehicleField']`, and set it on all 41 fields across the nine editors
  - [ ] 3.3 Rewrite `submit`: a field whose stored value was non-empty and is now blank is omitted from the body
        and its `clears` goes into a root-level `clear`; empty-and-still-empty is omitted from both; everything
        else sends its coerced value as now; `clear` only when non-empty; an empty block is left out
        (assumed: A6). Update the `coerce` doc comment
  - [ ] 3.4 Replace `BLANK_LEAVES_STORED` in `src/api/vehicle.ts` with `BLANK_CLEARS = 'leave blank to clear it'`,
        rewrite its doc comment to describe the `clear` list, and remove the old constant and its import
  - [ ] 3.5 Update the user-visible copy and comments that still promise a blank keeps the value: the
        `VehicleInfoPage.tsx` footer, `VehicleLifecyclePanel.tsx:108`, `DocumentDtos.cs:54` (assumed: A2)
  - [ ] 3.6 Docs: remove the "blank field in a vehicle PATCH" bullet from CLAUDE.md's Known open traps; rewrite
        `docs/guide/USER-GUIDE.md:181-182` (assumed: A2); in `docs/product/roadmap.md` mark the Phase 3 vehicle
        info line `[x]`, remove the "Fluids / tyre-pressure settings form" bullet and move the dateline to the
        new `VERSION` (assumed: A1); add one `docs/engineering-log.md` entry. Check that
        `git grep -nP '\x{2014}' -- '*.md'` stays empty
  - [ ] 3.7 Verify all tests pass (vitest with `NODE_OPTIONS=--no-webstorage`, baseline 701/705 with 4 skipped;
        `npx tsc -b`; `dotnet test`)
