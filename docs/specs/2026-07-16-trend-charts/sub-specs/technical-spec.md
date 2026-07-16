# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-trend-charts/spec.md

## Technical Requirements

### The build-vs-buy decision: extend the hand-rolled SVG, do not add a chart library

- **Recommendation: extend the `Spark.tsx` approach into a small internal charting primitive; add no chart
  library.** The reasoning is the project's own constraints, not preference:
  - **CSP.** The app runs under a strict Content-Security-Policy and self-hosts everything — fonts are inlined
    base64, there are no CDN loads by design. A chart library that injects styles, pulls a runtime, or assumes a
    web-font would fight that, and the ones that do not are the small ones we would be reimplementing anyway.
  - **Dependency surface.** `package.json` today is React, React-Router, TanStack Query and dev tooling — no
    runtime charting dep. The heavyweight options (Recharts, visx, nivo) bring a wide tree for three static
    line/area charts. That is a large surface to audit and keep CSP-clean for a small, well-understood need.
  - **The two hard parts are already solved.** `Spark` proves out exactly what a library tends to get wrong for
    this app: its `aria-label` is *derived* ("Fuel economy across N measured intervals, ranging … latest …"),
    not a frozen string and not a colour-keyed legend, and its markers (best dot, last dot) are position-and-shape,
    legible in greyscale. A generic chart library defaults to a colour-only legend and a decorative title, which
    is precisely the regression the whole app avoids.
- **If a library were chosen anyway** it must be CSP-safe (no runtime CDN fetch, no injected external stylesheet
  or font) and must let the accessible-name and greyscale story hold — a plotting lib whose only affordance is a
  hover tooltip and a coloured legend does not clear the bar this app already sets with a hand-rolled SVG. The
  smallest defensible candidate is a canvas/SVG micro-lib (e.g. uPlot-class, ~tens of KB, no dependencies), but
  even then it reintroduces the accessible-name work `Spark` already did. Hence: extend `Spark`.
- Generalise `Spark` into a reusable `Chart`/`TimeSeries` primitive: a value axis, a time axis, one or more
  series, and a **required derived caption/`aria-label`** built from the data the way `Spark`'s label is. Keep
  `Spark` as the compact dashboard variant or fold it in — but the derived-label discipline is non-negotiable and
  moves into the shared primitive so no chart can ship a frozen or colour-only description.

### Data — all derived, none stored

- **MPG-over-time and price-over-time** come straight from the fuel entries the summary already exposes.
  `FuelEconomyCalculator` produces `FuelEntryMetrics` per fill with `Mpg`, `PricePerLitre`, `TotalCost`,
  `Litres`, `EntryDate`, `IsPlausible`, `MilesSinceLast`. The MPG series is the *plausible measured* intervals —
  `e.mpg !== null && e.isPlausible` — the identical filter `FuelPanel.tsx` already applies before handing points
  to `Spark`. Implausible and unmeasurable fills are off the line, on the same reasoning: a 272 mpg splash is
  arithmetically correct and physically meaningless, and the headline excludes it. The price series is every
  fill (price is a receipt fact, always present), so it may hold points the MPG series does not.
- **Cumulative spend over time** is the expense log ordered by date and summed forward, split by category. The
  expenses are already fetched by the expenses screen; the running total is an axis transform over stored facts
  (an amount on a date), the same way `Spark` scales points into pixels — **not a new stored aggregate**. The one
  constraint that matters: the chart's final cumulative point, over a given period, must equal the figure
  `SpendCalculator` already reports (`SpendSummary` totals / `YtdByCategory`). Read the same expense set the spend
  panel reads so the two agree by construction; if they can diverge, that is the bug the whole project exists to
  prevent, reappearing on a chart.
- **No new domain arithmetic, so the five-defect fixture is untouched.** The per-fill MPG, price and cumulative
  spend are all values the domain already computes or trivially accumulates from stored facts; this spec plots
  them, it does not recompute them.

### Charts and accessibility

- Every chart carries a **derived** text alternative describing range, endpoints and series — following
  `Spark`'s `aria-label` exactly. A chart's accessible name is the entire chart for a non-visual reader; a frozen
  string "sounds authoritative while being false after the next fill", as `Spark`'s comment puts it.
- Multi-series (cumulative spend by category) must not be **colour-only**. Distinguish series by more than hue —
  direct labels, distinct dash/marker, or small multiples — so the chart survives greyscale, the invariant the
  whole app keeps. Category colours, where used, come from the token palette and stay off the status axes (green
  = OK, amber = due soon, rust = overdue, blue = integrity are semantic and must not be repurposed as series
  colours).
- Empty and thin states are real, not decorative: no MPG line with fewer than one measurable interval (the
  `Spark` empty copy — "MPG needs two fills" — carries over), no spend chart before the first expense. BT53 has
  data frozen in; other vehicles and future gaps do not, which is why these states must be handled.
- Charts recompute through TanStack Query on data change; they hold no state of their own beyond view scaling.
- Every new component is axe-swept or exempted with a reason in
  `src/CarTracker.WebApp/src/test/coverage.test.ts`; the plate comes from `usePlate()` (no `plate={reg}`
  regression).

### Verification

- Component tests: the MPG chart plots BT53's twelve measurable intervals, not thirteen (the first fill has no
  predecessor; DEC-012's phantom interval is excluded), and its derived label states the real range and latest.
- Component tests: the cumulative-spend chart's final point equals the spend panel's total for the same period,
  asserted against the same expense set — the reconciliation is the test that matters.
- Greyscale/axe: each chart passes the sweep and carries a non-colour-only distinction between series.
- No codegen or fixture change: `npm run gen:api` then `git diff --exit-code` stays green (no contract change),
  and the C# five-defect fixture is not touched (no domain arithmetic added).
- Live on BT53 against `dotnet run --project src/CarTracker.AppHost`: the MPG and price lines render across the
  real 13 fills, and the cumulative-spend chart lands on the dashboard's spend total.

## External Dependencies (Conditional)

None. The recommendation is to extend the existing hand-rolled SVG (`Spark.tsx`) rather than add a charting
library, precisely to keep the dependency surface small and the strict CSP intact. If a future revision reverses
this, the chosen library must be CSP-safe (no runtime CDN fetch, no injected external stylesheet/font) and must
preserve a derived accessible name and greyscale legibility — the two properties `Spark` already guarantees and a
generic library does not.
