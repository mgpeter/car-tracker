# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-07-14-react-app-foundation/spec.md

This spec adds **one** endpoint. It is not a product feature — it exists so the OpenAPI → codegen → typed
fetch → TanStack Query → render loop can be proven end-to-end before the Dashboard exists. Real endpoints
arrive with the Phase 2 screens.

## Why an endpoint at all

`openapi-typescript` generates from an OpenAPI document, and a document with zero paths generates nothing. The
codegen pipeline, its CI staleness check, and the fetch wrapper are all unverifiable without at least one path
to exercise. Deferring every endpoint to the Dashboard spec would mean shipping this foundation with its
central mechanism untested.

The alternative — mocking the OpenAPI document in tests — proves the generator works but not that it is wired
to *this* API, which is the part that breaks.

## Endpoints

### GET /api/meta

**Purpose:** Return build and environment metadata. Serves as the codegen smoke test, and remains useful
afterwards for confirming which build is deployed.

**Parameters:** None.

**Response:** `200 OK`, `application/json`

```json
{
  "applicationName": "CarTracker",
  "version": "0.1.0",
  "environment": "Development",
  "serverTimeUtc": "2026-07-14T10:53:08.918Z"
}
```

| Field | Type | Notes |
|---|---|---|
| `applicationName` | `string` | Constant |
| `version` | `string` | Informational assembly version |
| `environment` | `string` | ASP.NET environment name |
| `serverTimeUtc` | `string` (date-time) | From the injected `TimeProvider`, never `DateTime.UtcNow` |

**Errors:** None expected. Unreachable API surfaces as a network error in the fetch wrapper, which is itself
worth exercising — the error path is part of what this endpoint proves.

`serverTimeUtc` goes through `TimeProvider` for the same reason the domain does: it keeps the "no direct clock
access" rule true with no exceptions, so nobody finds a precedent for reading the clock directly later.

**Deliberately not included:** no vehicle data, no derived figures, no database access. A meta endpoint that
touched the domain would fail when the database is empty, making the front-end foundation's tests depend on
Phase 1 completing. This endpoint answers only "is the API up and what is it".

## Added 2026-07-15 — the vehicle endpoints

Beyond this spec's scope, recorded here because it is where the API surface is documented. These are the API
half of Phase 2's Dashboard and add-car flow, landed early because nothing the domain computes was observable
without them.

### POST /api/vehicles

**Purpose:** Add a vehicle, with its opening odometer reading.
**Body:** `registration`, `make`, `model`, `year`, `purchaseDate`, `purchaseMileage`, `fuelType`; `variant`,
`colour`, `purchasePrice`, `engineCode` optional.
**Response:** `201` + `Location` → the summary endpoint; body `{ id, registration }`.
**Errors:** `409` on a duplicate registration (matched normalised, so `bt53akj` collides with `BT53 AKJ`);
`401` without a key.

Calls `VehicleFactory.CreateAsync` — never constructs a `Vehicle` inline. That service is what guarantees the
opening `MileageReading`; without it, current mileage derives to null until the first log.

### GET /api/vehicles/{registration}/summary

**Purpose:** Every derived figure for one vehicle, computed on read.
**Response:** `200` → `VehicleSummary` (mileage, renewals, spend, fuel, checks).
**Errors:** `404` unknown registration; `401` without a key.

Registration is matched on the database's normalised generated column, so case and spacing are irrelevant.
Resolving registration → id is the endpoint's job; `IDerivedMetricsService` stays a pure id-keyed API, because
the MCP server will resolve registrations its own way (README §5.2).

**Enums serialise as strings** (`"Petrol"`, not `1`), matching the schema's choice — readable payloads, no
ordinals for clients to know, and the generated TypeScript becomes a union of literals rather than a number.

## Implementation

- A minimal API endpoint in `CarTracker.WebApi`, not a controller. One route, no logic.
- Mapped under the same OpenAPI document as future endpoints, so the generated `schema.d.ts` grows rather than
  being replaced.
- **Anonymous, deliberately** (revised by DEC-009, which brought API-key auth forward from Phase 5). Every
  other `/api` route requires `X-Api-Key`; this one does not, because the front-end needs something to call
  before a key is entered in order to distinguish "no key set" from "the API is down". A sibling
  `GET /api/meta/authenticated` returns 200 only with a valid key, so the front-end can verify one.

## Contract test

One integration test asserting the OpenAPI document contains `/api/meta` with the documented response shape.
This is what catches a rename or an OpenAPI misconfiguration breaking codegen — a failure that would otherwise
surface as a confusing type error in the front-end build.
