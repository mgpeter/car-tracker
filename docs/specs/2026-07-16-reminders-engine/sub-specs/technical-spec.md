# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-reminders-engine/spec.md

## Trigger evaluation — read the shared brain, never re-derive

- A reminder is not a new fact; it is the derived renewal and check state, read and counted. The evaluator is a
  **pure function** over `VehicleSummary` (`src/CarTracker.Shared/Metrics/VehicleSummary.cs`), taking the
  already-computed `RenewalSummary` and `CheckStatusSummary` and turning red/amber/overdue states into
  reminder items with a reason string. It computes no dates and reads no table — `IDerivedMetricsService`
  already did that, and doing it again is how the badge and the dashboard would diverge (DEC-002, §4).
- **The design lists five triggers; the domain already models them as two axes.** Reconciled:
  - *Renewal approaching* and *service due by date or mileage* → `RenewalSummary`. `Mot`, `Insurance` and
    `RoadTax` each carry `Urgency` (Red < 30 days, Amber < 60, README §3.1) and `DaysRemaining`;
    `NextServiceDate` carries the same, and `NextServiceMiles` covers "due by mileage". A firing renewal is one
    whose `Urgency` is Red (or Amber, if the design's amber digest is wanted).
  - *Checks overdue*, *wash cadence exceeded* and *tyre check overdue* → `CheckStatusSummary`. This is the
    reconciliation worth stating: **wash cadence and the tyre check are not separate derivations — they are
    check definitions.** "Wash & underbody rinse" is a 28-day check and "Tyre pressures" / "Tread depth" are
    30- and 90-day checks (see `CheckTemplate.cs`), so they surface as `CheckState` with `Status = Overdue`
    like any other check. The engine reads them off `CheckStatusSummary.Checks`; it does not invent a
    wash-cadence or tyre-check calculator, because the design's separate triggers are one axis in this schema.
- **`NeverLogged` is not a reminder.** A check that has never been performed is on the due axis's fourth state
  (`CheckStatus.NeverLogged`), not overdue — the same distinction the data-integrity spec kept blue off the due
  axis for. A reminder fires on *overdue*, which is a cadence that has lapsed, not one that never started.
- The design's *"Wash cadence exceeded — set in Settings → Reminders"* resolves to the **existing** wash check
  definition's interval, editable today in Settings → Checks (M1c). There is no second place to set it and this
  spec does not add one.

## No stored config, and therefore no `database-schema.md`

Checked deliberately, because "phase 1.5" sounds like it wants a settings table and it does not:

- **Every threshold already exists.** Renewal 30/60 days is in `RenewalUrgency`; the wash and tyre cadences are
  check-definition `IntervalDays`. A reminders config row would copy figures that already have a home, giving
  each a second source of truth — the drift this project exists to foreclose.
- **The badge is stateless.** Derived on read, it needs no "last evaluated" or "last sent" column. Push
  channels need a dedup record so email does not fire daily; that record is stored state, and it arrives with
  the channel that needs it (out of scope here), not now.
- Quiet hours and per-trigger toggles are the only config a v1 could want, and both are only meaningful once a
  channel interrupts you. Deferred with the channels.

So: no migration, no schema file. If a push channel later needs a `notification_log`, that channel's spec adds
it — this cut does not.

## The hosted background service

- A `BackgroundService` hosted **in `CarTracker.WebApi`** (the same process as everything else — one
  deployable, DEC-004/DEC-009), waking on a configured interval (a `TimeSpan` from configuration; a daily
  digest by default, matching the design's "daily digest" for checks). It uses `TimeProvider`, not
  `DateTime.Now`, so tests can advance the clock — the codebase already injects `TimeProvider` everywhere.
- Each tick: enumerate vehicles via `IVehicleMetricsLoader.ListVehicleIdsAsync`, call
  `IDerivedMetricsService.GetVehicleSummaryAsync` per vehicle, run the pure evaluator, and hand the fired
  items to each enabled `INotificationChannel`. With only the badge adapter registered this dispatch is a
  no-op push — the job still runs, still evaluates, and is where email/ntfy plug in.
- Resolve scoped services (`DbContext`, the metrics service) inside the tick via an `IServiceScopeFactory`, not
  from the singleton hosted service's constructor — a captured scoped `DbContext` is a classic hosted-service
  leak.

## `INotificationChannel` — the adapter seam DEC-006 requires

- One interface: `Task NotifyAsync(int vehicleId, IReadOnlyList<ReminderItem> fired, CancellationToken)`, plus a
  name and an enabled flag. Registered as a collection; the job dispatches to all enabled. This is the "channels
  are adapters" design made real, and it is what lets DEC-006 stay open — the choice becomes a new registration,
  not a change to the engine.
- **The badge adapter is the only implementation.** In-app delivery is a read, so the badge "channel" is really
  the reminders endpoint below; the adapter it registers is a thin in-app marker (or a no-op that records the
  in-app surface is live), present so the dispatch loop has exactly one member and the seam is exercised. Email,
  push and Assistant·MCP are named registration points with no implementation, so their absence is visible
  rather than assumed.

## The read endpoint and the badge

- A small dedicated endpoint (see `api-spec.md`) rather than reusing the garage summary: `GarageItem` already
  carries `OverdueCheckCount`, `NeverLoggedCheckCount`, `OpenAnomalyCount` and `RenewalsOk`, but those are
  *counts on separate axes*, not a unified list of fired reminders each with a reason. The reminder view needs
  the reason strings and the renewal-plus-check aggregation, which the garage shape does not carry — so the
  endpoint is justified, and it too reads `IDerivedMetricsService`, never a re-query.
- The shell badge count is that endpoint's firing length. It renders on the due/attention axis, **not the blue
  integrity axis** (`lib/status.ts`) and **not the orange accent** — a reminder is a due-status thing, and
  keeping the semantic axis off the structural accent is the rule `--accent` guards.
- `usePlate()` for the registration, never `plate={reg}`; the badge and any reminders view are new components
  and each must be axe-swept or exempted with a reason in `coverage.test.ts`, or the build fails.

## Verification

- The evaluator is a pure function and unit-tested against `VehicleSummary` fixtures: the fired set for BT53
  equals what its `RenewalSummary`/`CheckStatusSummary` imply — 7 overdue checks and the overdue tyre check
  fire, the wash stays quiet at day 14 of 28, the MOT (359 days after the service-history spec) does not fire.
  This is a parity assertion, the same shape as the MCP read-parity test: the reminder count and the dashboard
  cannot disagree because both read the one service.
- .NET tests run against real Postgres via Testcontainers applying migrations (not in-memory). The hosted
  service is tested by advancing `TimeProvider` and asserting a tick evaluates and dispatches; a throwaway stub
  channel asserts a fired reminder reaches every enabled adapter.
- The five-defect fixture is untouched — this spec adds no arithmetic; it reuses the derived service wholesale.
- `npm run gen:api` then `git diff --exit-code`: the reminders endpoint changes the OpenAPI contract, and the
  staleness gate must stay green.

## External Dependencies (Conditional)

None for this cut. `BackgroundService`, `TimeProvider` and DI are in the framework; the badge needs no client
library. A future email adapter would bring an SMTP/MailKit dependency and a push adapter an ntfy/Gotify client
— each named in its own registration when DEC-006 decides, and neither added here.
