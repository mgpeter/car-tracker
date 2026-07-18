# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-18-partial-fill-mpg/spec.md

## Where this sits against the fuel-basis spec

The fuel-basis spec (`2026-07-15-fuel-basis-initial-mileage`) removed a `previous.FillLevel == Full &&
current.FillLevel == Full` **gate** that discarded any interval touching a non-full fill, and it was right to:
that gate rested a hard receipt figure on a soft gauge glance and threw away real data. It replaced the gate
with a plausibility band and a per-fill MPG that assumes every fill tops the tank.

This spec keeps everything that decision established and adds the one thing it deferred: **what to do when a
fill genuinely was not to full.** The answer is not to gate (discard) but to **defer and accumulate** — no data
is lost, and the marker is used as a hard binary ("did the tank get closed?") rather than a soft magnitude
("how full, roughly?"). `FillLevel` becomes load-bearing again, but for grouping, not gating. Half-vs-Quarter
is irrelevant to the arithmetic; only *Full/unrecorded vs not* is read.

## Technical Requirements

### FillLevel semantics

`FuelEntry.FillLevel` stays `FillLevel?` (Full/Half/Quarter, nullable — unchanged column, no migration). Its
meaning to the calculator becomes:

| `FillLevel` | Role | MPG |
|---|---|---|
| `Full` | **Closes the tank** | Computes across the open segment |
| `null` (not recorded) | **Closes the tank** | Computes across the open segment |
| `Half` | Partial | Deferred (`AwaitingFullTank`) |
| `Quarter` | Partial | Deferred (`AwaitingFullTank`) |

**Null is treated as a closing fill.** This is deliberate and must be covered by a test. It is the only choice
that (a) reproduces every historical figure — all 13 fills are `Full`, and any future unmarked fill behaves as
the code did before this spec — and (b) matches the add-fill sheet's default-to-full. "Unrecorded" is not
"partial"; a driver who leaves the field alone is asserting the normal case, which is what the default encodes.
A helper makes the rule single-sourced:

```csharp
// In FuelEconomyCalculator. The only question the arithmetic asks of FillLevel.
private static bool ClosesTank(FuelEntry f) => f.FillLevel is null or FillLevel.Full;
```

### The tank-to-tank walk

`FuelEconomyCalculator.Calculate` replaces its pairwise `Measure(current, previous)` loop with a single forward
walk that carries an **open segment**: the anchor (the last closing fill) and the litres and fill-count piled up
since it.

```
anchorMileage = null      // odometer at the last closing fill; null until the first closing fill
segmentLitres = 0m        // litres added since the anchor (the anchor's own litres are NOT included)
segmentFills  = 0         // fills since the anchor, inclusive of the closing one

for each fill f in order (EntryDate, then Mileage):

    if anchorMileage is null:
        # No closing baseline established yet — nothing to measure from.
        emit Unmeasurable(f, reason = NoPreviousFill)
        if ClosesTank(f): anchorMileage = f.Mileage; segmentLitres = 0; segmentFills = 0
        continue

    segmentLitres += f.Litres
    segmentFills  += 1

    if not ClosesTank(f):
        # Partial: defer. Its litres stay in the open segment for the next closing fill.
        emit Unmeasurable(f, reason = AwaitingFullTank)
        continue

    # Closing fill: measure the whole open segment.
    miles = f.Mileage - anchorMileage
    if miles <= 0:
        emit Unmeasurable(f, reason = NonMonotonicMileage)
    else:
        mpg  = miles * 4.54609m / segmentLitres
        l100 = segmentLitres * 100m / (miles * KmPerMile)
        emit Measured(f, mpg, l100, segmentMiles = miles, spannedFillCount = segmentFills,
                      isPlausible = mpg in [MinPlausibleMpg, MaxPlausibleMpg])

    # This fill is the new anchor whether or not it measured (a non-monotonic closing fill still closes).
    anchorMileage = f.Mileage; segmentLitres = 0; segmentFills = 0
```

**Reduces to today on all-full history.** Every fill closes the tank, so each segment is exactly one fill:
`anchor` is the previous fill, `segmentLitres = f.Litres`, `segmentFills = 1`, `miles = f.Mileage −
previous.Mileage`, and `mpg = miles × 4.54609 ÷ f.Litres` — identical to the current `Measure`. `spannedFillCount`
is 1 for every historical figure. This is why the fixture and the 29.19 average do not move.

**The worked partial case.** A(full)→P(half)→B(full):
- A: `anchorMileage` was null → `NoPreviousFill`, then A becomes the anchor.
- P: anchor is A; `segmentLitres = P.L`, `segmentFills = 1`; not closing → `AwaitingFullTank`, no figure.
- B: `segmentLitres = P.L + B.L`, `segmentFills = 2`; closing → `mpg = (B.mi − A.mi) × 4.54609 ÷ (P.L + B.L)`,
  `spannedFillCount = 2`; B becomes the anchor.

**A trailing partial (the user's live case).** …B(full)→P(half), nothing after: P emits `AwaitingFullTank` and
stays pending until the next closing fill. Its litres and miles surface through the open-tank summary (below).

### New `MpgUnreliableReason` member

```csharp
public enum MpgUnreliableReason
{
    NoPreviousFill = 1,
    NonMonotonicMileage = 3,
    AwaitingFullTank = 4,   // new: a partial fill; MPG deferred until the next fill to full
}
```

Append `AwaitingFullTank = 4`. (Value `2` was the retired `PartialFill` from before the fuel-basis spec; do not
reuse it — the semantics are the opposite. The old member meant "discarded because an endpoint wasn't full";
the new one means "deferred, and its litres will count in the next measured span".) The enum serialises by name,
so the numeric value is cosmetic, but a fresh value avoids any confusion with history.

### `FuelEntryMetrics`: two new fields, one clarified

`FuelEntryMetrics` gains:

- `int? SegmentMiles` — the distance the MPG figure covers. Equals `MilesSinceLast` for an ungrouped fill;
  larger for a fill that closed a multi-fill segment. Null when there is no figure.
- `int SpannedFillCount` — how many fills the figure covers (1 = ordinary, ≥2 = grouped a partial or more).
  0 on a fill with no figure.

`MilesSinceLast` keeps its current meaning — odometer delta from the **previous row**, always shown in the
"Miles" column — so the table's per-row miles are unchanged. `SegmentMiles` is the denominator's distance and
drives the "over N fills · M mi" note on a grouped figure. On all-full history `SegmentMiles == MilesSinceLast`
and `SpannedFillCount == 1`, so nothing about the existing rows changes.

`FillLevel` stays on the record (it is displayed), but its doc comment must change: it is **no longer
descriptive-only** — Full/null closes the tank and Half/Quarter defers MPG. The fuel-basis-era "nothing depends
on it" comment on `FuelEntryMetrics.FillLevel` and in `FuelEntry` is now wrong and must be corrected, or it
becomes a lie that outlives the code.

### `FuelEconomySummary`: the open tank

Add three fields describing the in-progress tank — the fills accumulated after the last closing fill with no
closing fill yet:

- `int PendingFillCount` — trailing partial fills since the last closing fill. `0` when the last fill closed
  the tank (the normal, tank-closed state).
- `decimal PendingLitres` — litres pumped into the open tank so far.
- `int? PendingMiles` — miles from the anchor to the latest fill's odometer (`null` if there is no anchor yet,
  e.g. only partial fills exist).

Computed in the same walk: whatever `segmentFills`/`segmentLitres` hold, and `latestMileage − anchorMileage`,
after the loop ends **if the last fill did not close the tank**. When `PendingFillCount == 0` the tank is
closed and the other two are `0` / `null`.

`AverageMpg`, `PerFillAverageMpg`, `BestMpg`, `WorstMpg` are unchanged in definition but improve for free:

- `BestMpg` / `WorstMpg` / `PerFillAverageMpg` now range over **segment** figures (one per closing fill,
  plausible only). Partials contribute no figure, so the off-band pair a partial used to inject is simply gone.
- `AverageMpg` (cumulative) should measure between the first fill and the **last closing** fill, summing litres
  added after the first up to that closing fill — i.e. drop any trailing open-tank fills from the aggregate, the
  same way the per-fill path drops them. On all-full history the last fill *is* closing, so this is identical to
  today (3,175 mi ÷ 494.47 L = 29.19). Without this refinement a trailing partial would bias the cumulative
  figure low (litres pumped, tank not yet back to full); with it, the aggregate and per-fill stories agree.

### Anomaly detector — wording only

`AnomalyDetector.DetectImplausibleMpg` already iterates `FuelEconomyCalculator.Calculate(...).Entries.Where(e =>
e.Mpg is not null && !e.IsPlausible)`. Because it reads the calculator rather than re-deriving MPG, the new
grouping flows through with **no logic change**:

- Partials have `Mpg == null` → never flagged. A partial fill never raises an anomaly (the chosen "calm"
  behaviour) falls out of this for free.
- The band now judges the **grouped** figure, which is the physically meaningful one.

Only the flag's **message and detail** need updating: they currently read `over {MilesSinceLast:N0} miles` and
`"miles":{MilesSinceLast}`. For a grouped figure use `SegmentMiles` (the distance the MPG actually covers) and,
when `SpannedFillCount > 1`, note that the figure spans several fills. Keep the "usually a partial fill or a
mistyped odometer" hint — with grouping, a genuinely implausible grouped figure points harder at a mistyped
odometer, since partials no longer produce one.

### Front-end

**`FuelTable.tsx`** — the `NO_MPG` reason map gains `AwaitingFullTank: 'MPG pending · next full fill'`. The MPG
cell, when `spannedFillCount > 1`, shows a small sub-label ("over 2 fills · 340 mi" from `spannedFillCount` and
`segmentMiles`) so a grouped figure reads as covering a span, not a single tank. The "Fill" column keeps showing
`fillLevel`, but its header note changes: it is no longer "descriptive — never the reason an MPG is absent". For
a partial it now *is* the reason, and the cell can carry a quiet "partial" affordance beside Half/Quarter.

**`AddFillSheet.tsx`** — the Fill level control **defaults to `Full`** for a new fill (`fillLevel: 'Full'` in
the blank seed, replacing `''`), and its hint changes from "descriptive — it does not affect MPG or whether one
is shown" to something like "Full closes the tank and measures MPG; Half/Quarter defers it to your next full
fill". The live MPG preview must respect deferral: when the chosen level is Half/Quarter, the preview shows
"MPG · pending next full fill" instead of a computed number — otherwise the sheet contradicts what the server
will do. (An edit of an existing fill keeps its stored level, as today.)

**Dashboard `FuelPanel.tsx`** — when `pendingFillCount > 0`, show a calm "part-tank in progress" line
(`pendingFillCount` fills · `pendingMiles` mi · `pendingLitres` L — MPG pending) so the open tank is visible
where the owner looks first. When `pendingFillCount === 0` the panel is unchanged.

**Typed client** — regenerated off the updated OpenAPI contract picks up the new `FuelEntryMetrics` and
`FuelEconomySummary` fields; no hand-written type changes.

### Deferred: flagging a long-open tank

If a tank stays open across many fills or a long distance, MPG goes dark for that whole stretch. A future
low-severity nudge ("MPG unmeasured for N miles — log a full fill to reconcile") is worth considering but is out
of scope here (the user chose the calm, no-flag behaviour). Noted so the next reader doesn't assume the calm
state was an oversight.

## Consequences for existing tests

- `FuelEconomyCalculatorTests` — every all-`Full` case is unchanged (grouping reduces to the pairwise figure).
  Add: a partial fill defers (`AwaitingFullTank`, no MPG); a Full-after-partial computes over the summed litres
  and the full span with `SpannedFillCount == 2`; a trailing partial surfaces in the open-tank fields; a `null`
  fill level closes the tank exactly like `Full`.
- `NoFillLevelInCalculationsTests` — **this test now asserts the opposite of the intended behaviour and must be
  replaced.** It enforced "no calculator references `FillLevel`"; the calculator now must reference it (through
  `ClosesTank`). Replace it with a test pinning the *new* contract: only Full/null closes the tank, Half/Quarter
  defers, and Half vs Quarter are treated identically (magnitude is not read).
- `DashboardReproductionTests` — the workbook fixture is all `Full`, so `AverageMpg` (29.19), Best/Worst and the
  per-fill figures are unchanged. These should stay green untouched; if one moves, the grouping is not a proper
  superset and that is the bug.
- `AnomalyDetector` tests — the implausible-splash case survives (a full-to-full 272 mpg is still implausible);
  add a case proving a partial fill raises no `ImplausibleMpg`, and that a genuinely off-band *grouped* figure
  still does, with `SegmentMiles` in the message.
- Front-end `FuelLogPage.test.tsx` — add coverage for the pending row label and the grouped-figure sub-label;
  existing all-full expectations are unchanged.

## External Dependencies (Conditional)

None.
