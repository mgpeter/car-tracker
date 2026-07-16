# Spec Summary (Lite)

Build README §4's "phase 1.5" reminders engine: a hosted `BackgroundService` that flags renewals within N days,
service due by date or mileage, checks overdue, wash cadence exceeded and tyre check overdue — evaluated as a
pure read over `VehicleSummary` (`RenewalSummary` + `CheckStatusSummary`), never re-derived, so the reminder and
the dashboard agree because both call `IDerivedMetricsService`.

DEC-006 (email vs push vs UI badge) is still open, so this ships a UI-badge-first cut that needs no channel
decision: the badge is derived on read and stateless, the delivery sits behind a pluggable `INotificationChannel`
seam with only the badge adapter built, and email / push / Assistant·MCP are left as future registrations. No
config table — every threshold already lives in the domain (renewal 30/60 days; the wash and tyre cadences as
check-definition intervals) — so duplicating them would only invite drift.
