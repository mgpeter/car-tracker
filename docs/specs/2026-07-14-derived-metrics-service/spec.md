# Spec Requirements Document

> Spec: Derived Metrics Service
> Created: 2026-07-14
> Status: Planning

## Overview

Build the single service that computes every derived figure in the system — mileage, fuel economy, spend, renewals, check status, budget variance — so the web API and MCP server call identical logic and a metric can never disagree with itself across surfaces. This is the shared brain README §4 requires, and correctness here matters more than anywhere else in the project.

## User Stories

### One number, one answer

As the owner, I want the Dashboard and the assistant to give me the same figure, so that "what's my MPG" is never a different number depending on where I ask.

README §4 requires centralising these calculations in one service. The failure this prevents is not hypothetical: the workbook already has fuel YTD at £725.70 on one sheet and £888.86 on another, because two places computed the same concept differently. Two code paths would reproduce that. One service means the question cannot arise.

### The numbers are right

As the owner, I want the figures proven by tests against my real history, so that I can trust a red MOT countdown enough to act on it.

The old Dashboard is the fixture: it was computed from this exact data, and four of its figures are verifiably wrong. Every figure it got right must be reproduced; every figure it got wrong must resolve to the verified value. That is the definition of done.

Since DEC-008 dropped the importer, the workbook's rows are transcribed by hand into a C# fixture rather than read from the file. The figures are unchanged — only how they reach the test. `archive/…Freelander_BT53AKJ_Tracker.xlsx` remains the source of truth, and the transcription must be checked against it.

### Uncertainty is visible

As the owner, I want the service to tell me when a figure is unreliable, so that a suspiciously good MPG reads as a missed fill rather than good news.

MPG between two fills is only meaningful when both were full-to-full. A check that has never been logged is not OK and not overdue. A mileage history that goes backwards has no single honest "current" value. In each case the service must express the uncertainty rather than pick a number and present it as fact.

## Spec Scope

1. **Mileage metrics** - Current mileage, miles since purchase, and explicit handling of non-monotonic history.
2. **Fuel economy** - Per-fill MPG (UK), L/100km, miles since last, fleet average/best/worst, total litres, average price per litre, with full-to-full validity.
3. **Spend rollups** - Totals by category and by Dashboard group, YTD and since-purchase, monthly average, cost-per-mile.
4. **Renewals and due dates** - Days-to-renewal for MOT, insurance, tax, and next service, by date and by mileage, with the §3.1 colour thresholds.
5. **Check status and budget variance** - Per-check OK/DueSoon/Overdue/NeverLogged from last log plus interval; budget YTD actual, remaining, and % used.

## Out of Scope

- Any HTTP endpoint or controller. This spec produces a service, not an API.
- Any UI. The Dashboard that consumes this is Phase 2.
- MCP tools. Phase 4 calls this service; it does not shape it.
- Caching or memoisation of any kind. DEC-002 forbids it, and one car's data does not need it.
- The reminders background job (Phase 3). It will call this service's due-date logic; it is not part of it.
- Estimated tank range. README §5.2 wants it for MCP and §8 defers it for the Dashboard; it needs a tank-capacity figure not currently modelled.
- Trend charts and time series (README §8, deferred).

## Expected Deliverable

1. A `CarTracker.Domain` service returning every figure README §3.1 lists, with unit tests covering each formula against hand-computed values.
2. Run against a hand-authored fixture of the real workbook figures (DEC-008) at a fixed reference date of 2026-07-14, the service reproduces the Dashboard's correct figures and resolves its four defects: MOT 8 Jul 2027 (359 days), total litres 556.47, fuel YTD £888.86, current mileage 80,712.
3. Tests prove the three uncertainty cases: MPG across a partial fill is flagged unreliable, a never-logged check reports `NeverLogged` rather than a status, and non-monotonic mileage is reported alongside the derived current value.
