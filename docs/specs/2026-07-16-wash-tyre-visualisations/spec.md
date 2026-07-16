# Spec Requirements Document

> Spec: Wash Cadence Window & Tyre Corner Diagram
> Created: 2026-07-16
> Status: Planning

## Overview

Give the wash and tyre screens the two bespoke visualisations their designs draw and the current screens omit:
a cadence bar that shows where today sits against the 21–28 day wash window, and a car-body corner diagram
that lays out five pressures and four treads as the shape they physically are. Both are presentation over data
that already exists — `WashPage.tsx` already computes the gap since the last wash and the average gap, and
`TyresPage.tsx` already holds the per-corner figures — so there is no schema, no endpoint and no new arithmetic
here, only a picture of numbers that are already correct.

## User Stories

### The wash window, seen at a glance

As the owner, I want the wash screen to show today's position against the 21–28 day target as a bar, so that I
can see "due soon" or "overdue" without doing the subtraction in my head.

`WashPage.tsx` already derives `sinceLast` and `averageGap` against `TARGET_MIN = 21` / `TARGET_MAX = 28`, and
renders them as plain `Kv` stats. The design's `wash.dc.html` turns the same figures into a `cad-track` bar: a
filled portion for days elapsed, a highlighted 21–28 target window, a "today · day 14" marker, and a scale
reading "day 21 — window opens · day 28 — overdue". On a 2003 Freelander with salted roads the cadence is a
rust question, and a bar answers "am I inside the window" in one look where four stat tiles ask the reader to
compute it. The status flips to Overdue past day 28 — the same threshold the stat note already uses
(`sinceLast > TARGET_MAX`), now drawn rather than described.

### Four corners, laid out as four corners

As the owner, I want tyre pressures and tread shown on a car-body diagram, so that "which corner" is spatial
instead of a row of look-alike columns, and the spare's missing tread reads as a fact rather than a gap.

`TyresPage.tsx` renders a `<DataTable>` today, which is honest — four corners of numbers *are* a table — but the
design's `tyres.dc.html` draws them as four corner cards plus a full-width spare card, because pressure at the
front-left is easier to place on a car outline than in the second column of a wide row. The model is asymmetric
on purpose: **5 pressures, 4 treads — the spare takes a pressure but has no tread target**, and the spare card
says so ("never logged", "no tread target") instead of leaving a blank the reader has to interpret. A corner
warns when its tread nears the 1.6 mm MOT limit, on the due axis, so a legal-borderline tyre is visible before
the test finds it.

## Spec Scope

1. **Wash cadence bar** — a `cad-track`-style bar on `WashPage.tsx`: elapsed fill, a highlighted 21–28 day
   target window, a "today · day N" marker and a scale, driven entirely by the already-computed `sinceLast`.
2. **Wash status pill** — OK below day 28, Overdue past it, on the due axis (green/amber/rust), reusing the
   `sinceLast > TARGET_MAX` rule the stat note already applies, so the pill and the note can never disagree.
3. **Tyre corner diagram** — a CSS/HTML car-body layout of four corner cards (pressure + tread + any warn note)
   rendered *alongside* the existing `<DataTable>`, not replacing it.
4. **Spare card** — a full-width card for the fifth pressure with no tread target, rendering "never logged"
   from `psiSpare === null` exactly as the table's `Absent>never</Absent>` does today.
5. **Tread warn state** — a corner whose tread is at or below a named "approaching the MOT limit" threshold gets
   a due-axis warn tone and note, from the per-corner tread already loaded.

## Out of Scope

- **Tread-over-time and cadence history charts.** A single reading has no history to shape, and multi-reading
  trend lines are the trend-charts spec's job (`docs/specs/2026-07-16-trend-charts/`); the tyre diagram's own
  comment already says the shape earns its place "when tread depth has a history worth seeing", and that is a
  different spec, not this one.
- **Rendering either visualisation as an SVG.** `Spark.tsx` is the *only* hand-rolled SVG in the app and it
  earns it by plotting a series; a wash bar and a four-corner layout are boxes and fills, which CSS does
  natively and legibly. The tyre screen's own doc-comment already notes the design's "diagram" is "four
  `border-radius` divs, not an SVG", and the `<DataTable>` philosophy is not to over-abstract a layout that
  plain markup expresses.
- **Rolling wash-overdue into the dashboard attention/checks count.** The design says the wash pill "joins the
  dashboard checks count", but a wash is not a `CheckDefinition` and its cadence is not a check interval;
  folding it into the checks total would conflate two axes the way the codebase already refused to conflate
  "check never logged" with the integrity axis. Whether wash cadence becomes a due-axis signal on the dashboard
  is a summary/domain decision of its own, not this presentation spec.
- **A configurable per-vehicle wash target or tread threshold.** `TARGET_MIN`/`TARGET_MAX` and the MOT limit are
  constants today; making them per-vehicle settings is more than the visualisation needs and would be its own
  Settings spec.

## Expected Deliverable

1. On BT53's wash screen, the cadence bar shows the elapsed days filled against a highlighted 21–28 window with
   a "today · day N" marker, and the status pill reads OK inside the window and Overdue past day 28 — with N and
   the pill both derived from `sinceLast`, never stored.
2. On BT53's tyre screen, the latest reading renders as four corner cards plus a full-width spare card that
   reads "never logged · no tread target", above the unchanged readings table; a corner whose tread approaches
   1.6 mm shows a due-axis warn note.
3. Both visualisations survive a reload, render nothing misleading in the empty state (no bar before the first
   wash, no diagram before the first reading), and pass the axe sweep with the plate coming from `usePlate()`.
