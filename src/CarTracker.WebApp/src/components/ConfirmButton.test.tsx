import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ConfirmButton } from './ConfirmButton'
import { DataTable, type Column } from './DataTable'

describe('ConfirmButton — the two-step delete', () => {
  it('does not fire on the first press, and names the cascade before it does', async () => {
    const user = userEvent.setup()
    const onConfirm = vi.fn()
    render(<ConfirmButton onConfirm={onConfirm} cascade="also removes the mirrored expense" />)

    const button = screen.getByRole('button', { name: 'Delete' })
    await user.click(button)

    // First press only arms it — nothing destructive has happened, and the label now warns what else goes.
    expect(onConfirm).not.toHaveBeenCalled()
    expect(screen.getByRole('button', { name: /Confirm delete — also removes the mirrored expense/ })).toBeInTheDocument()

    await user.click(screen.getByRole('button'))
    expect(onConfirm).toHaveBeenCalledOnce()
  })

  it('disarms on blur — a change of mind must not leave it hot', async () => {
    const user = userEvent.setup()
    const onConfirm = vi.fn()
    render(
      <>
        <ConfirmButton onConfirm={onConfirm} />
        <button type="button">elsewhere</button>
      </>,
    )

    await user.click(screen.getByRole('button', { name: 'Delete' }))
    expect(screen.getByRole('button', { name: 'Confirm delete' })).toBeInTheDocument()

    // Focus moves away, then comes back: the armed state is gone, so the next click only re-arms.
    await user.click(screen.getByRole('button', { name: 'elsewhere' }))
    await user.click(screen.getByRole('button', { name: 'Delete' }))
    expect(onConfirm).not.toHaveBeenCalled()
  })
})

describe('DataTable — row click opens the editor', () => {
  interface Row {
    id: number
    name: string
    mirrored: boolean
  }
  const columns: Column<Row>[] = [{ key: 'name', label: 'Name', width: '1fr', render: (r) => r.name }]
  const rows: Row[] = [
    { id: 1, name: 'editable', mirrored: false },
    { id: 2, name: 'shadow', mirrored: true },
  ]

  it('activates a clickable row by click and by keyboard, but leaves a gated row inert', async () => {
    const user = userEvent.setup()
    const onRowClick = vi.fn()
    render(
      <DataTable
        columns={columns}
        rows={rows}
        rowKey={(r) => r.id}
        label="rows"
        onRowClick={onRowClick}
        rowClickable={(r) => !r.mirrored}
        rowLabel={(r) => `Edit ${r.name}`}
      />,
    )

    // The editable row is a focusable, activatable control.
    const editable = screen.getByRole('row', { name: 'Edit editable' })
    await user.click(editable)
    expect(onRowClick).toHaveBeenCalledWith(rows[0])

    editable.focus()
    await user.keyboard('{Enter}')
    expect(onRowClick).toHaveBeenCalledTimes(2)

    // The mirror-shadow row carries no accessible action name and is not clickable.
    expect(screen.queryByRole('row', { name: 'Edit shadow' })).not.toBeInTheDocument()
  })
})
