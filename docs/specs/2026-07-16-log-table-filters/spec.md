# Spec Requirements Document

> Spec: Filter, Sort & Search on the Log Tables
> Created: 2026-07-16
> Status: Complete (2026-07-19) — all four logs wired: fuel + expenses shipped first, tasks (kind chips + priority select, default priority-then-target sort) and equipment (status chips + category select, list stays grouped) followed

## Overview

Give the log tables the filter, sort and search the design shows on every one of them and none of them can do —
as a single capability over the `<DataTable>` seam, not six copies. README §3.2 requires these tables be
"filterable, sortable"; today they render every row in a fixed order. This is the seam's fourth kind of
extension, after columns, column priority and container-query reflow.

## User Stories

### Narrow the log to the thing I came to see

As the owner, I want to filter and sort a log in place, so that "the flagged fills", "last 30 days" or "the
biggest expenses" is a click, not a scroll through everything.

The design writes this on every log and delivers it on none: `fuel-log.dc.html` has chips "All fills / Last 30
days / Flagged only", a station `<select>`, and a footer reading "sorted · date ↓"; `expenses.dc.html` has
category chips, a date-range select down to "Custom range…", and the promise that "filtered totals recompute
with the filter"; `tasks.dc.html` has "All kinds / DIY / Workshop", a priority select, and "sorted · priority,
then target"; `equipment.dc.html` has "All / Owned / On order / To order", a category select, and "grouped ·
status". Four screens, one shape: a strip of predicate chips, one or two dropdowns, a sort, and a live count.
Built once on the shared table, every log gets it and no two drift.

### The total that follows the filter

As the owner, I want a filtered expenses view to total what it shows, so that "how much on tyres this year" is
the figure on screen and not the whole-year rollup sitting above a filtered list.

This is the one that is more than cosmetic. The expenses rollups (`SpendSummary`) are the dashboard's YTD
figures, computed server-side and shared so they can never disagree with the dashboard. A category filter that
left them untouched would show "Tyres" rows under a "This year £X, all categories" header — two answers to
different questions stacked as if they were one. The filtered total must visibly recompute from the visible
rows, clearly distinct from the authoritative YTD rollup, so neither is mistaken for the other.

## Spec Scope

1. **A shared filter/sort layer over `<DataTable>`** — a `useTableView` hook (or equivalent) that a log passes
   its rows, its filter predicates and its sort keys, receiving the filtered, sorted rows and a live count; the
   table itself stays a pure renderer.
2. **Predicate chips and dropdowns** — the design's per-log controls declared as data (chip label + predicate),
   rendered by one shared control strip so fuel, expenses, tasks and equipment configure the same component.
3. **Sort** — a header or footer sort control per table, defaulting to each log's current order (fuel: date ↓;
   tasks: priority then target), with the active sort shown as the design's footer does.
4. **Filtered aggregate on expenses** — a total computed from the *visible* rows, rendered beside — never in
   place of — the server's YTD rollup, so the filtered sum and the authoritative figure are both legible.

## Out of Scope

- **Full-text search across entities.** This is per-table filtering and a search box scoped to *one* log's
  visible columns; a global "find anything" search is a different feature with its own index and its own spec.
- **Saved filter presets.** Remembering "my usual view" is state that outlives a page visit and wants
  persistence and a management UI; the chips reset on reload here, which is the honest default for a first cut.
- **Server-side paging or querying the logs.** The logs are small — BT53 has 13 fills and a handful of
  everything else — so filtering is client-side over rows already fetched. If a log ever outgrows that, paging is
  its own decision; see the technical spec's note on the one tension this creates.
- **Grouping as a new table primitive.** Equipment's "grouped · status" is a sort-then-section presentation over
  the same rows, not a new `<DataTable>` mode; if true grouping is wanted it is a separate seam extension, not
  smuggled in here.
- **Filtering the checks list.** Checks stayed a list, not a table (no columns worth aligning), and its
  never-logged/due/overdue split is already a status axis, not a filter — it is outside this seam by design.

## Expected Deliverable

1. On BT53's fuel log, "Flagged only" and "Last 30 days" narrow the visible fills, the station `<select>`
   filters by station, sorting by date or MPG reorders in place, and the footer names the active sort — all
   client-side over the rows already loaded.
2. On the expenses log, a category filter narrows the rows and a **filtered total** recomputes from what is
   shown, rendered distinctly from the server's "all categories" YTD rollup so the two figures cannot be
   confused.
3. The same control strip drives tasks (kind + priority) and equipment (status + category) with no per-screen
   filter logic — the capability lives on the seam, and adding it to a fifth log is declaring predicates, not
   writing a fourth filter.
