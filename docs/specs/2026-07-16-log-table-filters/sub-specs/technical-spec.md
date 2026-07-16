# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-log-table-filters/spec.md

## Technical Requirements

### The seam, not the screens

- `src/CarTracker.WebApp/src/components/DataTable.tsx` stays a **pure renderer**. It already takes `columns` and
  `rows` and owns nothing but layout — its docstring is explicit that it was "extracted at the third consumer,
  not the first", and its three prior extensions (per-table column widths, `ColumnPriority`, container-query
  reflow) all kept it dumb. Filtering does not change that: the hook filters, the table renders what it is given.
  A `rows.filter()` inside `DataTable` would be the fork the seam exists to avoid.
- A `useTableView<T>` hook (new, beside `DataTable.tsx`) is the capability. It takes the full `rows`, a set of
  **filter predicates** (each `{ id, label, test: (row) => boolean }`, grouped so chips within a group are OR
  and groups are AND), and a set of **sort keys** (`{ id, label, compare: (a, b) => number }`). It returns
  `{ rows: visible, count, activeFilters, activeSort }` and the setters the control strip binds to. The current
  fixed order of each log becomes that log's default sort key, so behaviour with no filter is identical to today.

### Declared as data, rendered once

- A `<TableControls>` strip renders the chips and dropdowns from the predicate/sort declarations — the same
  component for every log. The design's per-log controls become data:
  - **Fuel** (`fuel/FuelTable.tsx` and `FuelLogPage.tsx`): chips "All fills / Last 30 days / Flagged only", a
    station `<select>` built from the distinct stations in the loaded rows, sort by date or MPG. The design's
    footer "sorted · date ↓" is `activeSort` rendered.
  - **Expenses** (`ExpensesPage.tsx`): category chips (from the loaded rows' categories, not a second hardcoded
    list — the same mistake `useCategories` was written to kill), a date-range select including "Custom range…",
    sort by date or amount.
  - **Tasks** (`TasksPage.tsx`): "All kinds / DIY / Workshop", a priority `<select>`, sort "priority, then
    target" — which is already `GetTasksAsync`'s server order, so it is the default key.
  - **Equipment** (`EquipmentPage.tsx`): "All / Owned / On order / To order", a category select; "grouped ·
    status" is a status sort plus sectioned rendering, **not** a new table mode (see Out of Scope).
- Distinct stations, categories and vendors are derived from the loaded rows, so a filter can never offer a value
  no row has — and never needs a second source of truth to stay in step with the data it filters.

### The expenses aggregate — the one real tension

- The expenses rollups come from the server: `ExpenseLog { rollups: SpendSummary, entries }`, and `SpendSummary`
  (`src/CarTracker.Shared/Metrics/SpendSummary.cs`) is `SpendCalculator`'s YTD answer, the **same figures the
  dashboard shows** because both read `IDerivedMetricsService`. The page footer already says "Every total here is
  SUM() at render" — but that SUM is over *all* rows, and the header rollup is the server's.
- **Recommendation: client-side filtered total, rendered as a separate figure.** When a filter is active,
  `ExpensesPage` computes a total over the *visible* `ExpenseItem[]` and shows it labelled as the filtered sum,
  beside the untouched server rollup ("This year · all categories"). The two must be visually distinct: one is
  "what this filtered view sums to", the other is "the authoritative YTD figure the dashboard agrees with".
- **The tension, stated honestly:** a client sum of the visible rows is not the server's YTD arithmetic. YTD is
  scoped to the year and to spend groups (`SpendGroup`); a naive `sum(amount)` over filtered rows answers a
  narrower question and could be mistaken for the rollup if presented as one. Two ways to keep it honest — (a)
  compute the filtered figure client-side and *label it as the filtered view's sum*, never as YTD (simpler,
  recommended, no API change); or (b) add a filtered-query endpoint that returns a scoped `SpendSummary`
  (authoritative, but a new server surface for a small log). This spec takes (a): the logs are small, the client
  already holds every row, and the labelling — not a second query — is what prevents confusion.

### Interactions to preserve

- **Mirrored rows still cannot be edited.** Filtering does not touch the "From fuel" `<IntegrityPill>` rows'
  read-only status; a filter is a view, not a write.
- **Empty filtered state is not the empty log.** "No fills match Flagged only" is a different message from "No
  fills yet" — an empty *result* must read as a filter that matched nothing, not as a load failure or a
  first-run empty state (the `Absent` reasoning, one level up).
- **The count is the filtered count.** The `SectionHead` "13 rows" becomes "3 of 13", so the filter's effect is
  legible and the total is not lost.
- No change to what the server returns for any log; this is entirely client-side over data already fetched. No
  new query params, no codegen change — the OpenAPI contract is untouched.

### Accessibility

- The control strip is real controls: chips are buttons with `aria-pressed`, dropdowns are labelled `<select>`s,
  the active sort is announced. Every new component is swept by axe or exempted with a reason in
  `coverage.test.ts`, which fails the build otherwise. `usePlate()` already handles the plate on each page.

## Verification

- Front-end tests (Vitest + RTL + the axe matcher): a predicate narrows the rows; combining a chip group and a
  dropdown is OR-within-group, AND-across; sorting reorders; the default (no filter) matches today's order
  exactly; an empty result shows the "nothing matches" message, not the empty-log one.
- Expenses: a category filter recomputes the filtered total from the visible rows, and the server YTD rollup
  stays put and stays labelled as such — the two figures are distinct in the DOM and the test asserts both.
- The codegen staleness gate stays green trivially — no contract change — but run `npm run gen:api` +
  `git diff --exit-code` to prove it.
- Live on BT53: filter the fuel log to "Flagged only" (the DEC-012 first-fill interval and the implausible-MPG
  fills surface), sort by MPG, and filter expenses by category to watch the filtered total move while the YTD
  rollup holds.

## External Dependencies (Conditional)

None. `DataTable`, the four log pages and their loaded row data all exist; this is a hook and a control strip
over rows already in the browser.
