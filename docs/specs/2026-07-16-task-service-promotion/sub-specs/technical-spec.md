# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-task-service-promotion/spec.md

## Technical Requirements

### The link already exists

- `MaintenanceTask.ServiceRecordId` (`src/CarTracker.Data/MaintenanceTask.cs`) is a nullable `int?`, its comment
  reads "README §3.3's one-click promotion link; preserved after promotion", and `TaskItem` already carries it
  out through `GetTasksAsync`. The read side is done. What is missing is the write that sets it — the endpoint's
  own comment says so: "Set when a task was promoted to a service record. Promotion itself is M2." **No schema
  change**: this spec adds a column to nothing.

### Promotion goes through the factory, not around it

- The record is created by `ServiceRecordFactory.CreateAsync` (`src/CarTracker.Domain/ServiceRecordFactory.cs`),
  the same path `ServiceEndpoints.AddServiceAsync` uses. That factory writes three rows in one transaction — the
  `ServiceRecord`, a `MileageReading` with `Origin = Service`, and (when there is a cost) a mirrored
  `ExpenseEntry` — inside `Database.CreateExecutionStrategy().ExecuteAsync(...)` because Aspire's retrying
  strategy refuses a user-initiated transaction. Promotion must not open its own transaction or write its own
  reading; it builds a `ServiceRecord` and hands it over. A second three-row path is the drift the factory
  exists to prevent, and the £163.16 fuel gap is the standing evidence for what an un-mirrored cost costs.
- After the factory returns, run `AnomalyScanner.ScanAsync` exactly as `AddServiceAsync` does — a promoted
  record writes a mileage reading like any other, and a promoted mileage can trip `MileageNonMonotonic`. The
  flags are returned, never a gate (§5.3).

### Field mapping — task to record

- `ServiceDate` ← the task's `CompletedDate` (a Done task always has one; the entity constrains it "present iff
  Status is Done"). `Mileage` ← the odometer at completion. **The task has no mileage column today**, so the
  promote request must supply it: a task records *what to do*, not *the reading it was done at*, and inventing a
  reading would be the same stored-derived lie the project rejects. The promote sheet asks for it.
- `Garage` ← `MaintenanceTask.AssignedGarage` (already a keyed FK via `ReferenceWriter`; the factory calls
  `EnsureGarageAsync` again, which is idempotent). `Cost` ← the task's `EstimatedCost`, editable on the promote
  sheet because an estimate is not a receipt — the record wants the amount actually paid.
- `WorkDone` ← the task's `Title` (and `Description` folded in if present). `Type` ← a chosen service type,
  defaulting to `ServiceRecordFactory.ServiceCategory`'s sibling on the record side; **`Type` is free text and
  "MOT" is matched exactly** for the expiry derivation, so the sheet offers it as a choice, never trusts it
  typed — the same trap `ServiceEndpoints` documents.
- Stamp `task.ServiceRecordId = record.Id` in the **same** `SaveChanges` scope as the endpoint's own work, after
  the factory has committed and the id exists.

### Preconditions, each a distinct refusal

- **Not Done** → 409: a job still open has no completion date to become the `ServiceDate`.
- **Not Workshop** → 400: DIY work is added as a DIY record directly; `MaintenanceTask.Kind` is the gate.
- **Already promoted** (`ServiceRecordId is not null`) → 409: promoting twice would create a second record and
  orphan the first. The link that read-only round-trips today is what makes this checkable.
- Each is a separate status/message so the tasks screen can say which precondition failed, not "cannot promote".

### Screen

- `TasksPage.tsx` gains a "Convert to service record" action, present **only** on a row that is Workshop, Done
  and unpromoted — absence, not a disabled control, per the design. A row already promoted shows a link to its
  record instead ("Converted → service history"). The promote sheet collects the odometer and confirms cost and
  type, then on success routes to the service history screen (or the new record) and invalidates the tasks,
  service, expenses and summary queries so the bundle total, the history and the spend rollups all recompute.
- The service side already renders records; a promoted record is distinguishable by its `ServiceRecordId`
  back-reference on the task, and the "Converted from workshop task" note rides in the record's `Notes` or
  `WorkDone`. Every new component axe-swept or exempted in `coverage.test.ts`; `usePlate()` supplies the plate.

## Verification

- Domain/write-path tests (Testcontainers, real Postgres): promoting a Done Workshop task creates one
  `ServiceRecord`, one `MileageReading (Origin = Service)`, and — with a cost — one mirrored `ExpenseEntry`, all
  in one transaction; the task's `ServiceRecordId` is set. Promoting a non-Done, a DIY, or an already-promoted
  task is refused with the right status and writes nothing.
- Re-scan asserted: a promoted mileage above the current reading raises exactly one `MileageNonMonotonic` and
  still saves.
- The codegen staleness gate stays green: `npm run gen:api` then `git diff --exit-code`, since the new endpoint
  and its request/response reach the OpenAPI contract.
- Live on BT53: take a real completed Workshop job (a garage repair with a cost and a garage), promote it,
  confirm the record, its mirrored expense and its reading, and that the tasks screen now shows it linked.

## External Dependencies (Conditional)

None. `MaintenanceTask.ServiceRecordId`, `ServiceRecordFactory`, `AnomalyScanner` and the tasks and service
screens all exist; this is the write that connects a column that already round-trips read-only.
