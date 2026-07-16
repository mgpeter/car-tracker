# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-07-16-reminders-engine/spec.md

One new read endpoint, behind the gateway on one origin, requiring `X-Api-Key` (DEC-009). It reads
`IDerivedMetricsService` — never a second query — so the reminders it lists cannot disagree with the dashboard
that computed the same due state.

## Why a new endpoint and not the garage summary

`GET /api/vehicles` already returns `GarageItem.OverdueCheckCount`, `NeverLoggedCheckCount`,
`OpenAnomalyCount` and `RenewalsOk`. Those are counts on separate axes, not a unified list of *fired reminders
each with a reason*. The badge and the reminders view need the reason strings and the renewal-plus-check
aggregation, which the garage shape does not carry — so a dedicated endpoint, reading the same service the
garage projection reads.

## Endpoints

### GET /api/vehicles/{registration}/reminders

**Purpose:** The fired reminders for one vehicle, each with a human reason, and the badge count derived from
them. Read through `IDerivedMetricsService` and the pure evaluator, so the list is the derived renewal and
check state re-expressed, not a new computation.
**Parameters:** `registration` (path); `includeQuiet` (query, optional) — when true, also returns triggers that
are evaluated but **not** firing (the MOT at 359 days, the wash at day 14), so a settings view can show "would
fire / quiet" like the design panel; default false returns only what fires.
**Response:** `ReminderList { FiringCount: int, Items: ReminderItem[] }` where
`ReminderItem { Kind, Subject, Reason, Severity, Firing, DaysRemaining? }` — e.g.
`{ Kind: "TyreCheckOverdue", Subject: "Tread depth, all 4 tyres", Reason: "Overdue — 52 days, target 30", Severity: "Overdue", Firing: true }`.
**Errors:** 404 when the registration is unknown.
**Notes:** `Severity` is on the due axis (OK / DueSoon / Overdue for checks, and the renewal Red/Amber mapped to
the same axis), never the blue integrity axis and never the orange accent. `FiringCount` is what the shell
badge shows; it equals `Items.Count(i => i.Firing)`.

## No new write endpoint

Reminders are evaluated, not stored, so there is nothing to POST. Acknowledging or snoozing a reminder would be
stored state, and it only matters for a channel that pushes — deferred with the channels (DEC-006). The badge
clears itself when the underlying due state clears, because it is derived, not marked.

## No change to existing responses

The garage and dashboard payloads are unchanged. The badge could be added to `GarageItem` later as a
convenience, but the reminders endpoint is the source and the garage counts already imply the same state — a
duplicated `FiringCount` on the garage would be a second figure to keep in step, which is the drift this design
avoids. Left off deliberately.
