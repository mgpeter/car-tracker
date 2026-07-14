# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-15-fuel-basis-initial-mileage/spec.md

## Why the current design is wrong

`FuelEconomyCalculator` gates every interval on `previous.FillLevel == Full && current.FillLevel == Full`. That
makes an eyeballed gauge reading the arbiter of a receipt figure. Three consequences, all bad:

- **It trusts the wrong input.** Litres is a receipt number to 2dp. "About half" is a glance at a needle.
- **It misses what it should catch.** A mistyped odometer produces nonsense with a perfectly Full tank at both
  ends — the gate waves it through.
- **It discards real data.** A single partial fill invalidates *two* intervals: the one ending at it and the one
  starting from it. Nothing is measured across either.

And empirically it buys nothing here: the workbook records no fill level at all — its "Full tank / Half /
Quarter" columns hold computed range estimates (329 / 164.5 / 82.25 — one number ×1, ×½, ×¼), so the fixture's
`FillLevel.Full` is an assumption, not data.

## Technical Requirements

### FillLevel becomes descriptive

- `FuelEntry.FillLevel` → `FillLevel?`, nullable. It is currently `NOT NULL`, which forces a value nobody has.
- **No calculator may reference it.** Enforced by a test, in the manner of `NoDirectClockAccessTests` — a rule
  stated only in prose decays.
- The enum stays for the note it carries ("this was a splash"). It gets no `CHECK` change beyond nullability.

### MPG is computed for every measurable interval

```
milesSinceLast = current.Mileage - previous.Mileage
mpg            = milesSinceLast * 4.54609m / current.Litres      // unchanged: already exact litres
```

`MpgUnreliableReason` loses `PartialFill` and gains nothing — the two remaining members are structural facts,
not judgements:

| Reason | When | Kept because |
|---|---|---|
| `NoPreviousFill` | The first fill | There is no interval. Not a quality judgement — there is nothing to divide. |
| `NonMonotonicMileage` | `milesSinceLast <= 0` | Never divide by zero; never report a negative MPG as economy. |

`IsReliable` is therefore true whenever an MPG exists. Plausibility is a *separate* axis — see below. Two
different questions ("is there a figure?" and "does the figure make sense?") that the current design conflates.

### Plausibility band

```csharp
public const decimal MinPlausibleMpg = 10m;
public const decimal MaxPlausibleMpg = 70m;
```

- A per-fill MPG outside the band sets `IsPlausible = false` on `FuelEntryMetrics`.
- **Implausible figures are excluded from average, best and worst** — a 272 mpg splash must never become "best
  MPG" — but remain visible on their own entry. Marked, not deleted.
- Bounds are for a petrol K-series Freelander. Below 10 means a missed odometer entry or a fuel leak; above 70
  means a partial fill or a mistyped mileage. All 12 real intervals fall in 25.4–32.2, so the band is loose
  enough to be a genuine anomaly signal rather than a nag.
- Feeds the `ImplausibleMpg` anomaly kind that DEC-008 already reserved (`data_anomalies`, core-data-model task
  6). This spec computes the flag; writing the anomaly row is that task's job.

### Cumulative average MPG

```
span         = last.Mileage - first.Mileage
litresBurned = SUM(litres) - first.Litres
averageMpg   = span * 4.54609m / litresBurned
```

**Why `- first.Litres`.** The first fill filled a tank you had already burned before recording began; the fuel
you burned *across the span* is what you pumped after it. On the real history: 3,175 miles ÷ 494.47 L = **29.19
mpg**, against 29.14 for the mean of per-fill figures. Agreeing to 0.05 mpg is the tank-level noise washing out
— which is exactly why this needs no fill level.

- Needs ≥ 2 fills and a positive span; otherwise null.
- **Robust by construction**: the error is bounded by one tank's variation spread across the whole span.
- `AverageMpg` becomes this. `BestMpg`/`WorstMpg` stay per-fill (over plausible intervals) — they are about
  individual tanks by definition.
- Expose `PerFillAverageMpg` alongside. They agree here; a divergence would be a real signal that something is
  wrong, and it costs one field to be able to see it.

### MileageOrigin.Purchase

- New member. **Append it** — `MileageOrigin` is stored as a lowercase string via `HasConversion`, so ordinal
  position is not persisted, but the `CHECK` constraint enumerates the permitted values and must gain
  `'purchase'`.
- Distinguishes the founding reading from a hand correction. Without it, "where this car started" and "someone
  typed over this" are the same row.

### Opening reading on vehicle creation

- A domain service — `VehicleFactory` or equivalent in `CarTracker.Domain` — creates the `Vehicle` **and** its
  opening `MileageReading` at `(PurchaseDate, PurchaseMileage, Origin: Purchase)` in one operation.
- **Not** an EF interceptor or a `SaveChanges` hook: this is a domain rule about how a vehicle comes into
  existence, not a cross-cutting concern like audit stamping. An interceptor would also fire on imports and
  bulk paths where it may not be wanted.
- Both rows are written in one transaction. A vehicle without its opening reading is the state this spec exists
  to prevent.
- `Vehicle.PurchaseMileage` stays (README §2 lists it as a purchase fact). The reading is derived from it at
  creation. **They can drift if someone later edits one** — a real cost, accepted because the alternative is
  losing purchase mileage for a vehicle with no readings. A test asserts they agree at creation; keeping them
  in step afterwards is the edit path's problem, and is out of scope here.

## Consequences for existing tests

- `A_brand_new_vehicle_reports_unknowns_rather_than_zeroes` currently asserts `CurrentMileage` is null for a
  vehicle with no readings. That stays true of `DerivedMetrics.Compute` given empty data — the calculator is
  unchanged. What changes is that the *creation path* no longer produces that state.
- The three `PartialFill` theory cases in `FuelEconomyCalculatorTests` go away with the member.
- `A_partial_fills_inflated_mpg_is_excluded_from_best_and_worst` survives, but for a better reason: the splash
  is excluded because 272 mpg is implausible, not because a box was ticked. The assertion barely changes; the
  mechanism does.
- The workbook fixture's `FillLevel.Full` becomes inert. Its comment ("the one place the fixture asserts
  something the workbook does not say") should note that it no longer asserts anything.
- `DashboardReproductionTests.Average_and_worst_mpg_differ_...` needs its expected average updated from the
  per-fill mean to the cumulative 29.19.

## External Dependencies (Conditional)

None.
