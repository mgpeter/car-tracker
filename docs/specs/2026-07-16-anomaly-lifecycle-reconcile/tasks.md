# Spec Tasks

## Tasks

- [ ] 1. The pure reconcile rule
  - [ ] 1.1 Write detector tests: an Open flag whose entity is gone reconciles; one whose condition still holds does not; an Accepted flag whose condition is gone is left alone; a no-op scan reconciles nothing
  - [ ] 1.2 Add `AnomalyDetector.Reconcile(data, existing)` returning the Open flags to auto-resolve, computed from the same detection pass as `Detect` (do not run the three detectors twice)
  - [ ] 1.3 Verify the pure tests pass, and the five-defect fixture is untouched

- [ ] 2. Persist the transition
  - [ ] 2.1 Write a write-path test (Testcontainers): raise a flag, delete its cause, scan, assert the flag is `Corrected` with `ResolvedAt` set and the row still present
  - [ ] 2.2 `AnomalyScanner` transitions reconciled flags to `Corrected` with `ResolvedAt = TimeProvider` and the system note, inside the existing write transaction
  - [ ] 2.3 Assert the `ck_anomalies_resolved_iff_terminal` constraint holds (status and resolved_at move together) and a double scan is idempotent
  - [ ] 2.4 Assert re-raise still works: re-add the bad data, confirm a fresh flag is raised despite the prior auto-Corrected one
  - [ ] 2.5 Verify all tests pass

- [ ] 3. Prove it end to end on BT53
  - [ ] 3.1 Add a fill with an implausible MPG through the UI; confirm the flag raises
  - [ ] 3.2 Delete that fill; confirm the flag auto-resolves to `Corrected` with the system note — no hand-resolution needed — and the dashboard open count drops
  - [ ] 3.3 Full suite, both builds, codegen gate; update the service-integrity spec's follow-up note to point here, and mark this spec complete
