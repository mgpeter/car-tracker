# Spec Summary (Lite)

Add the filter, sort and per-table search the design shows on every log and none can do — as one shared
capability over the `<DataTable>` seam, its fourth extension after columns, priority and container-query reflow.
A `useTableView` hook takes a log's rows, predicate chips and sort keys and returns the filtered, sorted rows
plus a live count; the table stays a pure renderer. Fuel, expenses, tasks and equipment configure the same
control strip by declaring predicates as data, not by each writing its own filter (README §3.2 requires these
tables be "filterable, sortable").

Filtering is client-side over rows already fetched — the logs are small. The one tension worth naming: the
expenses rollups (`SpendSummary`) are server-computed YTD figures shared with the dashboard, so a filtered
expenses total must recompute from the *visible* rows and render distinctly from the authoritative rollup, never
silently replace it.
