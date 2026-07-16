# Spec Tasks

## Tasks

- [ ] 1. The upstream clients and config
  - [ ] 1.1 Write tests: the VES and DVSA clients map their responses to `VehicleLookupResult` from recorded
        fixtures (a resolving reg, a no-MOT reg, a partial record); keys read from server config
  - [ ] 1.2 Typed `IHttpClientFactory` clients for DVLA VES and DVSA MOT History, short timeout + single retry
  - [ ] 1.3 Key/credential config: user-secrets in dev, host secret store in prod; never in committed
        `appsettings.json`
  - [ ] 1.4 Verify tests pass

- [ ] 2. The lookup endpoint
  - [ ] 2.1 Write tests: `GET /api/vehicles/lookup/{reg}` returns un-persisted facts; 404 on unknown reg; 502/503
        on upstream failure; the garage is unchanged after a lookup
  - [ ] 2.2 Decide and record (a short DEC or spec note) whether the MOT expiry seeds `MotExpirySeed` or an
        initial MOT `ServiceRecord`; wire that mapping — never a stored MOT-expiry figure
  - [ ] 2.3 Implement the endpoint in `VehicleEndpoints.cs`, `X-Api-Key` guarded, ProblemDetails on failure
  - [ ] 2.4 Regenerate the OpenAPI contract and TS types; staleness gate green
  - [ ] 2.5 Verify tests pass

- [ ] 3. Add-car pre-fill
  - [ ] 3.1 Write tests: "Look up" populates the form; fields stay editable; an error reveals manual entry;
        no create fires until submit
  - [ ] 3.2 The plate input + "Look up" button in `AddVehicleSheet.tsx` (the `garage.dc.html` `.lookup` block,
        design-verbatim hint), populating form state via the typed client
  - [ ] 3.3 Loading/error states on the button; `usePlate()` for the plate; axe sweep + coverage-guard exemptions
  - [ ] 3.4 Verify tests pass

- [ ] 4. Prove it end to end on BT53
  - [ ] 4.1 In the add-car sheet, look BT53's reg up: make/year/colour/engine/fuel pre-fill, DVLA key never in a
        browser trace, every field editable
  - [ ] 4.2 Create the car; confirm the MOT status shows as a seed, then log an MOT pass `ServiceRecord` with a
        later `NextDueDate` and confirm the derived expiry moves to the logged date — the seed did not win
  - [ ] 4.3 Confirm an unknown reg and a simulated outage both fall back cleanly to manual entry
  - [ ] 4.4 Full suite, both builds, codegen gate; update roadmap/CLAUDE.md
