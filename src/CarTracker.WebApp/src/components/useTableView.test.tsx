import { act, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { useTableView, type FilterGroup, type SortKey } from './useTableView'

interface Row {
  n: number
  kind: 'a' | 'b'
  flagged: boolean
}

const ROWS: Row[] = [
  { n: 3, kind: 'a', flagged: false },
  { n: 1, kind: 'b', flagged: true },
  { n: 2, kind: 'a', flagged: true },
]

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
