import { useMemo, useState } from 'react'

/** One filter option — a chip or a dropdown entry. */
export interface FilterOption<T> {
  id: string
  label: string
  test: (row: T) => boolean
}

/**
 * A group of filter options. Chips within a group are OR (any selected option passes); groups are AND (a row
 * must pass every active group). An empty selection means the group is inactive and passes everything — the
 * "All" state, rendered as a distinct clear chip.
 */
export interface FilterGroup<T> {
  id: string
  label: string
  /** Chips are multi-select buttons; a select is single-select. Both OR within, AND across. */
  render: 'chips' | 'select'
  options: FilterOption<T>[]
}

export interface SortKey<T> {
  id: string
  label: string
  compare: (a: T, b: T) => number
}

/**
 * Free-text search over a row's text.
 *
 * It lives here rather than beside the hook because `count`, `total` and `filtered` are all derived from the
 * filter state, and `TableControls` renders "3 of 13" from them — a search that narrowed rows anywhere else
 * would leave the strip announcing a number the table no longer matches.
 */
export interface TableSearch<T> {
  /** The visible label, and therefore the accessible name of the input. e.g. "Search expenses". */
  label: string
  /**
   * Every text field a query may match — including ones no column renders. A service record's `notes` holds
   * the MOT advisories and is shown nowhere, which is exactly the thing worth finding.
   */
  fields: (row: T) => (string | null | undefined)[]
}

export interface TableViewConfig<T> {
  groups?: FilterGroup<T>[]
  sorts?: SortKey<T>[]
  /** The log's current fixed order, so no-filter behaviour is identical to today. */
  defaultSortId?: string
  defaultDir?: 'asc' | 'desc'
  /** Omit it and no search input renders — the consumers that predate this feature are unchanged. */
  search?: TableSearch<T>
}

export interface TableView<T> {
  /** Filtered, sorted rows — what the table renders. */
  rows: T[]
  /** How many rows survived the filter. */
  count: number
  /** How many rows there were before filtering — for "3 of 13". */
  total: number
  /** Whether any filter is narrowing the rows (so an empty result reads as "nothing matches", not "empty log"). */
  filtered: boolean
  /** Selected option ids per group. */
  selected: Record<string, string[]>
  isSelected: (groupId: string, optionId: string) => boolean
  toggle: (groupId: string, optionId: string) => void
  /** Select exactly one option in a group (dropdowns; empty string clears). */
  select: (groupId: string, optionId: string) => void
  clearGroup: (groupId: string) => void
  sortId: string | null
  sortDir: 'asc' | 'desc'
  setSort: (id: string) => void
  toggleDir: () => void
  /** The raw query, exactly as typed — trimming is the predicate's business, not the input's. */
  searchText: string
  setSearchText: (value: string) => void
  config: Required<Pick<TableViewConfig<T>, never>> & {
    groups: FilterGroup<T>[]
    sorts: SortKey<T>[]
    search?: TableSearch<T>
  }
}

/**
 * Filter, sort and count a log's rows — the capability README §3.2 asks of every log table, built once so no
 * two drift. It is the fourth extension of the `<DataTable>` seam (after columns, priority and reflow) and it
 * keeps the table a pure renderer: the hook filters, the table renders what it is given. A `rows.filter()`
 * inside `DataTable` would be the fork the seam exists to avoid.
 */
export function useTableView<T>(rows: T[], config: TableViewConfig<T>): TableView<T> {
  const groups = config.groups ?? []
  const sorts = config.sorts ?? []
  const search = config.search
  const [selected, setSelected] = useState<Record<string, string[]>>({})
  const [sortId, setSortId] = useState<string | null>(config.defaultSortId ?? sorts[0]?.id ?? null)
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>(config.defaultDir ?? 'desc')
  const [searchText, setSearchText] = useState('')

  const isSelected = (groupId: string, optionId: string) => (selected[groupId] ?? []).includes(optionId)

  const toggle = (groupId: string, optionId: string) =>
    setSelected((s) => {
      const current = s[groupId] ?? []
      const next = current.includes(optionId) ? current.filter((id) => id !== optionId) : [...current, optionId]
      return { ...s, [groupId]: next }
    })

  const select = (groupId: string, optionId: string) =>
    setSelected((s) => ({ ...s, [groupId]: optionId === '' ? [] : [optionId] }))

  const clearGroup = (groupId: string) => setSelected((s) => ({ ...s, [groupId]: [] }))

  const setSort = (id: string) => setSortId(id)
  const toggleDir = () => setSortDir((d) => (d === 'asc' ? 'desc' : 'asc'))

  const anySelected = groups.some((g) => (selected[g.id] ?? []).length > 0)
  // Trimmed, so a box holding only spaces is not a filter — the strip would otherwise read "0 of 13" over a
  // table the user has not actually narrowed. A search a consumer never declared can never be active.
  const query = search === undefined ? '' : searchText.trim().toLowerCase()

  const visible = useMemo(() => {
    const filteredRows = rows.filter(
      (row) =>
        groups.every((g) => {
          const sel = selected[g.id] ?? []
          if (sel.length === 0) return true // inactive group passes everything
          // OR within the group: any selected option whose test passes.
          return g.options.some((o) => sel.includes(o.id) && o.test(row))
        }) &&
        // AND the search, on the same terms: an empty query passes everything. One case-insensitive
        // substring per field — not term-splitting, so the rule is one a reader can predict. The optional
        // chain is doing real work: a null vendor or an absent note is ordinary, not exceptional.
        (query === '' || (search?.fields(row) ?? []).some((f) => f?.toLowerCase().includes(query) === true)),
    )

    const sort = sorts.find((s) => s.id === sortId)
    if (sort === undefined) return filteredRows
    const dir = sortDir === 'asc' ? 1 : -1
    return [...filteredRows].sort((a, b) => dir * sort.compare(a, b))
  }, [rows, groups, sorts, selected, sortId, sortDir, search, query])

  return {
    rows: visible,
    count: visible.length,
    total: rows.length,
    // A query narrows exactly as a chip does, so it must read as filtered too — that is what makes an empty
    // result say "nothing matches" rather than "empty log", and what puts the count into "N of M" form.
    filtered: anySelected || query !== '',
    selected,
    isSelected,
    toggle,
    select,
    clearGroup,
    sortId,
    sortDir,
    setSort,
    toggleDir,
    searchText,
    setSearchText,
    config: { groups, sorts, ...(search !== undefined && { search }) },
  }
}
