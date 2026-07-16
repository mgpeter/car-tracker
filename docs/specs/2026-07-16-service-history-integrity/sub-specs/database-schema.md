# Database Schema

This is the database schema implementation for the spec detailed in @docs/specs/2026-07-16-service-history-integrity/spec.md

**This file was added during execution, not planning.** The spec claimed there were no schema changes because
`ServiceRecord` and `DataAnomaly` both already exist in full. That is true and it was the wrong thing to check:
the gap is not in either entity, it is in the one that has to point at them.

## Change: `expense_entries.service_record_id`

```
ALTER TABLE expense_entries
  ADD COLUMN service_record_id integer NULL
    REFERENCES service_records (id) ON DELETE CASCADE;

CREATE INDEX ix_expense_entries_service_record_id
  ON expense_entries (service_record_id);
```

Configured on `ExpenseEntryConfiguration` alongside the existing `FuelEntryId` relationship, with
`DeleteBehavior.Cascade` — the same as fuel's.

## Rationale

`ExpenseEntry` carries `FuelEntryId` and nothing else. A fill mirrors into expenses and links back, which is
what closes the workbook's £163.16 gap by construction rather than by discipline (spec §3.2). A **service
record has no equivalent**, and the consequence is a trap rather than a missing feature:

`SpendCalculator` reads `ExpenseEntries` and nothing else. `ServiceRecord.Cost` is therefore invisible to
spend, cost-per-mile and every rollup. Without this column the service screen would accept £603.99 for BT53's
cambelt and no figure anywhere would move — so the honest options were to mirror it, or to make the user type
the cost twice into two screens and keep them in step by hand. The second is precisely the failure the fuel
mirror exists to prevent, and the workbook is the evidence for what happens.

`ON DELETE CASCADE` because the mirrored row is a shadow: deleting the record deletes the expense, exactly as
for a fill. The shadow cannot outlive its source.

Nullable because most expenses are neither a fill nor a service — they are typed, and both FKs are null. A row
can never have both set: it is mirrored from one thing or from nothing.

## What this does not change

- No new table. `data_anomalies` has had its full lifecycle since Phase 1 (`Status`, `ResolvedAt`,
  `ResolutionNote`, and the `ck_anomalies_resolved_iff_terminal` constraint); it has only ever lacked a reader.
- No change to `service_records`. `Type` stays free text — see the technical spec on why the MOT must be a
  choice in the UI rather than typed.
- The five defects' fixture is untouched: this adds no arithmetic.
