import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { axe } from '../test/axe'
import { TableControls } from './TableControls'
import { useTableView, type FilterGroup, type TableSearch } from './useTableView'

interface Row {
  id: number
  vendor: string | null
  note: string | null
  kind: 'a' | 'b'
}

const ROWS: Row[] = [
  { id: 1, vendor: 'Halfords', note: null, kind: 'a' },
  { id: 2, vendor: 'Kwik Fit', note: 'rear tyres', kind: 'b' },
  { id: 3, vendor: null, note: 'HALFORDS receipt', kind: 'a' },
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
]

const search: TableSearch<Row> = { label: 'Search rows', fields: (r) => [r.vendor, r.note] }

/** The real usage: a screen owns the rows, the hook owns the view, the strip renders it. */
function Host({ withSearch = true }: { withSearch?: boolean }) {
  const view = useTableView(ROWS, { groups, ...(withSearch && { search }) })
  return (
    <>
      <TableControls view={view} noun="rows" />
      <ul>
        {view.rows.map((r) => (
          <li key={r.id}>{r.vendor ?? r.note}</li>
        ))}
      </ul>
    </>
  )
}

const count = () => document.querySelector('.tctl-count')?.textContent

describe('TableControls search', () => {
  it('renders no search input when the screen declares none', () => {
    render(<Host withSearch={false} />)
    // The four screens that predate the feature must get the strip exactly as it was.
    expect(screen.queryByRole('searchbox')).not.toBeInTheDocument()
  })

  it('names the input from the declared label, with no aria-label', () => {
    render(<Host />)
    const box = screen.getByRole('searchbox', { name: 'Search rows' })
    // The strip's own idiom is a wrapping <label>; an aria-label here would be a second way to do it.
    expect(box).not.toHaveAttribute('aria-label')
  })

  it('narrows the rows as you type and moves the live count', async () => {
    const user = userEvent.setup()
    render(<Host />)
    expect(count()).toMatch(/^\s*3 rows\s*$/)

    await user.type(screen.getByRole('searchbox', { name: 'Search rows' }), 'kwik')

    expect(screen.getByText('Kwik Fit')).toBeInTheDocument()
    expect(screen.queryByText('Halfords')).not.toBeInTheDocument()
    expect(screen.queryByText('HALFORDS receipt')).not.toBeInTheDocument()
    expect(count()).toMatch(/1 of 3/)
  })

  it('restores every row when the box is cleared', async () => {
    const user = userEvent.setup()
    render(<Host />)
    const box = screen.getByRole('searchbox', { name: 'Search rows' })

    await user.type(box, 'kwik')
    expect(count()).toMatch(/1 of 3/)

    await user.clear(box)
    // Back to the plain total, not "3 of 3" — an empty box is not a filter.
    expect(count()).toMatch(/^\s*3 rows\s*$/)
  })

  it('narrows with a chip and the query together', async () => {
    const user = userEvent.setup()
    render(<Host />)

    await user.click(screen.getByRole('button', { name: 'A' }))
    await user.type(screen.getByRole('searchbox', { name: 'Search rows' }), 'tyres')

    // "rear tyres" is kind b, so the chip and the query share no row.
    expect(count()).toMatch(/0 of 3/)
  })

  it('keeps exactly one live region on the strip', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.type(screen.getByRole('searchbox', { name: 'Search rows' }), 'halfords')

    // The count already announces itself. A second live region on the search would double-announce.
    expect(document.querySelectorAll('.tctl [aria-live]')).toHaveLength(1)
  })

  it('has no accessibility violations with the search box present', async () => {
    const { container } = render(<Host />)
    expect(await axe(container)).toHaveNoViolations()
  })
})
