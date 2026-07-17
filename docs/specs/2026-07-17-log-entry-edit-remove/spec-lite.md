# Spec Summary (Lite)

Make every log's entries editable and removable from the UI, symmetrically with the fuel/service paths that
already have it: add the missing `PATCH` (fuel, mileage, tyres, wash) and `DELETE` (mileage, tyres, wash,
equipment) endpoints, and turn every add-only sheet into a dual add/edit opened by clicking a row, with a
ghost Delete and a two-step inline confirm in the footer. Every edit and delete re-runs `AnomalyScanner`,
keeps mirrored readings/expenses in step, and runs inside the retrying execution strategy. Also fixes three
adjacent traps — the expense mirror-refusal must also block service-mirrored rows, expense `PATCH`/`DELETE`
must re-scan, and multi-table edits must be wrapped in the execution strategy. Depends on
`2026-07-16-anomaly-lifecycle-reconcile` landing first so no delete orphans an Open flag.
