# Spec Summary (Lite)

Handle fills that don't fill the tank. A `Full` (or unrecorded) fill closes the tank and computes MPG
tank-to-tank; a `Half`/`Quarter` fill defers its MPG and accumulates its litres and miles into the next full
fill, which posts one correct figure over the whole span. Partial fills are a calm, labelled "MPG pending"
state — never a discarded interval and never an anomaly — and the fuel summary keeps the in-progress tank
(fills, miles, litres since the last full fill) in view. All-full history is unchanged to the penny; the change
is pure domain logic with no new database column.
