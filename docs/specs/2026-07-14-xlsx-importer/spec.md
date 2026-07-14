# Spec Requirements Document

> Spec: XLSX Importer
> Created: 2026-07-14
> Status: Planning

## Overview

Import the existing 13-sheet workbook into the schema so no history is retyped, reading from the log sheets and recomputing rather than trusting the Dashboard's stored values. The Dashboard sheet is not an input — it becomes a fixture the import is validated against, and the four places where it disagrees with reality are the proof the import worked.

## User Stories

### Nothing is retyped

As the owner, I want every row of my workbook in the new system after one command, so that four months of fuel, service, and check history survives the move and I never reopen the spreadsheet.

Twelve of the thirteen sheets carry data: Vehicle Info, Expenses Log, Fuel Log, Service History, DIY To-Do, Workshop To-Do, Regular Checks, Wash Log, Tyre Log, Budget, Issues Watchlist, Equipment. Each maps to tables from the core data model spec. The import runs against a real file, reports what it did, and can be re-run from clean without leaving duplicates.

### Bad data arrives flagged, not silently

As the owner, I want the import to tell me what it found wrong, so that I can decide about the 83,000 mi service row rather than discovering it as a nonsense figure on the Dashboard months later.

The workbook contains at least one certain error: a Service History row dated 27 Jun 2026 logging 83,000 mi when current mileage is 80,712, almost certainly 80,300 mistyped. README §5.3 requires flagging anomalies rather than accepting them silently, and equally rather than rejecting the import. The row imports as written, with a flag attached and a report at the end.

### The old Dashboard proves the new one

As the developer, I want the import validated against the Dashboard's stored figures, so that every value the sheet got right is reproduced and every value it got wrong is demonstrably corrected.

The Dashboard is the best available test fixture: it is real, it was computed from this exact data, and four of its figures are verifiably wrong. Reproducing the correct ones proves fidelity; resolving the wrong ones to their verified values proves the recompute.

## Spec Scope

1. **Workbook reader** - Parse all 12 data sheets from the `.xlsx`, handling Excel serial dates and trailing blank rows.
2. **Entity mapping** - Map every sheet row to its entity from the core data model, including reference lists in side columns.
3. **Anomaly detection** - Flag mileage non-monotonicity, fuel cost discrepancies, and never-logged checks into a queryable table rather than the console.
4. **Import run tracking** - Record each import with counts and outcomes so a re-run is a deliberate act, not an accident.
5. **Dashboard validation harness** - A test suite asserting recomputed values against the Dashboard's figures, with the four known defects as explicit expected-mismatch cases.

## Out of Scope

- The derived-metrics service itself (its own spec: `2026-07-14-derived-metrics-service`). The importer stores inputs; it does not compute MPG or totals.
- Correcting the 83,000 mi row. It imports as written and flagged. Only the owner can decide whether it is 80,300.
- Importing the Dashboard sheet's values into any table.
- Ongoing/incremental sync with the workbook. This is one-off, per README §6.
- Document/PDF/photo import. The `Document` rows for existing files are Phase 3.
- Export back to Excel (Phase 5).
- A UI for the import or the anomaly report. Console output plus a queryable table is enough for a one-off.

## Expected Deliverable

1. A single command imports the real workbook into an empty database and prints a summary: rows read, rows written, anomalies flagged, per sheet.
2. After import, the four Dashboard defects resolve to their verified values — MOT expiry 8 Jul 2027 (not 6 Aug 2026), total litres 556.47 (not 1,112.94), fuel YTD £888.86 (not £725.70), current mileage 80,712 (not 80,705) — each asserted by a test.
3. The anomaly table contains the 83,000 mi service row flagged as non-monotonic, and *Spare tyre pressure* is present as a check definition with zero logs.
