# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-anomaly-lifecycle-reconcile/spec.md

## Technical Requirements

### Where the logic lives

- **The detector stays pure.** `AnomalyDetector.Detect(data, existing)` already computes `found` — the full set
  of anomalies that are *currently* true, before it filters out the ones already decided. That set is the ground
  truth this spec needs: an Open flag whose key is **not** in `found` is a flag whose condition no longer holds.
- Add a pure reconcile step alongside `Detect`, testable against the workbook fixture without a database. The
  cleanest shape given the existing design is a second pure method — e.g.
  `AnomalyDetector.Reconcile(data, existing) → IReadOnlyList<DataAnomaly>` returning the **Open** flags to
  auto-resolve — computed from the same `found` set so the two can never disagree about what is currently
  wrong. Detect and Reconcile share the internal detection pass; do not run the three detectors twice.
- **`AnomalyScanner` does the persistence**, as it does for raised flags: it calls the detector, adds the new
  anomalies, and now also transitions the reconciled ones to `Corrected`. It already runs inside every write
  path's transaction, so reconcile happens on the same DELETE/PATCH that removed the cause.

### The reconcile rule

- Key on `(Kind, EntityType, EntityId)` — the same tuple the re-raise dedup already uses. One fact about one
  row.
- Auto-resolve a flag **iff** its status is `Open` **and** its key is absent from the current `found` set.
  - Status `Open` only: **Accepted and Dismissed are never touched.** They are the owner's decisions, and the
    existing rule already keeps the detector from re-raising them; reconcile must honour the same line, or
    "Accept" would mean nothing.
    Already-`Corrected` flags are terminal and equally untouched.
- Set on the transitioned flag: `Status = Corrected`, `ResolvedAt = timeProvider.GetUtcNow()` (never the
  caller's clock — the same rule the resolve endpoint follows), and a **system resolution note** that names why,
  e.g. `"Auto-resolved: the condition is no longer present (the underlying data was changed or removed)."` The
  note is what distinguishes a scanner-corrected flag from a human-corrected one in the audit trail, since both
  carry status `Corrected` by the owner's design choice.

### Interactions to preserve

- **The `ck_anomalies_resolved_iff_terminal` constraint** requires `resolved_at` to be non-null exactly when the
  status is not `Open`. Setting `ResolvedAt` alongside `Corrected` satisfies it; forgetting it is a 500, so the
  test must assert both move together.
- **Re-raise falls out for free.** `Detect` already excludes only non-`Corrected` existing flags from
  suppression, so a `Corrected` (including auto-corrected) flag does **not** suppress a future raise. Re-adding
  a bad fill therefore raises a fresh flag. No new code; the test asserts the existing behaviour holds for
  auto-corrected flags specifically.
- **Idempotence.** A scan that changes nothing must reconcile nothing — running twice in a row must not churn
  statuses or timestamps. This is guaranteed by keying on the current `found` set, but the test pins it.

### Verification

- Unit tests against the pure detector: a fixture with an Open flag whose entity is gone from the data →
  reconcile returns it; a fixture where the condition still holds → reconcile returns nothing; an Accepted flag
  whose condition is gone → reconcile leaves it alone.
- Write-path test through `AnomalyScanner` against real Postgres (Testcontainers), because the constraint and
  the transaction are exactly what a pure test cannot cover: raise a flag, delete its cause, scan, assert the
  flag is `Corrected` with `ResolvedAt` set and the row still present.
- Live confirmation on BT53 is already done in spirit (the stray fill this session), but re-run it clean once
  the auto-resolve exists: the hand-resolution should no longer be necessary.
- The five workbook defects' fixture is untouched — this adds no arithmetic and no new detector.

## External Dependencies (Conditional)

None. No new package, no new endpoint, no schema change — the `Corrected` status, the constraint, and the
scanner's transaction all already exist. This is a lifecycle rule, not new machinery.
