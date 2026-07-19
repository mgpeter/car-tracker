# Spec Tasks

## Tasks

- [x] 1. Trigger evaluation
  - [x] 1.1 Write tests: the evaluator over BT53's `VehicleSummary` fires on 7 overdue checks and the overdue tyre check, stays quiet on the wash (day 14 of 28) and the MOT (359 days); `NeverLogged` never fires
  - [x] 1.2 A pure evaluator over `RenewalSummary` + `CheckStatusSummary` producing `ReminderItem`s with a reason each — renewals by `Urgency`, service by date or `NextServiceMiles`, checks/wash/tyre off `CheckStatusSummary`
  - [x] 1.3 Assert parity: the fired set equals what the dashboard's own renewal/check state implies — no re-derivation, both read `IDerivedMetricsService`
  - [x] 1.4 Verify all tests pass

- [x] 2. The channel seam and the hosted service
  - [x] 2.1 Write tests: the `BackgroundService` ticks on an advanced `TimeProvider`, evaluates per vehicle, and dispatches to every enabled channel; a stub channel receives the fired items
  - [x] 2.2 `INotificationChannel` (name, enabled, `NotifyAsync`) plus the in-app badge adapter as the only implementation; email / push / Assistant·MCP left as named registration points
  - [x] 2.3 The `BackgroundService` in `CarTracker.WebApi`, resolving scoped services per tick via `IServiceScopeFactory`, waking on a configured interval
  - [x] 2.4 Verify all tests pass

- [x] 3. Reminders endpoint and the shell badge
  - [x] 3.1 Write tests: `GET .../reminders` returns fired items with reasons and a `FiringCount`; `includeQuiet` adds the non-firing triggers; the badge reads the count
  - [x] 3.2 The endpoint reading `IDerivedMetricsService` and the evaluator, behind `VehicleLookup`; regenerate the contract and TS types, staleness gate green
  - [x] 3.3 The shell/garage badge on the due axis — not blue integrity, not orange accent; `usePlate()`, axe sweep, coverage-guard exemptions with reasons
  - [x] 3.4 Verify all tests pass

- [x] 4. Prove it end to end on BT53
  - [x] 4.1 Confirm the shell badge count equals the dashboard's implied due state — the 7 overdue checks and the overdue tyre check fire, the wash stays quiet
  - [x] 4.2 Confirm the hosted service runs on its interval and evaluates; a throwaway stub second channel receives a fired reminder, with no change to the trigger logic
  - [x] 4.3 Confirm `GET .../reminders` lists each fired item with its reason, and `includeQuiet` shows the MOT and wash as evaluated-but-quiet
  - [x] 4.4 Full suite, both builds, codegen gate; update roadmap and CLAUDE.md
