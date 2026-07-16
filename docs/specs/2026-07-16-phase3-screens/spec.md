# Spec Requirements Document

> Spec: Phase 3 Screens — Tasks, Issues, Tyres, Wash, Budget, Equipment, Vehicle Info
> Created: 2026-07-16
> Status: Complete

> **Written after the fact.** These seven screens and their API were built straight from the roadmap in one
> session, before this spec existed, to answer "get all the designed screens covered". This document is the
> spec trail catching up with shipped, tested code — it records the decisions as they were made, not as a plan
> to execute. It is Complete on creation. The one genuinely deferred item (documents) is called out in Out of
> Scope with its own future spec.

## Overview

Give the remaining ten workbook sheets a home, so no sheet lacks an equivalent view (Phase 3's goal). Seven
screens shipped here — tasks, issues, tyres, wash, budget, equipment and vehicle-info — each deriving what the
design hardcodes, leaving only documents (file upload, unlike anything else) for a later spec.

## User Stories

### One to-do list, not two sheets

As the owner, I want DIY and Workshop tasks in one place with the garage bundle costed, so that I can see
whether it is worth booking a visit yet without adding up estimates by hand.

The workbook keeps DIY and Workshop as separate sheets, which is how the same job ends up on whichever one was
open. `MaintenanceTask.Kind` makes them one list — a task moves between them the moment it is priced — and the
bundle total (the design's hardcoded "£150 · 1 job") is the sum of the open Workshop estimates, computed so it
moves when a task is added.

### Watching is not doing

As the owner, I want a watchlist separate from my to-do list, so that "brake pipe corrosion, advisory since
2024, not failure yet" is tracked as something to keep an eye on rather than lost among jobs I intend to do.

An issue carries what a task does not: `LastChecked`, `CurrentObservation`, and `ActionIfWorsens` — the
decision made calmly in advance so it is not made in a hurry at the roadside. "Advisory since 2024" is only
useful if the screen says how long that has been, so the duration is derived, never stored.

### Every remaining log has a home

As the owner, I want pressures, washes, spend targets, kit and the reference card, so that the spreadsheet has
nothing left it does the app cannot.

Tyres record pressure and tread by corner with every field nullable (not measured is not flat, and the spare is
why the workbook counts 17 of 18 checks). Wash derives cadence from the gaps, not the dates. Budget makes
`GetBudgetSummaryAsync`'s variance visible for the first time, with a missing target rendering "no target"
rather than a bar at zero. Equipment tracks a fourth axis — existence — and vehicle-info is the one honestly
stored screen, where an oil grade is what the manual says, not a measurement.

## Spec Scope

1. **Tasks** — DIY/Workshop board grouped by status, derived bundle total and worst-case total, add/edit sheet;
   High priority rendered (the design has no rule for it).
2. **Issues** — the watchlist, worst-case cost derived from what is still monitored, derived "watched N months",
   `ActionIfWorsens` surfaced; add/edit sheet.
3. **Tyres, wash, equipment** — three logs behind `LogEndpoints`: tyres by corner (spare nullable), wash with
   derived cadence, equipment on the owned/on-order/to-order axis.
4. **Budget** — editable targets (the only stored numbers), derived YTD variance, period toggle, over-budget
   bars capped in geometry with the true figure in text; a missing target is not a target of zero.
5. **Vehicle info** — the stored reference card (specs, fluids, tyre pressures, policies-as-inputs); the
   countdowns stay on the dashboard, derived, never repeated here.

## Out of Scope

- **Documents** — upload, tag, link-to-record, viewer/download. The only Phase 3 screen not built here, because
  it needs file upload, which nothing else in the app does. Its own spec, later.
- **Reminders background job** — deferred; DEC-006 (notification channel) stays open.
- **Promote-a-task-to-a-service-record** — `MaintenanceTask.ServiceRecordId` exists and the field round-trips,
  but the promote *flow* is a later addition; the tasks screen does not drive it yet.
- **Reference-list management in settings** — garages and wash locations are created on first use
  (`ReferenceWriter`), but editing/merging the lists is the rest of Settings, not these screens.
- **Reordering quick-add / drag interactions** — the design's drag grips were cut, not ported (an affordance
  that does nothing is worse than its absence).

## Expected Deliverable

1. All seven screens route, render BT53's real data, and their add/edit sheets write through the API — verified
   live this session, every write path returning 201 including two reference names never seen before.
2. The derived figures the design hardcodes move with the data: the task bundle sums open Workshop estimates,
   wash cadence comes from the gaps, budget variance from the expense rows, issue duration from the first-noted
   date.
3. The full suite is green (236 .NET, 297 front-end at ship), every screen is axe-swept or exempt with a
   reason, and no screen renders the URL slug as a registration (`usePlate()`, guarded in `coverage.test.ts`).
