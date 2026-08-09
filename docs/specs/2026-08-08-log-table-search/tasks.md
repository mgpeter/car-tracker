# Spec Tasks

## Tasks

- [x] 1. Search in the hook
  - [x] 1.1 Write tests in `components/useTableView.test.tsx` (`renderHook` + `act`, the existing style):
        a query narrows `rows` and moves `count` while `total` holds; an empty or whitespace query filters
        nothing and leaves `filtered` false; groups AND search compose; a null field is skipped, not thrown on
        — 7 tests, all failing on `setSearchText is not a function` before the implementation landed
  - [x] 1.2 Add `search?: { label, fields }` to `TableViewConfig<T>`, `searchText`/`setSearchText` to
        `TableView<T>`, and `search` to the returned `config` — extracted as a named `TableSearch<T>`, since
        `TableControls` needs the type to read `config.search`
  - [x] 1.3 Fold the predicate into the existing `useMemo` — groups AND search — and widen `filtered` to
        `anySelected || searchText.trim() !== ''`. Match with `trim().toLowerCase()` + `.includes`, one
        substring per field, reusing the idiom from `Combobox.tsx:35-37`
  - [x] 1.4 Verify the four existing consumers are untouched: omitting `search` must behave exactly as today
        — `npx tsc -b --force` clean, full suite green, plus a hook test asserting `setSearchText` is inert
        when no `search` is declared. Note the query is forced to `''` when `config.search` is undefined, so a
        consumer that never opted in cannot be filtered by stale state
  - [x] 1.5 Verify all tests pass — 464 front-end (was 457)

- [x] 2. The control on the strip
  - [x] 2.1 Write tests: the input renders only when a screen declares `search`; it resolves via
        `getByRole('searchbox', { name: … })`; typing updates the count; **no second `aria-live` region**
        — new `components/TableControls.test.tsx`, the strip's first direct test (it was covered only through
        the screens that render it), with a `Host` that wires the real hook to the real strip
  - [x] 2.2 Render it as the first child of `TableControls`, a wrapping `<label className="tctl-search">`
        around `<span className="tctl-label">` + `<input type="search">` — the strip's own labelling idiom,
        not `aria-label` and not `Field`
  - [x] 2.3 Add `.tctl-search` to `components.css` with `flex: 1 1 160px` and **`min-width: 0`**, and add the
        selector to the guard list in `src/styles/overflow.test.ts` — this is the `8b938af` failure mode and
        the strip is about to carry one more item than it ever has. The guard's shrink assertion was
        generalised from one hardcoded selector to a list, so the next control added is covered by adding a
        string. `min-width: 0` also goes on the inner `input`, which carries its own UA-default intrinsic width
  - [ ] 2.4 Check the strip at 360px with chips, a select, a sort and a search box all present — deferred to
        task 6, where the browser pass happens; nothing renders a search box until task 3 wires a screen
  - [x] 2.5 Verify all tests pass — 472 front-end (was 464), typecheck clean

- [x] 3. Expenses and Equipment
  - [x] 3.1 Write tests: searching a vendor narrows the expenses table and the count reads "N of M"; the
        `.filtered-total` box follows the visible rows while the **YTD rollup is unchanged**; clearing the box
        restores every row — plus a case-insensitivity test
  - [x] 3.2 Write tests: searching equipment narrows across categories, only headings with surviving items
        render (assert `.eqhead` contents, as the existing filter test does), and the inventory stats stay on
        the full set — plus a `storedAt` test, proving an unshown field is searched ("what's in the boot?")
  - [x] 3.3 Add the `search` config to both screens per the technical spec's field table
  - [x] 3.4 Check the filter-miss copy on both still reads correctly when the cause is a search rather than a
        chip — Expenses says "No expenses match this filter", Equipment "No items match this filter". Both
        read correctly for a query too, since a search *is* a filter here; left as written rather than
        reworded to name search, which would be worse copy for the chip case
  - [x] 3.5 Verify all tests pass — 478 front-end (was 472), typecheck clean

  > **Process note:** on this group the config went in before the tests, so 3.1/3.2 never saw red. The
  > mechanism they exercise was already proved red→green in task 2; these assert the wiring. Worth recording
  > rather than implying a discipline that was not followed.

- [x] 4. Service history — the screen with no controls at all
  - [x] 4.1 Write tests: **with no query the records read newest-first exactly as today** (the order is stated
        in the `SectionHead` rule, so a silent flip would pass review); a query matching only `notes` — an MOT
        advisory, which no column renders — returns that record; a miss shows the new empty panel and not the
        "no records yet" one. Needed a second fixture record (`CAMBELT`), since one row cannot show an order
  - [x] 4.2 Import `useTableView` + `TableControls`; declare the `serviceDate` sort with
        `defaultSortId: 'date'`, `defaultDir: 'desc'`; replace `[...data.records].reverse()` with `view.rows`.
        The compare carries an **id tie-break** — `reverse()` put the later-inserted of two same-day records
        first, and a bare date compare would not reproduce that
  - [x] 4.3 Add the filter-miss empty panel it lacks, distinct from the `records.length === 0` branch
  - [x] 4.4 Confirm the `Records` stat tile still counts the full set, not the visible rows
  - [x] 4.5 Verify all tests pass — 483 front-end (was 478), typecheck clean

  > The order test failed first time on a **test** bug worth remembering: `.dt-row` is on the header too
  > (`DataTable.tsx:86` renders `dt-head dt-row`), so a raw `querySelectorAll('.dt-row')` returns the column
  > labels as row one. The selector needs `:not(.dt-head)`. The existing fuel test sidesteps this by finding
  > rows by text rather than position.

- [x] 5. The remaining three, and the paperwork
  - [x] 5.1 Write one search test each for fuel, mileage and tasks, in the house style. Each also asserts the
        screen's **aggregate does not move** — fuel's fleet stats, tasks' bundle figure — since that is the
        rule most likely to be broken by wiring search into a screen that has one
  - [x] 5.2 Add the `search` config to `FuelLogPage`, `MileagePage` and `TasksPage`. Mileage searches the
        origin **label** (`ORIGIN[r.origin]`), not the wire enum: the column renders "from a fill", so that is
        what a reader types, and matching `Fuel` would make the box disagree with the words beside it
  - [x] 5.3 Update the stale `TableControls` exemption prose in `src/test/coverage.test.ts:40` — it names only
        FuelLogPage and ExpensesPage, and six screens now render the strip
  - [x] 5.4 Add a roadmap entry in `docs/product/roadmap.md`; update `README.md` §3.2, whose filter/sort
        sentence was corrected on 2026-08-07 and changes again once service history gains controls
  - [x] 5.5 Axe sweep on the changed screens; `npm run build` then the full suite, so `overflow.test.ts` and
        the built-document tests both run
  - [x] 5.6 Verify all tests pass — 486 front-end, typecheck clean, build clean

  > Three screens had a pre-existing comment sitting directly above the `useTableView` call. Inserting the
  > search block between them orphaned each one from the statement it described; all three were moved back.

- [~] 6. Prove it on BT53
  - [ ] 6.1 Search the expenses log for a real vendor; confirm the rows, the count, the filtered total and an
        unmoved YTD rollup
  - [ ] 6.2 Search the service history for a phrase that exists only in `notes`; confirm the record is found —
        this is the decision the spec turns on and the only manual check that proves it
  - [ ] 6.3 On a phone, confirm the strip wraps and the page does not scroll sideways
  - [x] 6.4 Full suite, both builds, codegen gate (expected: no contract diff at all); update CLAUDE.md —
        486 front-end, `dotnet build` clean, `npm run gen:api` produced **no diff at all**, as predicted

  > ⚠️ **6.1–6.3 cannot be done from here.** They are checks against BT53's real history, and that history
  > lives only on the NAS — vehicles are never seeded (DEC-007), so a local run has an empty garage and
  > nothing to search. They are the post-deploy pass, alongside the two fixes already waiting on the same
  > redeploy. 6.3 is the one worth doing deliberately: the strip now carries one more control than it ever
  > has, which is exactly the condition that produced `8b938af`.
