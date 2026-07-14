# Spec Requirements Document

> Spec: Fuel Basis and Initial Mileage
> Created: 2026-07-15
> Status: Planning

## Overview

Make actual litres the sole basis of every fuel figure, and stop a three-way tank-level guess from deciding whether a figure counts. Judge each MPG on whether the number is physically plausible instead, and give every vehicle an opening odometer reading at creation so current mileage is known from the moment it exists.

## User Stories

### The figures rest on what I measured, not what I guessed

As the owner, I want MPG to depend only on litres and miles — both of which I record exactly — so that no figure is discarded or kept on the strength of an eyeballed fuel gauge.

Litres pumped is a number from a receipt, accurate to two decimals. "The tank was about half full" is a glance at a needle. The current service treats the second as load-bearing: an interval is only counted if the tank was marked Full at both ends. That makes a soft observation the gatekeeper of a hard one, and it is backwards. The fill level should be a note, and the arithmetic should stand on the litres.

### Nonsense is caught by being nonsense

As the owner, I want an implausible MPG flagged because it is implausible, not because I ticked the wrong box, so that a mistyped odometer or a missed fill gets caught however it happened.

Litres added only equals fuel burned when the tank was at the same level at both ends. A five-litre splash after 300 miles computes to 272 mpg — from exact litres, correctly, and it is not a real figure. But a mistyped odometer produces the same kind of nonsense with a perfectly full tank, and the fill-level gate would miss it entirely. A plausibility band on the computed number catches both, and needs nothing from the gauge.

### A new car knows where it started

As the owner, I want a vehicle to have its opening odometer reading the moment I add it, so that the garage card shows a mileage instead of a blank until I happen to log a fill.

`Vehicle.PurchaseMileage` records what the odometer read at purchase, but current mileage derives from `MileageReading` — and a newly created vehicle has none. So the purchase mileage sits in the database while every derived figure reports null. Creating the vehicle should create the reading.

## Spec Scope

1. **Fill level becomes descriptive** - `FuelEntry.FillLevel` becomes nullable and is read by no calculator; it survives only as a note.
2. **Plausibility band replaces the fill-level gate** - a per-fill MPG outside 10–70 is flagged `ImplausibleMpg` and excluded from best/worst, per README §3.2 and DEC-008.
3. **Cumulative average MPG** - the headline average becomes total distance ÷ total litres actually pumped across the span, which needs no tank-level information at all.
4. **`MileageOrigin.Purchase`** - a new origin for the founding reading, distinguishable from a later hand correction.
5. **Opening reading on vehicle creation** - creating a vehicle creates a `MileageReading` at its purchase date and mileage.

## Out of Scope

- The add-car **UI** (Phase 2, DEC-007). This spec provides the domain rule and the service that enforces it; the form that calls it comes later.
- Retiring `Vehicle.PurchaseMileage`. README §2 lists it as a purchase fact alongside price, and a vehicle with no readings would otherwise have no purchase mileage either. It stays, with the reading derived from it at creation.
- A per-vehicle plausibility band. One constant serves one petrol Freelander; revisit when a second vehicle with a different powertrain exists.
- Estimated range on the current tank (README §8, deferred) — the only feature that would genuinely want a tank-level reading.
- Re-transcribing the workbook fixture. It records every fill as `Full`, which becomes a no-op once nothing reads the field.

## Expected Deliverable

1. `FuelEconomyCalculator` computes MPG for every interval with a measurable distance, regardless of fill level, and no calculator references `FillLevel`.
2. A 5 L splash after 300 miles yields ~272 mpg, flagged `ImplausibleMpg`, absent from best/worst, and still visible on its entry — while all 12 real intervals (25.4–32.2 mpg) pass unflagged.
3. Average MPG reads **29.19** on the workbook fixture (3,175 miles ÷ 494.47 L), and creating a vehicle at 76,632 mi yields a current mileage of 76,632 rather than null.
