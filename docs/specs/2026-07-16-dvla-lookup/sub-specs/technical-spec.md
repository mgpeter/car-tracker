# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-dvla-lookup/spec.md

## Technical Requirements

### The call is server-side, and there is no other option

- The lookup runs in `CarTracker.WebApi`, behind the gateway, on the one origin (DEC-009). The DVLA key is
  `X-Api-Key`-guarded like the rest of `/api`, and the *DVLA* key is server-only config, never shipped to the
  browser. This is not a preference: the strict CSP forbids a browser→`api.dvla`/`history.mot` fetch outright,
  so a client-side lookup could not work even if the key were public — which it must not be.
- A typed HTTP client per upstream (`IHttpClientFactory`), so timeouts, retries and the base addresses are
  configured once. A short timeout and a single retry: the owner is waiting on a sheet, and a slow DVLA must
  fail to manual entry rather than hang the flow.

### Two upstreams, two shapes

- **DVLA Vehicle Enquiry Service (VES)** — `POST` with the reg, returns make, colour, year of manufacture,
  fuel type, engine capacity (cc), and tax status + tax due date. This covers most of the form; note VES
  returns **make and colour but not model** — the model is often absent or coarse, so the field pre-fills from
  what VES gives and stays editable (BT53 is "LAND ROVER", model typed by the owner).
- **DVSA MOT History API** — returns the test history keyed by reg; this spec takes only the **current MOT
  expiry** (the most recent test's expiry). The history beyond that is out of scope.
- Map both into one `VehicleLookupResult` DTO — the un-persisted facts — with each field nullable, because a
  reg may resolve on VES but not DVSA (a brand-new car with no MOT yet), or return a partial record.

### Mapping into the domain — the load-bearing part

- VES fuel type → `FuelType` (Petrol/Diesel/Hybrid/Electric/LPG); make/colour/year/`EngineSizeCc` → the
  matching `CreateVehicleRequest` fields. Model is a best-effort pre-fill.
- **MOT expiry does NOT map to any stored MOT-expiry field, because there is none — that is the design.**
  `Vehicle.MotExpirySeed` is the documented fallback ("read only when no such record exists yet") and derived
  MOT expiry comes from the latest MOT `ServiceRecord.NextDueDate` (`ServiceRecord.cs`: "derived MOT expiry is
  the max `NextDueDate` over a vehicle's records with `Type = "MOT"`"). The DVLA MOT date therefore lands in
  exactly one of two places, decided as task 2:
  - as **`MotExpirySeed`** on the created vehicle (simplest; a later logged MOT pass supersedes it), or
  - as an **initial MOT `ServiceRecord`** (`Type = "MOT"`, `NextDueDate = the DVLA expiry`, `Source = Web`),
    which reads through the normal derived path from day one.
  Either way it is a *seed a real record overrides*, never a stored countdown the dashboard trusts as final.
  Reintroducing a stored MOT expiry would rebuild the first of the five defects — see `VehicleEndpoints.cs`,
  which refuses to make MOT expiry settable for precisely this reason.
- VES tax status → the stored VED inputs (`VedExpiry`, and taxed/SORN as a note), which `RenewalCalculator`
  already reads as inputs to the derived renewal countdown. VED expiry *is* a legitimately stored input (unlike
  MOT), so this mapping is direct.

### The endpoint returns facts, the create is unchanged

- `GET /api/vehicles/lookup/{registration}` returns the `VehicleLookupResult` and persists nothing. The
  existing `POST /api/vehicles` → `VehicleFactory.CreateAsync` path is untouched: the front end takes the
  looked-up facts, lets the owner confirm/edit, then submits the ordinary create request. The opening
  `MileageReading` guarantee (`VehicleFactory`) is preserved because the create path is the same one.
- Failure is a first-class response, not an exception the sheet cannot read: an unknown reg → `404` with a
  ProblemDetails ("no DVLA record for '{reg}'"); an upstream outage/rate-limit → `502`/`503` ProblemDetails
  ("DVLA lookup unavailable — enter the details manually"). The sheet shows the message and leaves manual
  entry open — RFC 9457 ProblemDetails so the generated TS can read the reason (the pattern
  `VehicleEndpoints.cs` already uses for its conflict).

### Front end

- `AddVehicleSheet.tsx` gains the plate input + "Look up" button (the `garage.dc.html` `.lookup` block: the GB
  tab, the yellow plate field). On success it populates the form state; the hint text is the design's verbatim
  "Fetches make, model, year, colour, engine, MOT and tax status from the DVLA — you confirm before anything is
  created." The lookup goes through the typed client (`npm run gen:api` off the committed OpenAPI contract), so
  a contract change is caught by the staleness gate.
- The lookup call is behind TanStack Query like every other read; a loading state on the button, an error state
  that reveals the manual fields. `usePlate()` already handles plate normalisation/display.
- Every new component axe-swept or exempted (`coverage.test.ts`).

## Verification

- **Key never reaches the browser**: the DVLA key is server config; a network trace of the add-car flow shows
  only `/api/vehicles/lookup/...` on the one origin, never a DVLA host. (CSP would block the latter anyway.)
- **Un-persisted**: a lookup that the owner then abandons creates no vehicle, no reading, no record — assert the
  garage is unchanged after a lookup without a submit.
- **MOT seed, not countdown**: create BT53 via the looked-up facts; confirm the MOT status shows, then log an
  MOT pass `ServiceRecord` with a later `NextDueDate` and confirm the derived expiry moves to the logged date —
  the seed did not win. This is the five-defect thesis, tested on the new path.
- **Failure falls back**: an unknown reg and a simulated upstream outage both return a readable ProblemDetails
  and leave manual entry usable; the upstreams are faked in tests (no live DVLA call in CI).
- **Contract**: the lookup endpoint appears in the OpenAPI document and the generated TS types; staleness gate
  green.

## External Dependencies (Conditional)

- **DVLA Vehicle Enquiry Service (VES) API** — `https://driver-vehicle-licensing.api.gov.uk` (or the current
  host). **Justification:** it is the authoritative source for make/colour/year/fuel/engine and tax status from
  a reg; there is no other way to turn a plate into these facts. **Needs an API key** (registered with DVLA),
  stored as server-side config (user-secrets in dev, the gateway/host's secret store in prod), never in
  `appsettings.json` committed.
- **DVSA MOT History API** — `https://history.mot.api.gov.uk` (or current). **Justification:** the current MOT
  expiry to seed the countdown; the reg has no MOT date without it. **Needs its own API key / client
  credentials** (DVSA's is OAuth-client-credentials, distinct from VES's key). Both keys are external config
  and a real external dependency — this spec cannot ship without them provisioned, which is a task, not an
  assumption.
- Both are UK-government APIs with rate limits; the typed clients set a conservative timeout and single retry so
  a throttle degrades to manual entry rather than a hung sheet.
