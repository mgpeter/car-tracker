# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-07-16-service-history-integrity/spec.md

All routes sit behind the gateway on one origin and require `X-Api-Key` (DEC-009). Registration → id resolution
is the endpoint's job, via `VehicleLookup`, exactly as in the existing groups.

## Endpoints

### GET /api/vehicles/{registration}/service

**Purpose:** The service history, newest last, with the derived next-service figures the dashboard already
shows — read through `IDerivedMetricsService`, not the table, so the screen cannot compute a second answer.
**Parameters:** `registration` (path).
**Response:** `ServiceLog { Records: ServiceRecordItem[], NextServiceDate: Renewal, NextServiceMiles: int? }`
**Errors:** 404 when the registration is unknown.

### POST /api/vehicles/{registration}/service

**Purpose:** Record a service, MOT or repair. Writes three rows in one transaction — the record, a
`MileageReading` with `Origin = Service`, and (when a cost is given) a mirrored `ExpenseEntry` — then runs the
detectors.
**Parameters:** `AddServiceRequest { ServiceDate, Type, Mileage, Garage?, WorkDone?, PartsReplaced?, Cost?, NextDueDate?, NextDueMileage?, Notes? }`
**Response:** `201` with `AddServiceResponse { Id, Flags: AnomalyFlag[] }`
**Errors:** 404 unknown registration; 400 on validation.
**Notes:** `Flags` is non-empty for BT53's 83,000 mi row and the save still succeeds — §5.3 is flag, never
block. `Type = "MOT"` with a `NextDueDate` is what ends the dashboard's "MOT · Not set".

### PATCH /api/vehicles/{registration}/service/{id}

**Purpose:** Correct a record. Re-runs the scan, because editing a mileage down can clear an anomaly and editing
one up can raise one; updates the mirrored reading and expense to match.
**Parameters:** `id` (path); `UpdateServiceRequest` — every field optional.
**Response:** `200` with the updated `ServiceRecordItem`.
**Errors:** 404; 400 on validation.

### DELETE /api/vehicles/{registration}/service/{id}

**Purpose:** Remove a record and its shadows — the mirrored reading and expense cannot outlive their source.
**Response:** `204`.
**Errors:** 404.

### GET /api/vehicles/{registration}/anomalies

**Purpose:** The integrity queue, and the first reader of a flag's *content*. `AnomalyScanner` has written
flags on every write path since M1a, and `CountOpenAnomaliesAsync` already reads the count for the garage card —
so the gap is narrower than "never read" but worse in kind: the app has been able to say **how many** flags
there are without being able to say **what any of them is**.
**Parameters:** `registration` (path); `status` (query, optional) — `open` (default) or `all`. The default view
is work to do, not history.
**Response:** `AnomalyItem[]` — `{ Id, Kind, Severity, EntityType, EntityId, Message, Detail, Status, ResolvedAt, ResolutionNote, CreatedAt }`
**Errors:** 404.

### PATCH /api/vehicles/{registration}/anomalies/{id}

**Purpose:** Resolve a flag with a reason.
**Parameters:** `id` (path); `ResolveAnomalyRequest { Status: Corrected | Accepted | Dismissed, ResolutionNote? }`
**Response:** `200` with the updated `AnomalyItem`.
**Errors:** 404; 400 if the status is not terminal — `Open` is not a resolution, and the check constraint
`ck_anomalies_resolved_iff_terminal` would reject it anyway.
**Notes:** The endpoint sets `ResolvedAt` from `TimeProvider`; the caller never supplies it, or two surfaces
would disagree about when something happened. **Corrected re-raises if the condition returns** — the fix did not
hold — while Accepted and Dismissed stay down. That rule already lives in `AnomalyDetector` and is not
re-implemented here.

## Changes to existing responses

### GET /api/vehicles/{registration}/summary

`VehicleSummary` grows `Integrity { OpenCount, HighestSeverity }`. A count and a severity, not the flags: the
dashboard's panel needs a headline and the queue has its own endpoint, so putting the full list here would make
every reader of a headline figure pay for it. `get_vehicle_summary` over MCP gains the same field, which is the
point of one summary type serving both surfaces (§4).

### GET /api/vehicles (garage)

`GarageItem.OpenAnomalyCount` is already populated, via `IDerivedMetricsService.CountOpenAnomaliesAsync`. No
change to the payload — but the garage card's count becomes a link to the queue, which is the screen it has been
counting toward since M1b with nowhere to send anyone.
