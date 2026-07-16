# Spec Tasks

## Tasks

- [ ] 1. Reference-list read + CRUD API
  - [ ] 1.1 Write write-path tests (Testcontainers): list with reference counts; create rejects duplicate names; rename cascades to referencing columns in one transaction
  - [ ] 1.2 Garages — `GET / POST / PATCH /{name} / DELETE /{name}` on `ReferenceEndpoints`, rename cascading `ServiceRecord.Garage` / `MaintenanceTask.AssignedGarage` / `Vehicle.DefaultGarage`
  - [ ] 1.3 Wash locations — the same four routes, cascading `WashEntry.Location`
  - [ ] 1.4 Extend `GET expense-categories` with `IsSystem` + `ReferenceCount`; add category `PATCH` (rename/reorder) and guarded `DELETE`
  - [ ] 1.5 Regenerate the contract and TS types; verify the staleness gate is green
  - [ ] 1.6 Verify all tests pass

- [ ] 2. The referential-integrity guard
  - [ ] 2.1 Write tests: deleting a referenced row is refused with the count; a re-home re-points records then removes the row in one transaction; deleting Fuel is refused unconditionally; a system category delete is refused
  - [ ] 2.2 Reference-counting across every FK column, before any delete
  - [ ] 2.3 Re-home path (`?rehomeTo=`) inside an execution strategy; Fuel rename-lock (mirror resolves by constant)
  - [ ] 2.4 Verify all tests pass

- [ ] 3. Reference-list settings panels
  - [ ] 3.1 Write front-end tests: delete asks first and shows the count; a system/Fuel row shows a lock not a delete; rename updates the pick-list
  - [ ] 3.2 Garages / wash-locations / categories panels in `screens/settings/`, editable lists with add / rename / guarded delete
  - [ ] 3.3 The delete-guard dialog (re-home or block) and the locked-row affordance
  - [ ] 3.4 Axe sweep + coverage-guard exemptions with reasons
  - [ ] 3.5 Verify all tests pass

- [ ] 4. Check-definition management panel
  - [ ] 4.1 Write tests: retire via `IsActive` drops the check from the active 18 and keeps its logs; guidance/order edits round-trip through the existing PATCH
  - [ ] 4.2 `settings/CheckDefinitionsPanel.tsx` — the Cadence / Days / Active / Order table, Active as retire, inline guidance and reorder
  - [ ] 4.3 Frame delete as the rare "should never have existed" case (it cascades logs); lead with retire
  - [ ] 4.4 Verify all tests pass

- [ ] 5. Prove it end to end on BT53
  - [ ] 5.1 Rename a garage entered during dogfooding; confirm the pick-lists follow
  - [ ] 5.2 Try to delete a garage a service record references — refused with the count; re-home and confirm the records move
  - [ ] 5.3 Confirm Fuel cannot be deleted or renamed; remove a mistaken non-system category with no entries
  - [ ] 5.4 Retire a check; confirm it drops from the 18 and its logs survive
  - [ ] 5.5 Full suite, both builds, codegen gate; update roadmap and CLAUDE.md
