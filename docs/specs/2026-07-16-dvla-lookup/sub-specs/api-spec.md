# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-07-16-dvla-lookup/spec.md

## Endpoint

### `GET /api/vehicles/lookup/{registration}`

**Purpose:** Resolve a UK registration to un-persisted vehicle facts (DVLA VES + DVSA MOT History), for the
owner to confirm in the add-car sheet. Reads only — persists nothing.

**Group:** added to `VehicleEndpoints.cs`'s `/api/vehicles` group, so it sits with the create/summary/detail
endpoints. `X-Api-Key` guarded like the rest of `/api` (only `/api/meta` is anonymous).

**Why a GET, not a POST.** It creates nothing and is idempotent — the same reg returns the same facts. A `POST
/vehicles/lookup` would read as "create a lookup"; a `GET .../lookup/{reg}` reads as "fetch the facts for this
reg", which is what it does. The create remains the separate, deliberate `POST /api/vehicles`.

**Parameters:** `registration` (path) — normalised the same way `VehicleLookup.Normalize` treats it (case,
spacing), so "BT53 AKJ" and "bt53akj" resolve alike.

**Response 200** — `VehicleLookupResult`, every field nullable (a reg may resolve partially):

```
VehicleLookupResult {
  registration:   string        // normalised, echoed back
  make:           string?       // VES: e.g. "LAND ROVER"
  model:          string?       // best-effort; VES often omits — owner types it
  year:           int?          // VES year of manufacture
  colour:         string?       // VES
  engineSizeCc:   int?          // VES engine capacity
  fuelType:       FuelType?     // VES fuel type → the app enum
  motExpiry:      DateOnly?     // DVSA current MOT expiry — a SEED, see below
  motStatus:      string?       // e.g. "Valid" / "No details held"
  taxStatus:      string?       // VES: "Taxed" / "SORN" / "Untaxed"
  vedExpiry:      DateOnly?     // VES tax due date → stored VED input
  source:         string        // "dvla" — provenance of the facts
}
```

**`motExpiry` is a seed, not a stored countdown.** The front end carries it into the create as `MotExpirySeed`
(or the create path materialises an initial MOT `ServiceRecord` — task 2 decides which). It is never written
as a stored MOT-expiry figure; a later logged MOT pass supersedes it. This is the single most important
contract note: MOT expiry is derived everywhere else in the app, and this endpoint must not create a place
where it is stored-and-trusted (`VehicleEndpoints.cs`, the first of the five defects).

**Response 404** — `ProblemDetails`, "No DVLA record for registration '{reg}'." The reg is well-formed but
neither upstream knows it. The sheet shows this and keeps manual entry open.

**Response 502 / 503** — `ProblemDetails`, "DVLA lookup unavailable — enter the details manually." An upstream
timeout, outage or rate-limit. Distinct from 404 so the front end can say "try again or type it in" rather than
"no such car".

**Errors are ProblemDetails (RFC 9457), never anonymous `{ message }`** — an anonymous type generates as
`unknown` and the front end cannot read why it failed, the exact reasoning `VehicleEndpoints.cs` records for its
409.

## What this does NOT add

- No new persisting endpoint. The create is still `POST /api/vehicles` → `VehicleFactory`, unchanged. The
  lookup and the create are two calls: fetch facts, confirm, create — never fused into a "lookup-and-create".
- No refresh-existing endpoint. Re-running a lookup against a car already in the garage is out of scope (see
  spec Out of Scope); this endpoint is for the pre-fill only.
- No MOT-history endpoint. Only the current expiry is taken; the DVSA test history is not surfaced or stored.
