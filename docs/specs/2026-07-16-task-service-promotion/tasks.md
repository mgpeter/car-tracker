# Spec Tasks

## Tasks

- [ ] 1. The promote endpoint
  - [ ] 1.1 Write write-path tests (Testcontainers): a Done Workshop task promotes to one record + one reading + one mirrored expense in one transaction, and `ServiceRecordId` is stamped
  - [ ] 1.2 Write refusal tests: non-Done → 409, DIY → 400, already-promoted → 409, unknown task → 404 — each writes nothing
  - [ ] 1.3 `POST /tasks/{id}/promote` in `TaskEndpoints.cs`, building a `ServiceRecord` from the task and calling `ServiceRecordFactory.CreateAsync` (no second transaction)
  - [ ] 1.4 Run `AnomalyScanner.ScanAsync` after the factory and return `Flags`; stamp `task.ServiceRecordId`
  - [ ] 1.5 Regenerate the contract and TS types; verify the staleness gate is green
  - [ ] 1.6 Verify all tests pass

- [ ] 2. The field mapping and preconditions
  - [ ] 2.1 Write tests: `CompletedDate` → `ServiceDate`, `AssignedGarage` → `Garage`, `EstimatedCost` → `Cost` (overridable), `Title`/`Description` → `WorkDone`; `Type` free text with "MOT" matched exactly
  - [ ] 2.2 Map the fields in the endpoint; require `Mileage` in the request (a task has no reading of its own)
  - [ ] 2.3 The three preconditions as distinct statuses with messages the UI can show
  - [ ] 2.4 Verify all tests pass

- [ ] 3. The tasks-screen action
  - [ ] 3.1 Write tests: the promote action appears only on a Workshop + Done + unpromoted row; a promoted row shows a link to its record instead
  - [ ] 3.2 `TasksPage.tsx` — the "Convert to service record" affordance and a promote sheet collecting odometer, confirming cost and type
  - [ ] 3.3 On success, route to the new record and invalidate tasks/service/expenses/summary queries so bundle, history and rollups recompute
  - [ ] 3.4 Axe sweep + coverage-guard exemptions with reasons
  - [ ] 3.5 Verify all tests pass

- [ ] 4. Prove it end to end on BT53
  - [ ] 4.1 Take a real completed Workshop job with a cost and a garage; promote it through the UI
  - [ ] 4.2 Confirm the service record, its mirrored expense (the spend rollup moves) and its mileage reading, and that the task now shows it linked
  - [ ] 4.3 Confirm a non-Done, a DIY and an already-promoted task are each refused with a clear reason
  - [ ] 4.4 Full suite, both builds, codegen gate; update roadmap and CLAUDE.md
