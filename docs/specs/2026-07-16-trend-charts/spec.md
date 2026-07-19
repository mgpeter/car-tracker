# Spec Requirements Document

> Spec: Trend Charts — fuel, MPG & spend over time
> Created: 2026-07-16
> Status: Complete

## Overview

Deliver the real trend charts README §8 defers — fuel price over time, MPG over time across fills, and
cumulative spend over time by category — that the static MPG sparkline explicitly does *not* discharge. Every
chart is a derived view over data the app already computes and exposes (`FuelEntryMetrics` per fill, the expense
log by date and category); nothing new is stored, and the whole point of §1 — figures computed on read, never
stale — extends unchanged to a series of points instead of a single number.

## User Stories

### MPG and price, as a line rather than a headline

As the owner, I want to see MPG and fuel price plotted across every fill, so that a drift — a tank that reads
worse, a price creeping up — is visible as a slope instead of buried in a best/worst pair.

`Spark.tsx` already plots MPG, but its own doc-comment is emphatic that it is not the deferred chart: "§8 defers
*real* trend charts — multi-series, tooltips, zoom — and this does not discharge that … the same picture with
`points.map()` where the typing was, which is all it ever needed." This spec builds the thing §8 actually names:
a proper time-axis chart of MPG per measured interval and price per litre per fill, with axes and readable range,
using the fuel figures the domain already derives (`FuelEntryMetrics.Mpg`, `.PricePerLitre`, `.EntryDate`,
`.IsPlausible`). Implausible intervals stay off the line exactly as they stay off the headline — a 272 mpg splash
plotted would be a spike the average denies.

### Spend, accumulating over time

As the owner, I want cumulative spend plotted over time, split by category, so that I can see where the money
went and when, and confirm the running total lands on the same figure the dashboard already shows.

README §8 line one asks for "spend-over-time charts", and the `SpendSummary` already carries YTD-by-category
(`YtdByCategory`) and the totals the dashboard prints. A cumulative-spend chart is those same expenses ordered by
date and summed forward — a picture of the number the spend panel states, and its final point must equal that
number, or one of the two surfaces is lying. Reading both from the same expense log is what makes them agree by
construction.

## Spec Scope

1. **A charting primitive** — a small hand-rolled SVG chart component (the `Spark.tsx` approach, generalised),
   with a value axis, a time axis, and a derived accessible name, reusable across the three series.
2. **MPG-over-time chart** — plausible measured intervals from `fuel.entries`, plotted against date; the deferred
   §8 chart the sparkline stood in for.
3. **Fuel-price-over-time chart** — `pricePerLitre` per fill against date, from the same fuel entries.
4. **Cumulative-spend-over-time chart** — expenses ordered by date and summed forward, split by category, whose
   final total reconciles with the existing spend headline.
5. **Placement** — the charts live on the fuel screen (`FuelLogPage.tsx`) and the expenses/dashboard spend
   surfaces, replacing or extending the sparkline where a real chart now belongs.

## Out of Scope

- **Interactive zoom, pan and per-point tooltips as a hard requirement.** They are the "nice-to-have" tail of
  §8's "multi-series, tooltips, zoom"; v1 must be readable and accessible *first*, and a hover-only tooltip is
  invisible to a screen reader and on a phone. A derived caption and axis labels carry the data with no
  interaction, the way `Spark`'s `aria-label` already is the chart for a non-visual reader. Interactivity can be
  layered on later without redoing the series.
- **Real-time or streaming updates.** The charts recompute on data change through TanStack Query like every other
  view; there is no live-tick requirement and inventing one is scope the spec does not have.
- **New stored aggregates or a reporting endpoint.** The fuel entries and the expense log are already exposed;
  the charts are derived views over them, never a stored table, which is the §1 rule the sparkline already
  honours. If a cumulative series needs to *agree* with a headline, it reads the same source, not a new one.
- **A general dashboarding / custom-chart builder.** This is three named charts §8 asks for, not a framework for
  arbitrary ones — that would be a product of its own and is not what "trend charts" means here.

## Expected Deliverable

1. On BT53's fuel screen, MPG plots across its twelve measurable intervals (not thirteen — the first fill has no
   predecessor, and DEC-012's phantom interval is not real), with a date axis and a value axis, and its
   accessible name describes the actual range and latest — derived, never a frozen string.
2. Fuel price per litre plots across the thirteen fills, and a cumulative-spend chart plots the running total by
   category whose last point equals the spend panel's total for the same period.
3. Every chart is legible in greyscale and carries a derived text alternative; none is colour-only, and the axe
   sweep passes with the plate from `usePlate()`.
