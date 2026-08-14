# Spec Requirements Document

> Spec: Log Table Search — free text over the rows already on screen
> Created: 2026-08-08
> Status: Shipped 2026-08-09 in `05885e5` (six screens wired, 486 front-end tests; the `VERSION` bump was
> missed and corrected a commit later in `4b178c2`). One task is open — 2.4, a 360px browser check deferred
> into task 6 — and the rest of task 6 was verified by the owner on the deployed build.
>
> The problem statement below is written in the present tense as it stood on 2026-08-08 ("there is no search
> anywhere in the app today") and is left that way deliberately: it is the argument for the work, not a
> description of the app.

## Overview

Add a free-text search box to the shared filter/sort strip, so a log can be narrowed by typing rather than
only by pre-declared chips and selects. It extends `useTableView` — the seam four screens already share —
rather than adding a filter to any one screen, so search arrives everywhere the strip is rendered and the
live "N of M" count keeps telling the truth.

**This finishes a sentence an earlier spec started.** `docs/specs/2026-07-16-log-table-filters/spec.md` is
titled *"Filter, Sort & Search on the Log Tables"* and shipped only the first two, putting search out of
scope as "a search box scoped to *one* log's visible columns; a global find-anything search is a different
feature with its own index and its own spec". That framing still holds: this is per-table search, and a
global one remains out.

There is no search anywhere in the app today — `type="search"`, `role="searchbox"`, `filterText` and
`searchTerm` all return zero hits across `src/`. So this is genuinely new UI, with no in-app pattern to copy.

## User Stories

### Find the receipt without remembering the category

As the owner, I want to type a vendor's name into the expenses log and see only their rows, so that I can
answer "what have I spent at Halfords?" without first working out which categories they'd be filed under.

The category chips already answer "show me all servicing". They cannot answer "show me everything from one
supplier", because a vendor spans categories — a garage visit is `Service`, its parts are `Repair`, and the
same name appears on both. The owner knows the name on the receipt; that is the thing they actually
remember. Typing it narrows the table, the filtered total below it follows, and the YTD rollup above stays
put — the two figures already coexist and neither may be mistaken for the other.

### Find which service replaced the part

As the owner, I want to search the service history for a part or a phrase, so that I can find out when
something was last done without reading four years of records.

This is the story the workbook cannot serve at all. "When did the cambelt last get changed?" and "which
visit produced that advisory?" are questions about free text — `workDone`, `partsReplaced`, and the `notes`
field where MOT advisories live. Service history has **no filter controls of any kind** today: it renders
every record newest-first and leaves the reading to you. A search box is the whole of its filtering.

### Find the kit without knowing its category

As the owner, I want to type a tool's name in the equipment inventory and find it whatever category it sits
in, so that "do I already own a torque wrench?" is one question rather than an expansion of every group.

The inventory is grouped by category, and the answer to "do I own this" is often "yes, filed somewhere you
didn't look". `storedAt` matters just as much — "what's in the boot?" is a real question the data can
answer and the UI currently cannot ask.

## Spec Scope

1. **Search inside `useTableView`** — an optional `search` config declaring the row's searchable text, with
   `searchText`/`setSearchText` on the returned view and the query folded into the existing `filtered` flag,
   so `count`, `total` and the "nothing matches" branches stay correct without any screen recomputing them.
2. **One search input on the shared strip** — rendered by `TableControls` when a screen declares `search`,
   labelled by a wrapping `<label>` like the existing selects, with no second live region because the count
   already announces itself.
3. **All text a row carries, not only what a column shows** — including `notes` on service records and
   equipment, which no column renders and which holds the MOT advisories worth finding.
4. **Six screens wired** — expenses, equipment and service history (the three asked for), plus fuel, mileage
   and tasks, which already call the hook and so cost one config block each.
5. **Service history gains the strip it never had** — `useTableView` + `TableControls`, a `serviceDate` sort
   that reproduces today's newest-first order exactly, and the filter-miss empty state it currently lacks.

## Out of Scope

- **Global, cross-entity search.** A box that searches expenses, services, documents and tasks at once needs
  an index, a result-ranking model and a place to live that is not a table header. Explicitly deferred by
  the filters spec and still deferred here.
- **Term splitting and fuzzy matching.** The query matches as one case-insensitive substring per field.
  Splitting `brake pads` into two AND-ed terms is the obvious v2 and is left out so v1 has one rule that is
  easy to predict.
- **Highlighting the matched substring.** Useful, and a different job: it means every screen's cell renderer
  learns about the query, where this spec keeps search entirely inside the hook and the strip.
- **Server-side search or paging.** Every log is one un-paged GET and every row is already in memory.
- **Tyres and Wash.** Like service history, they have `<DataTable>` with no controls at all, so including
  them means the same from-scratch wiring for two screens that were not asked for. They stay as they are,
  and the seam is ready when they are.

## Expected Deliverable

1. On the expenses log, typing a vendor's name narrows the table to their rows, the strip reads "N of M",
   the filtered total under the table follows the visible rows, and the YTD rollup above is unchanged.
   Clearing the box restores every row and the count returns to the plain total.
2. On the service history, a query matching only a record's `notes` — an MOT advisory, which no column
   renders — returns that record; and with no query the records read newest-first exactly as they do today.
3. On the equipment inventory, a search narrows the list across categories and only the headings with
   surviving items render, while the inventory stats above stay on the full set.
