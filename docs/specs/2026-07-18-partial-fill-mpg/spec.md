# Spec Requirements Document

> Spec: Partial Fills and Tank-to-Tank MPG
> Created: 2026-07-18
> Status: Planning

## Overview

Let a fill that did not fill the tank defer its MPG instead of computing a wrong one, and gather the deferred
litres and miles into a single correct figure at the next fill to full. A partial fill is a normal, expected
event — the app should say so plainly and keep the tank's running total in view — not a silent corruption of
two economy figures.

## The problem, precisely

Per-fill MPG is a **tank-to-tank** measurement: litres added at a fill equals fuel burned since the previous
fill *only when the tank was at the same level at both ends*. The fuel-basis spec (2026-07-15) made litres the
sole basis of MPG and — correctly — stopped a soft three-way gauge-guess from *discarding* intervals, judging
each figure by a 10–70 plausibility band instead. But it computes every interval **as if every fill were full**.
A genuine partial fill breaks that assumption in two places at once:

- The interval **ending at** the partial reads **too high** — fewer litres were added than were burned, so
  miles ÷ litres overstates economy.
- The interval **starting from** the partial reads **too low** — the next full fill tops up both the miles just
  driven *and* the deficit the partial left, so miles ÷ litres understates economy.

The 10–70 band catches only the gross cases. A part-tank on a car that normally returns ~30 mpg can read 45 then
22 — both *inside* the band, both plausible, both wrong — and both land in Best/Worst and the per-fill average.
The cumulative `AverageMpg` (total miles ÷ total litres across the span) already self-corrects and is untouched;
this spec fixes the **per-fill** figures, which are the ones a partial fill corrupts.

This is not a reversal of the fuel-basis decision — it is its completion. That spec's objection was that a soft
gauge-reading was used as a *gate that threw data away*. Here the marker is a **hard fact the driver knows for
certain** — did the pump click off at the brim, or not — and deferred litres are **accumulated, never
discarded**. No figure is lost; a correct one simply arrives one fill later.

## User Stories

### A half-fill doesn't lie about my economy

As the owner, I want a fill I didn't take to full to *not* produce an MPG figure, so that a splash of fuel
before a motorway run doesn't post a fake 45 mpg and then a fake 22 mpg either side of it and drag my averages
around.

I stop for £20 of fuel because the tank was low and I'm in a hurry. That fill measures nothing on its own — the
tank isn't back to a known level. The next time I fill to the brim, the miles I've driven since my *last* brim
fill, over *all* the litres I've added since then, is a real number. The app should wait for that fill and then
show one honest figure covering the whole stretch.

### The app tells me what's going on

As the owner, I want a partial fill clearly labelled and the still-open tank kept in view, so that a missing MPG
reads as "waiting for your next full fill", not as a bug or lost data.

When I log a partial, its row should say so and show "MPG pending — next full fill", not a blank that looks
broken. Somewhere I should be able to see the tank in progress: how many fills and how many miles have piled up
since my last brim fill, and how many litres they've cost — so the deferred figure isn't a mystery when it lands.

### Recording it is one tap in the common case

As the owner, I want the fill sheet to assume I filled to full, so that logging the normal case is unchanged and
I only do extra work on the rare partial.

Most fills are to the brim. The sheet should default to "filled to full" and let me flip it to Half or Quarter
when it wasn't — the fill level I already record, now doing real work.

## Spec Scope

1. **Fill level becomes load-bearing again — as a hard binary, not a gate.** `Full` (and an unrecorded level)
   *closes the tank*; `Half`/`Quarter` mark a *partial* that defers MPG. No interval is discarded; deferred
   litres accumulate into the next measured span.
2. **Tank-to-tank MPG across the open segment.** A fill-to-full computes one MPG from the miles since the
   previous full fill over the sum of litres added at every fill in between (partials included). On all-full
   history this is byte-for-byte the figure computed today.
3. **A pending state for deferred fills.** A partial fill (and any fill before the first full fill) carries no
   MPG and a new `AwaitingFullTank` reason — distinct from "no previous fill" and "odometer didn't advance".
4. **An open-tank summary.** The vehicle fuel summary exposes the in-progress tank: fills, miles and litres
   accumulated since the last full fill, so the fuel screen and dashboard can show "part-tank in progress".
5. **UI labels for both states.** The fills table shows "MPG pending — next full fill" on a partial and, on a
   grouped full fill, a note that the figure spans more than one fill; the add-fill sheet defaults to full and
   the live MPG preview reflects deferral.
6. **Anomaly behaviour follows automatically.** `ImplausibleMpg` already reads the calculator's per-entry
   plausibility, so partials (no figure) stop tripping false flags and the band now judges the grouped figure —
   with only the flag's wording to update. A partial fill never itself raises a flag.

## Out of Scope

- **Changing the marker's shape.** The three-way `FillLevel` (Full/Half/Quarter) stays; only its *role*
  changes. A dedicated boolean was considered and set aside — Half/Quarter carry a note about *how* partial and
  cost nothing to keep, and the calculation reads only "is this Full/unrecorded, or not".
- **Estimated range on the current tank** (README §8, still deferred) — the one feature that would want an
  actual gauge level rather than a fill-to-full marker.
- **A database column or any stored figure.** MPG, the grouping, and the open-tank totals are all derived on
  read (§1). This spec adds no migration.
- **A per-vehicle plausibility band.** Still one constant for one petrol Freelander; revisit with a second
  vehicle.
- **Flagging a long-open tank.** A tank left open across many fills is a calm pending state, not an anomaly.
  A future threshold-based nudge is noted in the technical spec but not built here.
- **Re-transcribing the workbook fixture.** Every fixture fill is `Full`, so it closes every tank and every
  figure is unchanged; the fixture stays as-is and gains one deliberate partial-fill test case built in code.

## Expected Deliverable

1. Logging a Half fill records it with no MPG and an `AwaitingFullTank` reason; the fills table shows
   "MPG pending — next full fill" on that row and the fuel summary reports a part-tank in progress (its fills,
   miles and litres). A subsequent Full fill posts a single MPG computed over the whole span — miles since the
   last full fill ÷ all litres added since — and the pending state clears.
2. The all-`Full` workbook history is unchanged to the penny: `AverageMpg` still reads 29.19, every per-fill
   figure matches today, and Best/Worst are identical — proving the grouping is a superset that reduces to the
   current behaviour when no tank is ever left partial.
3. A constructed A(full)→P(half)→B(full) sequence yields no MPG at P, one plausible figure at B equal to
   `(B.mi − A.mi) × 4.54609 ÷ (P.litres + B.litres)`, and neither a false `ImplausibleMpg` flag on P nor the
   two off-band figures the current calculator would post either side of it.
