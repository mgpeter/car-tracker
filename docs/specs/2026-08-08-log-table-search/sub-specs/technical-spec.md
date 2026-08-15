# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-08-08-log-table-search/spec.md

## Technical Requirements

### Search goes inside `useTableView`, and it has to

`components/useTableView.ts` holds selection as `Record<string, string[]>` of **option ids**, and filters with
`sel.includes(o.id) && o.test(row)` (`:98`). The predicate must already exist as an option in the group, so an
unbounded text value has nothing to select. There is a hack - a one-option group whose `test` closes over
screen-level state - and it is the fork the file's own docblock warns against (`:60-65`).

The decisive reason is not elegance. `count`, `total` and `filtered` all derive from that state (`:88`,
`:100-106`), and `TableControls` renders "N of M" from them. A search that filtered rows **anywhere else**
would leave the strip announcing a number that no longer matches the table. So:

```ts
export interface TableViewConfig<T> {
  groups?: FilterGroup<T>[]
  sorts?: SortKey<T>[]
  defaultSortId?: string
  defaultDir?: 'asc' | 'desc'
  /** Free-text search. Omit it and no search input renders - the four existing consumers are unchanged. */
  search?: {
    /** The visible label, and therefore the accessible name. e.g. "Search expenses". */
    label: string
    /** Every text field a query may match, shown on screen or not. Nulls are tolerated, not filtered out. */
    fields: (row: T) => (string | null | undefined)[]
  }
}
```

`TableView<T>` gains `searchText: string` and `setSearchText: (v: string) => void`, and `filtered` becomes
`anySelected || searchText.trim() !== ''`. The filter chain is **groups AND search**, applied inside the same
`useMemo` so ordering and memoisation are unchanged.

Matching reuses the house idiom verbatim from `Combobox.tsx:35-37` - `trim().toLowerCase()` and `.includes`:

```ts
const q = searchText.trim().toLowerCase()
const matchesSearch = (row: T) =>
  q === '' || (config.search?.fields(row) ?? []).some((f) => f?.toLowerCase().includes(q) === true)
```

One case-insensitive substring per field. **Not** term-splitting: `brake pads` matches a field containing
that phrase, not a row with `brake` in one field and `pads` in another. That is a deliberate v1 rule - one
behaviour a user can predict - and term-splitting is the obvious v2.

`config` on the returned view must carry `search` too, since `TableControls` decides whether to render the
input from it.

### One control, on the shared strip

`components/TableControls.tsx` renders the input as the **first** child, before the `groups.map` at `:16`, so
`.tctl-count`'s `margin-left: auto` still floats the count to the right of everything.

```tsx
{view.config.search !== undefined && (
  <label className="tctl-search">
    <span className="tctl-label">{view.config.search.label}</span>
    <input
      type="search"
      value={view.searchText}
      onChange={(e) => view.setSearchText(e.target.value)}
    />
  </label>
)}
```

- **Wrapping `<label>`, not `aria-label`, not `Field`.** This is the strip's existing idiom (`:40-53`) and is
  what makes `getByRole('searchbox', { name: /…/ })` resolve. `Field` belongs to sheets and brings `useId`,
  hints and `aria-invalid` that a filter control has no use for.
- `type="search"` yields `role="searchbox"` and the browser's native clear affordance.
- **No new live region.** The count at `:80` already carries `aria-live="polite"`, so "3 of 13" is announced
  as the query narrows. A second one would double-announce.
- **No new exported component**, so `src/test/coverage.test.ts`'s axe rule needs no new exemption - the input
  lives inside `TableControls`, which is already covered through the pages that render it.

### CSS: `min-width: 0` is not optional

```css
.tctl-search {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  flex: 1 1 160px;
  min-width: 0;
}
```

`.tctl-chips` lacking exactly this is the bug fixed in `8b938af`: a flex item's default `min-width: auto`
floors it at min-content, so a row of controls wider than the viewport pushed the whole document sideways on
a phone and took the fixed bottom nav with it. Add `.tctl-search` to the guard list in
`src/styles/overflow.test.ts`, which exists for this failure and enforces both declarations.

The strip is about to carry one more item than it ever has. Re-check it at 360px with chips, a select, a sort
and a search box all present.

### Per-screen configuration

One `search` block per screen; the field lists are the whole of the change on five of the six.

| Screen | `fields` |
|---|---|
| `ExpensesPage` | `vendor`, `notes`, `category`, `subCategory`, `paymentMethod` |
| `EquipmentPage` | `name`, `notes`, `storedAt`, `sourceVendor`, `category` |
| `ServiceHistoryPage` | `type`, `garage`, `workDone`, `partsReplaced`, `notes` |
| `FuelLogPage` | `station`, `notes` |
| `MileagePage` | `note`/`source` as the DTO carries them |
| `TasksPage` | `title`, `notes`, `assignedGarage` |

**`notes` is searched on every screen that has it, including where no column renders it** - service records
and equipment. That is decision 1 of the spec and the point of the feature on service history, where the MOT
advisories live only in `notes`. The accepted cost is a row that matches for a reason not visible on screen.

### Service history is the only structural change

It calls neither the hook nor the strip today (`ServiceHistoryPage.tsx`), rendering
`[...data.records].reverse()` directly into `<DataTable>` at `:318-325`.

- Import `useTableView` and `TableControls`; render the strip above the table with noun `"records"`.
- Declare **one sort**, `serviceDate`, with `defaultSortId: 'date'` and `defaultDir: 'desc'`, and replace the
  hardcoded `reverse()` with `view.rows`. That reproduces today's order exactly - assert it, because "newest
  first" is stated in the `SectionHead` rule at `:306` and a silent flip would be invisible in review.
- **Add a filter-miss empty panel.** It has only a `records.length === 0` branch (`:309-316`); Expenses
  (`:408-411`) and Equipment (`:216-220`) already distinguish "no records yet" from "nothing matches", and a
  search that appears to empty the log is exactly when that distinction matters.
- Its `Records` stat tile (`:272-297`) counts the **full** set and must not follow the search - the same rule
  Equipment states at `:91-92`. The live "N of M" belongs to `.tctl-count`.

### No debounce, deliberately

No debounce utility exists in the codebase (`debounce`, `useDeferredValue`, `useTransition`: zero hits), and
nothing paginates - every log is one un-paged GET and `DataTable` maps every row with no virtualisation. At
BT53's scale, filtering tens of rows inside a `useMemo` on each keystroke is free, and a debounce would add
input latency to buy nothing. If a log ever outgrows that, **`useDeferredValue`** is the zero-dependency
answer and would be its first use here; paging is its own decision, as the filters spec already says.

### What must not move

- Expenses' `filteredTotal` (`:145`) derives from `view.rows`, so it follows the search - correct, and its
  `.filtered-total` box renders on `view.filtered && view.count > 0`, which a query now satisfies.
- The **stats bands above the tables** stay on the full set: Equipment's inventory totals, Service History's
  `Records` tile, Expenses' YTD rollup. Two of the three already have tests asserting the rollup is untouched
  while a filter is active; the search tests should assert the same.
- Equipment recomputes `visibleCategories` from `view.rows` (`:123-125`) so an emptied group's heading
  disappears. That keeps working for free **because** search is inside the hook.

## External Dependencies (Conditional)

**None.** No new package, no schema change, no endpoint, and therefore no OpenAPI or generated-types diff -
this is client-side filtering over rows already fetched.
