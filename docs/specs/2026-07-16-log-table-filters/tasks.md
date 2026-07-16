# Spec Tasks

## Tasks

- [ ] 1. The shared view layer
  - [ ] 1.1 Write tests for `useTableView`: OR-within-group, AND-across-groups, sort keys, the no-filter default matching today's order, filtered count
  - [ ] 1.2 `useTableView<T>` beside `DataTable.tsx` — takes rows + predicate groups + sort keys, returns visible rows, count, active state and setters
  - [ ] 1.3 `<TableControls>` strip rendering chips (`aria-pressed` buttons) and labelled `<select>`s from declarations
  - [ ] 1.4 Confirm `DataTable` is untouched — it stays a pure renderer
  - [ ] 1.5 Verify all tests pass

- [ ] 2. Wire the four logs
  - [ ] 2.1 Write tests: fuel (all/30-day/flagged + station + sort), tasks (kind + priority), equipment (status + category), each declaring predicates as data
  - [ ] 2.2 Fuel, tasks, equipment pass their rows through `useTableView` and render `<TableControls>`; distinct values (stations, categories) derived from loaded rows
  - [ ] 2.3 The active-sort footer and the "N of M" filtered count on each `SectionHead`
  - [ ] 2.4 Empty-result message distinct from the empty-log state
  - [ ] 2.5 Verify all tests pass

- [ ] 3. The expenses filtered total
  - [ ] 3.1 Write tests: a category filter recomputes the filtered total from visible rows; the server YTD rollup stays put and stays labelled "all categories"; both are distinct in the DOM
  - [ ] 3.2 `ExpensesPage` — category chips + date-range select, a filtered-sum figure rendered beside (never replacing) the `SpendSummary` rollup
  - [ ] 3.3 Confirm mirrored "From fuel" rows stay read-only under any filter
  - [ ] 3.4 Axe sweep + coverage-guard exemptions for the new components
  - [ ] 3.5 Verify all tests pass

- [ ] 4. Prove it end to end on BT53
  - [ ] 4.1 Filter the fuel log to "Flagged only" and by station; sort by MPG; confirm the footer names the active sort
  - [ ] 4.2 Filter expenses by category; confirm the filtered total moves while the YTD rollup holds and stays labelled authoritative
  - [ ] 4.3 Confirm tasks and equipment filter through the same strip with no per-screen filter logic
  - [ ] 4.4 Full suite, both builds, codegen gate (no contract change); update roadmap and CLAUDE.md
