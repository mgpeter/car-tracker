# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-07-19-starter-check-selection/spec.md

## Endpoints

### GET /api/reference/starter-checks

**Purpose:** Return the generic starter set — the fifteen `CheckTemplate.Generic` items — so the add-vehicle
sheet can present them for selection. Vehicle-independent lookup data, grouped with the other
`/api/reference/…` reads; the front-end never hardcodes this list, keeping it in lockstep with what create
applies.

**Auth:** API key (`X-Api-Key`), like every route except `/api/meta`.

**Parameters:** None.

**Response:** `200 OK` — a JSON array in template (`DisplayOrder`) order:

```json
[
  { "name": "Walk-around: tyres, glass, wipers", "cadenceLabel": "Weekly", "intervalDays": 7, "guidance": "visual — cuts, stone damage, perishing" },
  { "name": "Exterior lights & indicators", "cadenceLabel": "Weekly", "intervalDays": 7, "guidance": null },
  "… 13 more, ending with Tread depth, all 4 tyres"
]
```

Each item: `name` (string), `cadenceLabel` (string), `intervalDays` (int), `guidance` (string | null). The
array is exactly `CheckTemplate.Generic` — the endpoint projects that list and nothing else.

**Errors:** `401` if the key is missing or wrong. No `404` — the set always exists.

### POST /api/vehicles (modified)

**Purpose:** Create a vehicle. Unchanged except that the request may now carry which generic starter checks to
include.

**New field on `CreateVehicleRequest`:**

- `selectedCheckNames` (`string[] | null`, optional, default `null`)
  - **`null` (or omitted)** — apply the whole generic set. Byte-for-byte today's behaviour; existing callers,
    tests and the MCP create path are unaffected.
  - **non-null, with `checkSource: "GenericStarterSet"`** — apply only the template checks whose `name` is in
    the array (ordinal set intersection, template order preserved). Names not in the template are dropped, not
    rejected. An **empty array** creates a vehicle with **no** checks — the deselect-all case.
  - **ignored** when `checkSource` is `"None"` or `"CopyFromVehicle"` — those sources do not draw from the
    template.

**Example body (thirteen of fifteen kept):**

```json
{
  "registration": "AB12 CDE",
  "make": "Toyota",
  "model": "Yaris",
  "year": 2015,
  "purchaseDate": "2026-07-19",
  "purchaseMileage": 48000,
  "fuelType": "Petrol",
  "checkSource": "GenericStarterSet",
  "selectedCheckNames": [
    "Walk-around: tyres, glass, wipers",
    "Exterior lights & indicators",
    "… eleven more; 'Air-con run, 10 minutes' and 'Power steering fluid' omitted"
  ]
}
```

**Response:** unchanged — `201 Created` with `{ id, registration }`.

**Errors:** unchanged — `409 Conflict` if the registration already exists; `400` for the existing field
validation. `selectedCheckNames` adds no new failure mode (unknown names are dropped, an empty array is valid).

## Contract

Both changes are additive to `api-contract/v1.json`: one new GET path, one new optional request property. The
typed front-end client is regenerated off the emitted contract — the client is never hand-edited.
