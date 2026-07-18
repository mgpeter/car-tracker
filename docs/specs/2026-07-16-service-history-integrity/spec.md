# Spec Requirements Document

> Spec: Service History & Data Integrity
> Created: 2026-07-16
> Status: Complete

## Overview

Give service records a screen and `DataAnomaly` its first read path, so that the two workbook defects still
unprovable in the running app — the stale MOT and the 83,000 mi row — can be entered, flagged and seen. These
two features are specced together because neither demonstrates the product's thesis alone: one supplies the
record that trips the detector, the other is where the flag surfaces.

## User Stories

### The MOT stops being unknown

As the owner, I want to log the MOT pass, so that the expiry countdown derives itself and can never go stale.

BT53's dashboard reads **MOT · Not set** today. Not because the test has not happened — it passed on 8 Jul 2026
at 80,705 mi — but because `RenewalCalculator` derives the expiry from the latest `type = 'MOT'` service record
and there is no screen that can create one. The workbook's answer to the same question is a *stored* 6 Aug 2026,
23 days away, red: a countdown for a renewal already done. Entering the pass record here replaces "Not set" with
8 Jul 2027 / 359 days, computed, and the settings screen's MOT seed row disappears on its own because a record
now outranks it.

### The flag that proves the premise

As the owner, I want a mistyped mileage to be flagged rather than absorbed, so that I can see the app noticed
and decide myself.

The workbook's Service History logs 83,000 mi on 27 Jun 2026 against a current 80,712 — almost certainly 80,300
mistyped. A spreadsheet has one number and a typo in it is permanent. Entering that row here raises exactly one
`MileageNonMonotonic` anomaly, the odometer does not move (current mileage is the newest reading *by date*, not
the largest), the row is kept, and the integrity queue explains which two figures disagree. Nothing is rejected
and nothing is silently corrected.

### Closing a flag with a reason

As the owner, I want to say what I decided about a flag, so that the queue is a record rather than a nag.

An open flag is resolved as **Corrected**, **Accepted** or **Dismissed** with a note. The distinction is
load-bearing and already in the schema: a Corrected flag re-raises if the condition returns, because the fix did
not hold; Accepted and Dismissed stay down, because the answer was "I know, and it is fine". Today
`AnomalyScanner` writes flags on every write path, and the only thing that reads them back is a `COUNT` for the
garage card — so the app can tell you how many flags a vehicle has and nothing whatever about what any of them
is, which is arguably worse than not counting.

## Spec Scope

1. **Service history API** — `GET/POST/PATCH/DELETE /api/vehicles/{reg}/service`, writing a mileage reading per
   record and running the detectors on every write, like every other log.
2. **Service history screen** — the record list on `<DataTable>`, an add/edit sheet, and the MOT record made
   visibly the source the dashboard's countdown derives from.
3. **Anomaly read path** — `GET /api/vehicles/{reg}/anomalies` and `PATCH .../anomalies/{id}`, plus
   `DataAnomaly` reaching `VehicleMetricsData` so the summary can carry an open-flag count.
4. **Data integrity screen** — the queue, grouped by status, each flag naming the two figures that disagree and
   linking to the record that caused it, with resolve/dismiss actions.
5. **Dashboard integrity panel** — the section M1d deliberately left out for having no data source, now with
   one.

## Out of Scope

- **Reminders background job** — deferred; DEC-006 (notification channel) stays open and undecided.
- **Tyre and wash logs** — roadmap item 2 groups them with service history; they share no logic with it and land
  in a later Phase 3 spec.
- **Tasks, issues, budget, equipment, documents, vehicle-info** — the rest of Phase 3.
- **New detectors.** The three that exist ship as they are: mileage monotonicity, implausible MPG, and the
  fuel-cost discrepancy. The design's DETECTORS panel also lists "Check never logged", which is **not an
  integrity flag** — it is `CheckStatus.NeverLogged` on the due axis, and it stays there.
- **Promote-to-service-record** from a task — that belongs with the tasks spec, which owns the source side.
- **Editing a record's mirrored mileage reading directly.** As with the fuel mirror, the record is the source
  and the reading is its shadow.

## Expected Deliverable

1. Entering BT53's MOT pass (8 Jul 2026, 80,705 mi, next due 8 Jul 2027) through the UI changes the dashboard's
   MOT from **Not set** to **8 Jul 2027 · 359 days · OK**, and the settings seed row disappears — with no
   stored expiry anywhere.
2. Entering the 83,000 mi service record raises **exactly one** flag; the odometer stays at 80,712; the
   integrity panel and the queue both show it; and resolving it as Accepted with a note clears it from the
   dashboard without deleting the row.
3. The three detectors are visible on one screen, and the queue distinguishes Corrected from Accepted from
   Dismissed — a Corrected flag whose condition returns raises again.

## Found during execution — a follow-up, out of scope

`AnomalyScanner` **adds** flags for conditions it finds on each write, but does not **retract** an open flag
when the data that caused it is later deleted. Deleting the fill behind an implausible-MPG flag left the flag
open, pointing at a row that no longer exists — it had to be resolved by hand as Corrected. That is a real gap
in the detector's lifecycle, but it is the detector's semantics rather than this spec's surface, and it wants
its own decision (retract silently? mark auto-corrected? leave for the human?). Recorded here, not fixed
here. **Now built** (2026-07-17) — see `docs/specs/2026-07-16-anomaly-lifecycle-reconcile/`: `AnomalyScanner`
auto-resolves Open flags to `Corrected` with a system note when the next scan finds their condition gone, keeps
the row, and never touches Accepted/Dismissed. The hand-resolution that prompted this note is no longer needed.
