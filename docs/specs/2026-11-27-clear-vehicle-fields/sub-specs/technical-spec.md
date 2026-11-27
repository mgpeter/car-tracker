# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-11-27-clear-vehicle-fields/spec.md

## Why a list, not null-means-clear

- `VehiclePatch` is shared by the REST endpoint and the MCP settings tools. Every MCP tool parameter defaults to
  `null` when the assistant omits it, so making `null` mean "clear" would turn `set_fluids(oilSpec: ...)` into
  "set the oil and wipe the other eleven fluids".
- System.Text.Json cannot tell an absent property from an explicit `null` on a record without a custom
  `Optional<T>` wrapper on all 40 fields and their generated client types.
- A named list is additive to the contract, keeps `null` meaning "unchanged" everywhere, and says what it does
  in the request body.

## Contract

- New enum `VehicleField` in `CarTracker.Shared/Logs/VehicleDtos.cs`, serialised as strings by the existing
  `JsonStringEnumConverter`. Members, block-prefixed so the name says where the field lives:
  - Root: `Colour`, `Vin`, `BodyStyle`, `Seller`, `DefaultGarage`, `Notes`, `MotExpirySeed`, `VedExpiry`,
    `VedAnnualCost`, `UlezCompliant`, `PurchasePrice`
  - Insurance: `InsuranceInsurer`, `InsurancePolicyNumber`, `InsurancePeriodStart`, `InsurancePeriodEnd`,
    `InsuranceCoverType`, `InsurancePremium`, `InsuranceExcessCompulsory`, `InsuranceExcessVoluntary`,
    `InsuranceNcbYears`
  - Breakdown: `BreakdownProvider`, `BreakdownPolicyNumber`, `BreakdownExpiry`
  - Fluids: `FluidsFuelTankCapacityLitres`, `FluidsOilSpec`, `FluidsOilCapacityLitres`, `FluidsCoolantSpec`,
    `FluidsCoolantCapacityLitres`, `FluidsBrakeFluidSpec`, `FluidsTransmissionOilSpec`, `FluidsSparkPlugPart`,
    `FluidsOilFilterPart`, `FluidsAirFilterPart`, `FluidsFuelFilterPart`, `FluidsCabinFilterPart`
  - Tyres: `TyresTyreSize`, `TyresPressureFrontPsi`, `TyresPressureRearPsi`, `TyresPressureFrontLadenPsi`,
    `TyresPressureRearLadenPsi`, `TyresMinTreadMm`
- `Status` and `IsDefault` are not members: their columns are `IsRequired()`.
- `VehiclePatch` gains `IReadOnlyList<VehicleField>? Clear = null`, appended last so every positional
  construction stays valid (the same reason `PurchasePrice` and `Breakdown` were appended).
- `UpdateVehicleRequest` gains the same property, appended last, and `UpdateVehicleAsync` passes it through.
- Regenerate `src/CarTracker.WebApp/src/api/generated/schema.d.ts`. The diff is additive.

## Domain: `VehicleUpdateService.ApplyAsync`

- **Validate before any write.** For each entry in `Clear`, if the same field carries a non-null value in the
  patch, return `WriteResult.Invalid(<field key>, "<Label> cannot be both set and cleared.")`. The key uses the
  web form's field key (`oilSpec`, `premium`) so `reportApiError` lands it on the right input. Duplicates in
  `Clear` are harmless and not an error.
- **Order:** existing validation, then the existing merge, then apply clears, then `purchaseMirror.SyncAsync`,
  then the single `SaveChangesAsync`. Clears run after the merge so the conflict check above is the only place
  "set" and "clear" meet.
- **Applying a clear** sets the property to `null`. Insurance and breakdown use `??=` to create the owned
  object, so clearing on a vehicle that has none creates nothing: skip the owned block when it is null.
- **Already null** is a no-op, so clearing is idempotent.
- **`PurchasePrice`:** no new code. `VehiclePurchaseMirror.SyncAsync` already removes the mirror when the price is
  null, and it runs after the clears. Update the comment in `ApplyAsync` that says there is no way to clear a
  price.
- **`DefaultGarage`:** sets the column to null and leaves the garage reference row alone. No FK exists
  (DEC-018), so nothing cascades.
- **`MotExpirySeed`:** cleared like any other field. A logged MOT pass still wins in `RenewalCalculator`, so the
  countdown only changes for a car with no MOT record.
- **Insurance period check:** unchanged. It compares the patch's own start and end, and a cleared side cannot
  produce an end-before-start.
- One implementation choice is left to the implementer: a `switch` over `VehicleField`, or a
  `Dictionary<VehicleField, Action<Vehicle>>`. Either way, a test enumerates `Enum.GetValues<VehicleField>()` and
  proves each member clears its own column and nothing else, so a new member without a handler fails the build.

## Web: `VehicleEditSheet`

- `FieldSpec` gains `clears: VehicleField`, typed against `components['schemas']['VehicleField']` from the
  generated contract, never a hand-typed string union (standing rule: source values, never guess them).
- `submit`:
  - A field whose stored value was non-empty and whose effective value is now blank is **omitted** from the
    block body and its `clears` member is added to a root-level `clear` array.
  - A field that was empty and is still empty is omitted from both. It must not send a clear, so an untouched
    sheet still sends no clears.
  - Every other field sends its coerced value as now.
  - `clear` is sent only when non-empty.
- `stored` for `motSeed` and `ved` comes from the derived summary's renewals, so blanking the MOT seed on a car
  with an MOT record sends `MotExpirySeed` and the row redisplays the record's date after save. That is correct
  (the record wins) and the existing hint already says so.
- Hint text: replace `BLANK_LEAVES_STORED` in `api/vehicle.ts` with `BLANK_CLEARS = 'leave blank to clear it'`,
  and rewrite its doc comment to describe the `clear` list. Delete the old constant and its import.
- The bool `Select` ("not recorded") already maps to blank, so choosing it on a recorded ULEZ value sends
  `UlezCompliant` in `clear` with no extra code.
- The garage `Combobox` follows the same rule.
- Errors from a conflict arrive keyed by field and render through the existing `reportApiError` path.

## Tests

Data tests run against real PostgreSQL in `tests/CarTracker.Data.Tests/VehicleUpdateServiceTests.cs`, pinned to
an owner with `TestOwner.As(ownerId)`:

- Clearing `FluidsOilSpec` nulls the column and leaves the other eleven fluids as stored.
- Every `VehicleField` member clears exactly its own column (enumerated, as above).
- Set-and-clear on one field returns `Validation` keyed on that field, and the row is unchanged after a reload.
- Clearing `PurchasePrice` removes the `IsVehiclePurchase` expense in the same save.
- Clearing an already-null field succeeds and changes nothing.
- Clearing an insurance field on a vehicle with no insurance does not create an empty `InsurancePolicy`.
- A patch with `Clear = null` behaves exactly as before (regression for the MCP tools).

Front-end tests in `VehicleInfoPage.test.tsx`, asserting the request body rather than the DOM:

- Replace "sends null for a blank, which the server merge leaves unchanged": blanking a preloaded "Engine oil"
  sends `clear: ['FluidsOilSpec']` and no `oilSpec` key in `fluids`.
- An untouched sheet sends no `clear`.
- A field that was already empty and stays empty is not cleared.
- Every `VehicleField` member is the `clears` of exactly one editor field (the inverse of "can edit every field
  the API accepts").
- Hints read "leave blank to clear it".

## Docs and release

- `CLAUDE.md`: remove the "A blank field in a vehicle PATCH means leave unchanged" bullet from Known open traps.
- `docs/product/roadmap.md`: mark Phase 3's vehicle info line `[x]` (the editors shipped in `f6f800f`,
  2026-08-15), replace the "Fluids / tyre-pressure settings form" entry under Specced but unscheduled, and update
  the dateline.
- `docs/engineering-log.md`: one entry when it ships.
- Minor `VERSION` bump inside the feature commit.
