import { act, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { useTableView, type FilterGroup, type SortKey } from './useTableView'

interface Row {
  n: number
  kind: 'a' | 'b'
  flagged: boolean
  /** Nullable on purpose: a real row's vendor or note is often absent, and the predicate must survive it. */
  name: string | null
  note: string | null
}

const ROWS: Row[] = [
  { n: 3, kind: 'a', flagged: false, name: 'Halfords', note: null },
  { n: 1, kind: 'b', flagged: true, name: 'Kwik Fit', note: 'rear tyres' },
  // Name absent and the match living in the other field — the two cases a naive predicate gets wrong.
  { n: 2, kind: 'a', flagged: true, name: null, note: 'HALFORDS receipt' },
]

const search = { label: 'Search rows', fields: (r: Row) => [r.name, r.note] }

const groups: FilterGroup<Row>[] = [
  {
    id: 'kind',
    label: 'Kind',
    render: 'chips',
    options: [
      { id: 'a', label: 'A', test: (r) => r.kind === 'a' },
      { id: 'b', label: 'B', test: (r) => r.kind === 'b' },
    ],
  },
  {
    id: 'flag',
    label: 'Flag',
    render: 'chips',
    options: [{ id: 'flagged', label: 'Flagged', test: (r) => r.flagged }],
  },
]

const sorts: SortKey<Row>[] = [{ id: 'n', label: 'N', compare: (a, b) => a.n - b.n }]

describe('useTableView', () => {
  it('passes everything through with no filter, in the default order', () => {
    const { result } = renderHook(() => useTableView(ROWS, { groups, sorts, defaultSortId: 'n', defaultDir: 'asc' }))
    expect(result.current.rows.map((r) => r.n)).toEqual([1, 2, 3])
    expect(result.current.count).toBe(3)
    expect(result.current.filtered).toBe(false)
  })

  it('filters within a group as OR', () => {
    const { result } = renderHook(() => useTableView(ROWS, { groups, sorts, defaultSortId: 'n', defaultDir: 'asc' }))
    act(() => result.current.toggle('kind', 'a'))
    act(() => result.current.toggle('kind', 'b'))
    // Both kinds selected → every row passes the group (OR).
    expect(result.current.count).toBe(3)
    expect(result.current.filtered).toBe(true)
  })

  it('combines groups as AND', () => {
    const { result } = renderHook(() => useTableView(ROWS, { groups, sorts, defaultSortId: 'n', defaultDir: 'asc' }))
    act(() => result.current.toggle('kind', 'a'))
    act(() => result.current.toggle('flag', 'flagged'))
    // kind a AND flagged → only n=2.
    expect(result.current.rows.map((r) => r.n)).toEqual([2])
  })

  it('sorts and reverses direction', () => {
    const { result } = renderHook(() => useTableView(ROWS, { groups, sorts, defaultSortId: 'n', defaultDir: 'asc' }))
    expect(result.current.rows.map((r) => r.n)).toEqual([1, 2, 3])
    act(() => result.current.toggleDir())
    expect(result.current.rows.map((r) => r.n)).toEqual([3, 2, 1])
  })

  it('reports zero when a filter matches nothing', () => {
    const noMatch: FilterGroup<Row>[] = [
      { id: 'k', label: 'K', render: 'chips', options: [{ id: 'x', label: 'X', test: () => false }] },
    ]
    const { result } = renderHook(() => useTableView(ROWS, { groups: noMatch, sorts }))
    act(() => result.current.toggle('k', 'x'))
    expect(result.current.count).toBe(0)
    expect(result.current.total).toBe(3)
    expect(result.current.filtered).toBe(true)
  })
})

describe('useTableView search', () => {
  const view = () =>
    renderHook(() => useTableView(ROWS, { groups, sorts, search, defaultSortId: 'n', defaultDir: 'asc' }))

  it('narrows the rows and moves the count while the total holds', () => {
    const { result } = view()
    act(() => result.current.setSearchText('kwik'))

    expect(result.current.rows.map((r) => r.n)).toEqual([1])
    expect(result.current.count).toBe(1)
    // The total is the log's size, not the visible set — it is the M in "1 of 3".
    expect(result.current.total).toBe(3)
    expect(result.current.filtered).toBe(true)
  })

  it('matches case-insensitively in either direction, across every declared field', () => {
    const { result } = view()
    // Lower-case query against a capitalised name (n=3) and an upper-case note (n=2).
    act(() => result.current.setSearchText('halfords'))

    expect(result.current.rows.map((r) => r.n)).toEqual([2, 3])
  })

  it('skips a null field rather than throwing on it', () => {
    const { result } = view()
    // n=2 has a null name. A predicate that reaches for .toLowerCase() blindly dies here.
    expect(() => act(() => result.current.setSearchText('receipt'))).not.toThrow()
    expect(result.current.rows.map((r) => r.n)).toEqual([2])
  })

  it('treats an empty or whitespace-only query as no filter at all', () => {
    const { result } = view()
    act(() => result.current.setSearchText('   '))

    // Whitespace must not read as a filter: the strip would say "0 of 3" over a full table.
    expect(result.current.count).toBe(3)
    expect(result.current.filtered).toBe(false)
  })

  it('restores every row when the box is cleared', () => {
    const { result } = view()
    act(() => result.current.setSearchText('kwik'))
    expect(result.current.count).toBe(1)

    act(() => result.current.setSearchText(''))
    expect(result.current.rows.map((r) => r.n)).toEqual([1, 2, 3])
    expect(result.current.filtered).toBe(false)
  })

  it('combines with the groups as AND', () => {
    const { result } = view()
    act(() => result.current.toggle('kind', 'a'))
    act(() => result.current.setSearchText('tyres'))

    // "rear tyres" is n=1, which is kind b — so the two narrowings have no row in common.
    expect(result.current.count).toBe(0)
    expect(result.current.filtered).toBe(true)
  })

  it('stays inert on a config that declares no search', () => {
    const { result } = renderHook(() => useTableView(ROWS, { groups, sorts, defaultSortId: 'n', defaultDir: 'asc' }))
    act(() => result.current.setSearchText('halfords'))

    // The four consumers that predate this feature must behave exactly as before.
    expect(result.current.count).toBe(3)
    expect(result.current.filtered).toBe(false)
  })
})
