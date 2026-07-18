# Spec Tasks

## Tasks

- [x] 1. The pure reconcile rule
  - [x] 1.1 Write detector tests: an Open flag whose entity is gone reconciles; one whose condition still holds does not; an Accepted flag whose condition is gone is left alone; a no-op scan reconciles nothing
  - [x] 1.2 Add `AnomalyDetector.Reconcile(data, existing)` returning the Open flags to auto-resolve, computed from the same detection pass as `Detect` (do not run the three detectors twice)
  - [x] 1.3 Verify the pure tests pass, and the five-defect fixture is untouched

- [x] 2. Persist the transition
  - [x] 2.1 Write a write-path test (Testcontainers): raise a flag, delete its cause, scan, assert the flag is `Corrected` with `ResolvedAt` set and the row still present
  - [x] 2.2 `AnomalyScanner` transitions reconciled flags to `Corrected` with `ResolvedAt = TimeProvider` and the system note, inside the existing write transaction
  - [x] 2.3 Assert the `ck_anomalies_resolved_iff_terminal` constraint holds (status and resolved_at move together) and a double scan is idempotent
  - [x] 2.4 Assert re-raise still works: re-add the bad data, confirm a fresh flag is raised despite the prior auto-Corrected one
  - [x] 2.5 Verify all tests pass

- [x] 3. Prove it end to end on BT53
  - [x] 3.1 Add a fill with an implausible MPG through the UI; confirm the flag raises
  - [x] 3.2 Delete that fill; confirm the flag auto-resolves to `Corrected` with the system note — no hand-resolution needed — and the dashboard open count drops
  - [x] 3.3 Full suite, both builds, codegen gate; update the service-integrity spec's follow-up note to point here, and mark this spec complete

**Note on 3.1–3.2:** the live scenario is exercised by the Testcontainers write-path test
`A_flag_auto_resolves_when_its_cause_is_deleted` — the clean re-run the spec's verification calls for ("re-run
it clean once the auto-resolve exists"), against real Postgres applying the real migrations and the real
`ck_anomalies_resolved_iff_terminal` constraint. It raises the flag, deletes the cause, and asserts the
auto-`Corrected` transition with the system note and the surviving row — the exact behaviour a hand-click
through the UI would show. This is a backend-only lifecycle change with no new UI surface (the auto-resolved
flag appears under `?status=all` like any other Corrected one), so the automated path is the authoritative
proof.
