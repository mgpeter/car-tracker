# Spec Tasks

## Tasks

- [x] 0. Prerequisite: anomaly auto-reconcile
  - [x] 0.1 `2026-07-16-anomaly-lifecycle-reconcile` is built and complete — every delete added below auto-resolves an Open flag whose cause it removes, rather than orphaning it.

- [x] 1. Domain: factory Update/Delete for the two shadowed logs
  - [x] 1.1 Factory tests (Testcontainers): editing a fill re-derives and updates its mirrored reading + expense; deleting a fill removes both shadows; the same for a service record with a cost; cost added-on-edit creates the mirror and removed deletes it; correcting the litres that tripped a flag clears it
  - [x] 1.2 `FuelEntryFactory.UpdateAsync`/`DeleteAsync` carry the reading + expense mirror invariants, inside `CreateExecutionStrategy().ExecuteAsync`
  - [x] 1.3 `ServiceRecordFactory.UpdateAsync`/`DeleteAsync` (expense mirror created/updated/removed across all three cost transitions; `ReferenceWriter` for garage), inside the strategy
  - [x] 1.4 Factory tests pass

- [x] 2. API: fuel PATCH, mileage PATCH+DELETE, and the delete cascade
  - [x] 2.1 Covered by factory tests + the shadow-guard logic; no HTTP test harness exists in the repo (the codebase tests at the factory/scanner level against real Postgres), so endpoint-only guards follow the same untested-by-harness pattern as the pre-existing fuel/expense 409s
  - [x] 2.2 `FuelEndpoints` — `PATCH /{id}` on `FuelEntryFactory.UpdateAsync`; `DELETE` moved onto `DeleteAsync`
  - [x] 2.3 `MileageEndpoints` — `PATCH` + `DELETE`, both guarding non-`Manual` shadow readings with 409, both re-scanning
  - [x] 2.4 `ServiceEndpoints` — `PATCH`/`DELETE` refactored onto the new factory methods (also fixing a latent reading-match-after-mutate bug in the old inline PATCH)
  - [x] 2.5 Verify

- [x] 3. API: tyres, wash, equipment, and the correctness fixes
  - [x] 3.1 Tyre/wash PATCH+DELETE keep the odometer shadow in step (shared `SyncOdometerShadowAsync`) and re-scan; equipment DELETE; the mirror-refusal + re-scan + shadow-removal fixes below
  - [x] 3.2 `LogEndpoints` — tyre `PATCH`+`DELETE`, wash `PATCH`+`DELETE` (via `ReferenceWriter`), equipment `DELETE`
  - [x] 3.3 `ExpenseEndpoints` — mirror-refusal extended to `ServiceRecordId`; `AnomalyScanner` re-run on PATCH/DELETE; the expense's own `Origin=Manual` reading removed on delete
  - [x] 3.4 **Finding, not done as written:** on reading the code, every existing PATCH/DELETE handler uses a *single* `SaveChangesAsync`, which EF wraps in one transaction and the retrying strategy retries whole — so the "bare SaveChanges tears a multi-table edit" concern does not apply to them. The execution strategy is only needed for the multi-`SaveChanges` `BeginTransaction` work, which lives in the fuel/service factories (done). No ceremony added to the single-save handlers.
  - [x] 3.5 OpenAPI contract regenerated (adds only the new operations/DTOs); typed client regenerated via `npm run gen:api`
  - [x] 3.6 Full .NET suite green — 252 tests

- [x] 4. Front-end: dual sheets, delete UI, and the row affordance
  - [x] 4.1 `ConfirmButton.test.tsx`: the two-step confirm does not fire on the first press and names the cascade; it disarms on blur; a clickable `<DataTable>` row activates by click and keyboard while a gated (mirror-shadow) row stays inert
  - [x] 4.2 `<DataTable>` gained `onRowClick?(row)` + `rowClickable?`/`rowLabel?` — interactive, keyboard-activatable rows when set; identical markup when not
  - [x] 4.3 `<ConfirmButton>` extracted — two-step inline confirm, accessible name changes with state and names the cascade, disarms on blur
  - [x] 4.4 All six add-only sheets are now dual add/edit with a footer Delete; tasks/issues/equipment gained a footer Delete on their existing edit sheets
  - [x] 4.5 Row-click wired on fuel, expenses, mileage, service, tyres, wash; mileage gates on `origin === 'Manual'`, expenses on `!isMirrored` (new `serviceRecordId` on the DTO), with the "From fuel"/"From service" pill as the pointer to the source
  - [x] 4.6 Per-screen edit + delete `useMutation`s, invalidating the established keys plus the shadow screens the cascade touches
  - [x] 4.7 `npm run test` (303), `tsc -b`, `npm run build`, axe coverage guard — all green

- [x] 5. Prove it
  - [x] 5.1 `Editing_a_fill_drags_its_reading_and_expense_along` + `Correcting_the_litres_that_tripped_an_implausible_flag_clears_it` — the fill's figures and its mirrored expense move together
  - [x] 5.2 Current mileage derives from the newest reading by date (existing derivation tests) + the mileage DELETE endpoint; deleting a non-latest reading cannot move it, deleting the latest falls it back
  - [x] 5.3 `A_flag_auto_resolves_when_its_cause_is_deleted` (reconcile prerequisite) — deleting the fill behind an implausible flag auto-resolves it to `Corrected`
  - [x] 5.4 Service-mirror guard: the expense endpoint refuses PATCH/DELETE on a `ServiceRecordId` row, and the DTO now exposes it so the screen marks it read-only rather than offering an edit the API would 409
  - [x] 5.5 Full .NET suite (252) + front-end suite (303) + both builds + codegen gate idempotent — all green

**Note on live BT53 verification (5.1–5.4):** the browser in this environment reports a zero-width viewport, so
a hand-click through the running app could not be observed (the same constraint recorded during Phase 2). The
behaviours are instead exercised end to end by the Testcontainers factory/scanner tests named above, against
real PostgreSQL applying the real migrations — the code paths a UI click would drive. No HTTP endpoint test
harness exists in the repo, so the endpoint-only guards (the 409s, the mileage shadow-origin refusal) follow
the same untested-by-harness pattern as the pre-existing fuel/expense mirror 409s.
