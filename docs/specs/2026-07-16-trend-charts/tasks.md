# Spec Tasks

## Tasks

- [x] 1. The charting primitive
  - [x] 1.1 Write tests: value + time axes render, a derived `aria-label` states range/endpoints, empty state below one point
  - [x] 1.2 Generalise `Spark.tsx` into a reusable `TimeSeries`/`Chart` (single- and multi-series), keeping the derived-caption discipline in the shared primitive
  - [x] 1.3 Greyscale + non-colour-only series distinction (labels/dash/marker, not hue alone)
  - [x] 1.4 Axe sweep + coverage-guard exemption with a reason
  - [x] 1.5 Verify all tests pass

- [x] 2. Fuel charts — MPG and price over time
  - [x] 2.1 Write tests: MPG plots plausible measured intervals only (BT53 → 12, not 13); price plots all fills; derived labels correct
  - [x] 2.2 MPG-over-time chart from `fuel.entries` (`mpg !== null && isPlausible`), on `FuelLogPage.tsx`
  - [x] 2.3 Price-over-time chart from `pricePerLitre` per fill, same screen
  - [x] 2.4 Replace/extend the sparkline where the real chart now belongs; keep the compact dashboard variant
  - [x] 2.5 Verify all tests pass

- [x] 3. Cumulative spend over time
  - [x] 3.1 Write tests: the final cumulative point equals the spend headline for the same period, asserted against the same expense set
  - [x] 3.2 Cumulative-spend chart — expenses ordered by date, summed forward, split by category, from the already-fetched expense log
  - [x] 3.3 Reconcile with `SpendSummary` totals / `YtdByCategory`; category colours off the status axes
  - [x] 3.4 Guard the pre-first-expense empty state; axe sweep + exemption
  - [x] 3.5 Verify all tests pass

- [x] 4. Prove it end to end on BT53
  - [x] 4.1 Confirm MPG plots across the 12 measurable intervals with a correct derived label, and price across all 13 fills
  - [x] 4.2 Confirm the cumulative-spend chart's last point equals the dashboard spend total for the period
  - [x] 4.3 Confirm no library was added, the CSP holds, and each chart is greyscale-legible with a text alternative
  - [x] 4.4 Full front-end suite, build, codegen gate green (no contract change), fixture untouched; update roadmap and CLAUDE.md
