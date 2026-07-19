# Spec Requirements Document

> Spec: Reminders — background job with a pluggable channel
> Created: 2026-07-16
> Status: Complete

## Overview

Build the reminders engine README §4 calls "phase 1.5": a hosted background service that flags renewals within
N days, service due by date or mileage, checks overdue, wash cadence exceeded and tyre check overdue —
evaluated off the **same derived service the dashboard reads**, and delivered through a **pluggable channel**.
DEC-006 (email vs push vs UI badge) is still open, so this ships a UI-badge-first cut that needs no channel
decision, with the channel modelled as an adapter so email or push land later as a plug-in rather than a
rewrite.

## User Stories

### One badge, the same truth as the dashboard

As the owner, I want a reminder count in the app that agrees with what the dashboard already shows, so that
"needs attention" is one number computed one way, not a second opinion that can drift from the first.

The mission's whole claim is that a figure cannot disagree with itself across surfaces (DEC-002, §4). A
reminder is not a new fact — it is the *derived* renewal and check state, read and counted. So the badge reads
`IDerivedMetricsService`, exactly as the dashboard's attention panel does, and BT53's badge fires on the same
7 overdue checks and the same overdue tyre check the dashboard is already showing. It re-derives nothing, and
because it is derived on read it needs no stored state at all.

### A channel I can choose later without rebuilding

As the owner, I want the reminder logic to fire regardless of how it is delivered, so that deciding between
email, push and a badge later costs an adapter and not a rewrite.

The design says it in as many words — *"Channels are adapters. Triggers fire regardless; today they only
surface in-app. Wiring a channel later changes delivery, not logic."* DEC-006 deferred the channel choice and
required exactly this pluggability. The trigger evaluation and the delivery are separated by an
`INotificationChannel` seam; the only adapter built now is the in-app badge, which needs no SMTP server, no
push topic, and no decision.

### A job that fires when nobody is looking

As the owner, I want a background job that can notice an overdue renewal while the browser is closed, so that
the reminder does not depend on my happening to open the app.

The badge is a read — it works whenever the UI is open. A *push* channel must fire without the UI, which is why
README §4 asks for a Hosted Service. That service is built now as the seam: it wakes on a schedule, evaluates
the same triggers through the same derived service, and dispatches fired reminders to every enabled channel.
With only the badge adapter registered it has nothing to push and effectively no-ops — but it exists, is
tested, and is where email or ntfy plug in.

## Spec Scope

1. **Trigger evaluation** — a pure function over `VehicleSummary` (`RenewalSummary` + `CheckStatusSummary`)
   producing reminder items with a human reason each: renewals within threshold, service due by date or
   mileage, checks overdue, wash cadence exceeded, tyre check overdue. No new derivation.
2. **The hosted background service** — the `BackgroundService` README §4 names, waking on a configured
   interval, iterating the vehicles, evaluating through `IDerivedMetricsService`, and dispatching to the
   enabled channels.
3. **`INotificationChannel`** — the pluggable adapter interface the design's "channels are adapters" framing
   describes, with a **UI-badge adapter as the only implementation**, and email / push / Assistant·MCP left as
   future registrations.
4. **The badge in the shell** — a live reminder count surfaced in the shell and on the garage card, derived on
   read so it always agrees with the dashboard.
5. **A reminders read endpoint** — the fired items with their reasons, feeding the badge and a settings/dashboard
   view of what would fire and why.

## Out of Scope

- **Email/SMTP and ntfy/Gotify adapters.** Deferred until DEC-006 picks a channel — building both now would be
  guessing at a setup that is not yet known. They also each need a "last sent" record to avoid firing daily
  forever, which is stored state this stateless badge cut deliberately does not carry; that table arrives with
  the channel that needs it, not before.
- **The Assistant·MCP channel.** A future adapter pointed at the MCP server, which is its own spec and does not
  yet exist — a notification target cannot be wired to a surface that has not been built.
- **Quiet hours and per-trigger on/off toggles.** These need stored config, and they are only meaningful for a
  channel that interrupts you — a badge is glanced at, not pushed. Ship all triggers on; add the config when a
  push channel makes silencing one worth storing.
- **A thresholds/config table.** Every threshold already exists in the domain: renewal 30/60 days in
  `RenewalUrgency` (README §3.1), the wash 28-day and tyre-check 30/90-day cadences as **check-definition
  intervals** editable today in Settings → Checks. Copying them into a reminders config row makes a second
  source of truth that can drift from the first — the exact failure this project exists to prevent.
- **Re-deriving due state inside the job.** Forbidden. The job calls `IDerivedMetricsService` like the
  dashboard; a parallel query is how the badge and the dashboard would start disagreeing (DEC-002, §4).

## Expected Deliverable

1. BT53's shell shows a live reminder badge whose count equals what the dashboard's own renewal and check state
   implies — because both read `IDerivedMetricsService` — firing on the 7 overdue checks and the overdue tyre
   check the design panel already predicts, and staying quiet on the wash (day 14 of 28).
2. The hosted service runs on its interval and evaluates the same triggers; registering a throwaway stub second
   channel proves a fired reminder reaches every enabled adapter, with no change to the trigger logic — the
   adapter seam works.
3. `GET .../reminders` returns the fired items each with a human reason ("Tyre check overdue — 52 days, target
   30"; "MOT — 359 days, OK, not firing"), and the badge count is that list's firing length.
