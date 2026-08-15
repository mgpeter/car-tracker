# Spec Summary (Lite)

Add free-text search to the shared filter/sort strip so a log can be narrowed by typing, not only by
pre-declared chips and selects. Search lives **inside `useTableView`** rather than beside it - the hook's
selection state holds option ids and its `count`/`total`/`filtered` derive from them, so a search filtering
rows anywhere else would silently desync the "N of M" the strip renders.

A query matches every text field the row carries, **including ones no column shows** - service `notes` holds
the MOT advisories, and equipment `notes` is rendered nowhere. Six screens are wired: expenses, equipment and
service history, plus fuel, mileage and tasks, which already call the hook. Service history gains the strip
it never had, with a `serviceDate` sort reproducing its current newest-first order and the filter-miss empty
state it lacks.

Entirely client-side over rows already fetched: no schema, no endpoint, no contract change, and no debounce
(nothing paginates and the logs are tens of rows). Completes the deferred third of
`2026-07-16-log-table-filters`, which was titled "Filter, Sort & Search" and shipped two of the three.
