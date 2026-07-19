# Spec Tasks

## Tasks

- [x] 1. The promote endpoint
  - [x] 1.1 Write write-path tests (Testcontainers): a Done Workshop task promotes to one record + one reading + one mirrored expense in one transaction, and `ServiceRecordId` is stamped
  - [x] 1.2 Write refusal tests: non-Done → 409, DIY → 400, already-promoted → 409, unknown task → 404 — each writes nothing
  - [x] 1.3 `POST /tasks/{id}/promote` in `TaskEndpoints.cs`, building a `ServiceRecord` from the task and calling `ServiceRecordFactory.CreateAsync` (no second transaction)
  - [x] 1.4 Run `AnomalyScanner.ScanAsync` after the factory and return `Flags`; stamp `task.ServiceRecordId`
  - [x] 1.5 Regenerate the contract and TS types; verify the staleness gate is green
  - [x] 1.6 Verify all tests pass

- [x] 2. The field mapping and preconditions
  - [x] 2.1 Write tests: `CompletedDate` → `ServiceDate`, `AssignedGarage` → `Garage`, `EstimatedCost` → `Cost` (overridable), `Title`/`Description` → `WorkDone`; `Type` free text with "MOT" matched exactly
  - [x] 2.2 Map the fields in the endpoint; require `Mileage` in the request (a task has no reading of its own)
  - [x] 2.3 The three preconditions as distinct statuses with messages the UI can show
  - [x] 2.4 Verify all tests pass

- [x] 3. The tasks-screen action
  - [x] 3.1 Write tests: the promote action appears only on a Workshop + Done + unpromoted row; a promoted row shows a link to its record instead
  - [x] 3.2 `TasksPage.tsx` — the "Convert to service record" affordance and a promote sheet collecting odometer, confirming cost and type
  - [x] 3.3 On success, route to the new record and invalidate tasks/service/expenses/summary queries so bundle, history and rollups recompute
  - [x] 3.4 Axe sweep + coverage-guard exemptions with reasons
  - [x] 3.5 Verify all tests pass

- [x] 4. Prove it end to end on BT53
  - [x] 4.1 Take a real completed Workshop job with a cost and a garage; promote it through the UI
  - [x] 4.2 Confirm the service record, its mirrored expense (the spend rollup moves) and its mileage reading, and that the task now shows it linked
  - [x] 4.3 Confirm a non-Done, a DIY and an already-promoted task are each refused with a clear reason
  - [x] 4.4 Full suite, both builds, codegen gate; update roadmap and CLAUDE.md
