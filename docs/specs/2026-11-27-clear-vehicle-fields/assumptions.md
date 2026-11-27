# Assumptions

## A1 · open · medium
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** spec (lands in 3.6)
- **Question:** technical-spec.md says the vehicle editors shipped in `f6f800f` and asks for the roadmap's Phase 3 "Vehicle info / settings" line to be marked `[x]` and the "Fluids / tyre-pressure settings form" entry under Specced but unscheduled to be replaced. The roadmap still calls that form missing, and the batch queue has item #4 `new: fluids-tyre-settings-form` drafting a spec for it. Which is right?
- **Assumed:** The spec is right. `VehicleEditSheet.tsx` already has `fluids` and `tyres` editors writing `FluidsPatch`/`TyresPatch`, and the clear list closes the last gap. Task 3.6 marks the Phase 3 line `[x]`, removes the Specced-but-unscheduled bullet, and moves the dateline. Batch item #4 probably duplicates shipped work and should be checked before it runs.
- **Why:** The code is the evidence. `f6f800f` (2026-08-15) created `VehicleEditSheet.tsx` with the fluids and tyres editors, and the roadmap was not updated for them.
- **Touches:** docs/product/roadmap.md
- **If wrong:** Leave the roadmap bullet in place and drop 3.6's roadmap edit (small). If #4 means something other than the existing sheet, its draft says what.

## A2 · open · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 3.5, 3.6
- **Question:** The spec only names the `BLANK_LEAVES_STORED` hint. Other text also promises that a blank keeps the stored value: the Vehicle screen's footer (`VehicleInfoPage.tsx:73`), the user guide (`docs/guide/USER-GUIDE.md:181-182`), and comments in `DocumentDtos.cs:54`, `VehicleLifecyclePanel.tsx:108` and `VehicleUpdateService.cs` (the stale "DefaultGarage is a foreign key" note, which DEC-018 made untrue). Should they change too?
- **Assumed:** Yes. The footer and user guide say a blank clears the stored value. The comments describe the `clear` list, and the DefaultGarage comment says no FK exists. Existing em-dashes in the lines being edited stay, and new text uses none.
- **Why:** Leaving them would make the screen contradict its own hints. These are copy and comment edits.
- **Touches:** src/CarTracker.WebApp/src/screens/VehicleInfoPage.tsx, docs/guide/USER-GUIDE.md, src/CarTracker.Shared/Logs/DocumentDtos.cs, src/CarTracker.WebApp/src/screens/vehicle/VehicleLifecyclePanel.tsx, src/CarTracker.Domain/Vehicles/VehicleUpdateService.cs
- **If wrong:** Revert the specific lines (small).

## A3 · open · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 1.4
- **Question:** The conflict error needs a key (the web form key) and a label ("Engine oil cannot be both set and cleared.") for each of the 41 `VehicleField` members. The spec gives one example. Where do the others come from?
- **Assumed:** Domain keeps one entry per member next to the clear handler, holding the camelCase form key and a plain label. The label is the web `FieldSpec` label with unit suffixes dropped ("Premium", "Oil capacity", "Front tyre pressure (laden)"). `InsurancePolicyNumber` and `BreakdownPolicyNumber` both use the key `policyNumber`, which works because only one sheet is open at a time. Tests pin the spec's example message and the key for every member. They do not pin every label.
- **Why:** The domain cannot import the web labels. Units read badly in a sentence, and the error renders under the field anyway.
- **Touches:** src/CarTracker.Domain/Vehicles/ (clear handler and labels), tests/CarTracker.Data.Tests/VehicleUpdateServiceTests.cs
- **If wrong:** Change label strings in one table (small).

## A4 · open · medium
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 2.3
- **Question:** `VehicleEndpoints.UpdateVehicleAsync` builds `VehiclePatch` positionally, and `Clear` is appended last with a default of `null`. Forgetting `request.Clear` still compiles and drops every clear without a sound. There is no `CarTracker.WebApi.Tests` project (CLAUDE.md). How is the pass-through tested?
- **Assumed:** No new test project. 2.1 adds contract tests over `api-contract/v1.json` (the shape). 2.3 changes the endpoint's construction to named arguments so the `Clear:` argument shows up in review. 2.5 makes one manual PATCH against the running AppHost (set-and-clear answers 400, a clear nulls the field), and the report lists the pass-through as covered by hand. The claim that an unknown `VehicleField` string answers 400 from model binding is left to the framework and is not tested.
- **Why:** CLAUDE.md says rules worth testing live in the domain and the WebApi has no test project. Adding one is a bigger decision than this spec.
- **Touches:** src/CarTracker.WebApi/Endpoints/VehicleEndpoints.cs, src/CarTracker.WebApp/src/api/contract.test.ts
- **If wrong:** Add a `WebApplicationFactory` test project over Testcontainers with one PATCH test (medium).

## A5 · open · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 3.1
- **Question:** openapi-typescript emits `VehicleField` as a type only, so a vitest test has no runtime list of members for "every `VehicleField` member is the `clears` of exactly one editor field". Where does the list come from?
- **Assumed:** The test file declares `const ALL: Record<VehicleField, true> = { ... }` and takes `Object.keys(ALL)`. `tsc -b` compiles `src/**` including tests, so a new contract member fails the typecheck until it is added, and the test then fails until an editor claims it. No runtime list ships in production code.
- **Why:** This follows the standing `Record<Enum, ...>` rule and the type-assertion pattern `contract.test.ts` already uses.
- **Touches:** src/CarTracker.WebApp/src/screens/VehicleInfoPage.test.tsx
- **If wrong:** Export the record from `api/vehicle.ts` instead (small).

## A6 · open · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 3.3
- **Question:** When every field in a block is either cleared or was empty and is still empty, the block body is `{}`. Send `insurance: {}` or leave the block out?
- **Assumed:** Leave the block key out when its body is empty. `insurance: {}` would hit `vehicle.Insurance ??= new InsurancePolicy()` for nothing.
- **Why:** The request says only what it changes. Domain behaviour is the same either way.
- **Touches:** src/CarTracker.WebApp/src/screens/vehicle/VehicleEditSheet.tsx, src/CarTracker.WebApp/src/screens/VehicleInfoPage.test.tsx
- **If wrong:** Always send the block (one line, one test).
