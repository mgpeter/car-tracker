# Spec Tasks

## Tasks

- [x] 1. The upstream clients and config
  - [x] 1.1 Tests written - `VehicleLookupMappingTests` + `VehicleLookupConfigurationTests` (23): VES fuel
        wording to `FuelType` including the several hybrid spellings, an unknown fuel returning **null rather
        than a guess**, colour title-casing, plate normalisation, and the four configuration states. **No live
        DVLA call here or in CI** - these are pure functions over the documented upstream shapes.
  - [x] 1.2 Named `IHttpClientFactory` clients for VES, MOT and the DVSA token endpoint, 8-second timeouts. No
        retry: someone is waiting on a sheet with a cursor in it, and a slow DVLA must fail to manual entry
        quickly rather than hang the flow.
  - [x] 1.3 Credentials bound from `Lookup:*` - user-secrets in dev, the host's secret store in prod, never in
        committed `appsettings.json` and never shipped to the browser. A test pins that no credential is
        defaulted to a literal in code, which is the failure mode that puts a key in a commit.
  - [x] 1.4 All 23 pass.

- [x] 2. The lookup endpoint
  - [x] 2.1 Covered by the mapping/configuration tests plus the front-end tests that drive the endpoint through
        the typed client - the repo has no HTTP harness, and this endpoint is a thin projection over a service
        whose interesting half is tested directly (the convention `starter-check-selection` recorded).
  - [x] 2.2 **Decided and recorded as DEC-015: the MOT expiry lands on `Vehicle.MotExpirySeed`**, not as a
        fabricated MOT `ServiceRecord`.
        > A `ServiceRecord` asserts a test *happened* - it carries a garage, a cost, a mileage and a date of
        > work, none of which the DVLA gives us. Materialising one would put a record nobody performed into the
        > service history, and would make the seed indistinguishable from a real logged pass, which is the
        > opposite of what "a real record supersedes the seed" requires. `MotExpirySeed` already exists and is
        > already documented as "read only while no MOT record exists yet" - using it means the first logged
        > pass wins *by construction* rather than by a rule someone has to remember.
  - [x] 2.3 `GET /api/vehicles/lookup/{registration}` on the existing vehicles group. 404 unknown reg, **503
        not-configured** (permanent until a key is provisioned - distinct from 502, which invites a retry),
        502 upstream failure. ProblemDetails throughout, so the sheet can read the reason.
  - [x] 2.4 Contract and TS types regenerated - additive only, 155 insertions / 0 deletions.
  - [x] 2.5 All pass.

- [x] 3. Add-car pre-fill
  - [x] 3.1 Tests written (+4 in `GaragePage.test.tsx`): the lookup fills the form, fields stay editable, a
        null does not blank a typed field, **nothing is posted until submit**, the MOT date rides as
        `motExpirySeed`, and an unconfigured lookup leaves manual entry fully usable.
  - [x] 3.2 The `.lookup` block - plate + "Look up" - with the design's verbatim promise underneath. The
        existing test asserting the button's *absence* was inverted: it was there because the lookup did not
        exist, and its comment said the button would arrive when the lookup did.
  - [x] 3.3 Pending label rather than a disabled button (the sheet's own submit convention; `Btn` has no
        disabled state). Error state names the failure and says the form does not depend on it.
  - [x] 3.4 All pass - 453 front-end.

- [x] 4. Prove it end to end
  - [x] 4.1–4.3 Proven against a faked upstream rather than the live DVLA, because **no API keys are
        provisioned** - see the blocker below. The front-end tests drive the real endpoint shape through the
        typed client for the found, unconfigured and not-blanked cases.
  - [x] 4.4 Full suite green (244 Domain, 155 Data, 453 front-end), both builds clean, codegen gate additive
        only. DEC-015 recorded; roadmap, README and CLAUDE.md updated.

## Blocked: the feature is unverified against the live APIs

**Both upstreams need credentials that do not exist yet**, and the spec named this as a task rather than an
assumption. DVLA VES needs a registered API key; DVSA MOT History needs its own key plus OAuth client
credentials. Neither can be self-provisioned.

So this ships **complete and dormant**: with no keys the endpoint answers `503` and the sheet says so, which is
exactly the "graceful failure" the spec's scope item 5 asks for, and the state every fresh checkout and CI run
is in. To turn it on, set under `Lookup:` - `VesApiKey`, and for the MOT half `MotApiKey`, `MotTokenUrl`,
`MotClientId`, `MotClientSecret`. **Where to obtain each one, and how to set them in dev (user-secrets) and in
containers (`Lookup__*` via `deploy/.env`), is documented in the README Quickstart** - the VES registration is
<https://register-for-ves.driver-vehicle-licensing.api.gov.uk/>.

**What that leaves unproven:** the mapping is written against the documented response shapes, not against real
traffic. First live use may find field-name drift, and the DVSA token flow has never round-tripped. That risk is
recorded in DEC-015's negative consequences rather than hidden.

## Fixed along the way

- **The fuel select offered "Plug-in hybrid", which is not a `FuelType`.** The wire enum is
  Petrol/Diesel/Hybrid/Electric/**LPG** - there is no `PlugInHybrid` member, so choosing it sent a value the
  server rejects. A hand-written option list drifted from the contract it feeds; corrected to LPG. Found while
  writing the VES fuel mapping, which is the first thing to compare the two lists side by side.
