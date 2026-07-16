# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-07-16-task-service-promotion/spec.md

The route sits behind the gateway on one origin and requires `X-Api-Key` (DEC-009). Registration → id resolution
is the endpoint's job via `VehicleLookup`, exactly as in the existing task and service groups.

## Endpoints

### POST /api/vehicles/{registration}/tasks/{id}/promote

**Purpose:** Convert a completed Workshop task into a `ServiceRecord` — README §3.3's one-click promotion. The
record is created through `ServiceRecordFactory`, so it writes the record, a `MileageReading` with
`Origin = Service`, and (when a cost is given) a mirrored `ExpenseEntry` in one transaction; then the detectors
run. Finally the task's `ServiceRecordId` is stamped with the new record's id.

**Why a sub-resource action, not a PATCH:** promotion is a state transition with side effects (three rows and a
scan), not a field edit. `POST …/{id}/promote` names the verb; folding it into `UpdateTaskAsync` would hide a
history write behind a task update.

**Parameters:** `registration` (path), `id` (path — the task); body `PromoteTaskRequest`:

```
PromoteTaskRequest {
  Mileage: int,              // required — the odometer at completion; a task carries no reading of its own
  Cost: decimal?,           // defaults to the task's EstimatedCost; an estimate is not a receipt
  Type: string?,            // service type; "MOT" matched exactly, offered as a choice not typed
  ServiceDate: DateOnly?,   // defaults to the task's CompletedDate
  NextDueDate: DateOnly?,
  NextDueMileage: int?,
  Notes: string?            // carries the "Converted from workshop task" provenance
}
```

**Response:** `201 Created` with `PromoteTaskResponse { ServiceRecordId: int, Flags: AnomalyFlag[] }` — the same
`AnomalyFlag[]` shape `AddServiceResponse` returns, because a promoted mileage can raise `MileageNonMonotonic`
and the caller must see it. `Location` points at the new record:
`/api/vehicles/{registration}/service/{ServiceRecordId}`.

**Errors:**
- `404` — unknown registration, or no task `id` on this vehicle.
- `409 Conflict` — the task is not `Done` (no completion date to become the service date), or it is already
  promoted (`ServiceRecordId is not null`; promoting twice would orphan the first record).
- `400` — the task is not `Kind = Workshop` (DIY work is added as a DIY record directly), or the mileage is
  missing/invalid.

**Notes:** `Flags` may be non-empty and the promotion still succeeds — §5.3 is flag, never block, the same rule
`AddServiceAsync` follows for the 83,000 mi row. The endpoint does **not** open its own transaction or write its
own reading; `ServiceRecordFactory.CreateAsync` owns all three rows.

## Changes to existing responses

None. `TaskItem` already carries `ServiceRecordId` out of `GetTasksAsync`, so a promoted task reads back as
linked with no payload change — the read side has been ready since the column was added.
