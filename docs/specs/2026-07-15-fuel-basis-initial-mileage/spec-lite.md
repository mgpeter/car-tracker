# Spec Summary (Lite)

Make actual litres the sole basis of every fuel figure. `FuelEntry.FillLevel` becomes descriptive — no calculator reads it — and the reliability gate moves from a three-way tank-level guess to a plausibility band on the computed number: an MPG outside 10–70 is flagged `ImplausibleMpg` and excluded from best/worst. The headline average becomes cumulative (total distance ÷ total litres pumped), which needs no tank-level information and reads 29.19 on the real history against 29.14 for the per-fill mean — the two agreeing to 0.05 mpg is the tank-level noise washing out.

Separately, a vehicle created today has `PurchaseMileage` but no `MileageReading`, so current mileage derives to null until the first log. Creating a vehicle now creates its opening reading, tagged with a new `MileageOrigin.Purchase` so the founding figure stays distinguishable from a later hand correction.
