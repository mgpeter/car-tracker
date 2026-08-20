import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { useAddOnArrival, useAddRequest } from './useAddOnArrival'

/**
 * The receiving half of quick add.
 *
 * Two properties, and both are the kind that look fine by hand and rot silently. **It fires once** - the
 * caller's sheet sets its state back to null on close, so a dependency-driven effect would reopen it forever,
 * which is the trap `useOpenFixedRow` documents. And **the param is stripped on arrival**, so closing the
 * sheet and pressing Back does not reopen it and a refresh does not either. A URL that goes on asserting an
 * intent already acted on is the bug `useFlagFix` was written to avoid, and this is the second param to have
 * it.
 */

/** The live search string, so a test can assert the param is gone rather than trusting that it is. */
function Search() {
  return <output data-testid="search">{useLocation().search}</output>
}

function Host({ onOpen }: { onOpen: () => void }) {
  useAddOnArrival(onOpen)
  return <Search />
}

const renderAt = (path: string, onOpen: () => void) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/:reg/wash" element={<Host onOpen={onOpen} />} />
      </Routes>
    </MemoryRouter>,
  )

describe('useAddOnArrival', () => {
  it('opens the sheet when the visit asked for one', async () => {
    const open = vi.fn()
    renderAt('/bt53akj/wash?add=1', open)

    await waitFor(() => expect(open).toHaveBeenCalledTimes(1))
  })

  it('does nothing on an ordinary visit', async () => {
    const open = vi.fn()
    renderAt('/bt53akj/wash', open)

    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent(''))
    expect(open).not.toHaveBeenCalled()
  })

  it('strips the param, so Back and refresh cannot reopen it', async () => {
    const open = vi.fn()
    renderAt('/bt53akj/wash?add=1', open)

    await waitFor(() => expect(open).toHaveBeenCalled())
    expect(screen.getByTestId('search').textContent).toBe('')
  })

  it('leaves any other param alone', async () => {
    const open = vi.fn()
    renderAt('/bt53akj/wash?add=1&flag=7', open)

    await waitFor(() => expect(open).toHaveBeenCalled())
    expect(screen.getByTestId('search').textContent).toBe('?flag=7')
  })

  /**
   * The ref guard, not the dependency array, is what makes this once. A caller passing an inline arrow gets a
   * new function identity every render, and re-rendering for any unrelated reason must not re-open the sheet.
   */
  it('fires once however often the caller re-renders', async () => {
    const open = vi.fn()

    function Rerendering() {
      const [n, setN] = useState(0)
      useAddOnArrival(() => open())
      return (
        <button type="button" onClick={() => setN((v) => v + 1)}>
          bump {n}
        </button>
      )
    }

    render(
      <MemoryRouter initialEntries={['/bt53akj/wash?add=1']}>
        <Routes>
          <Route path="/:reg/wash" element={<Rerendering />} />
        </Routes>
      </MemoryRouter>,
    )

    await waitFor(() => expect(open).toHaveBeenCalledTimes(1))

    const user = userEvent.setup()
    await user.click(screen.getByRole('button'))
    await user.click(screen.getByRole('button'))

    await waitFor(() => expect(screen.getByRole('button')).toHaveTextContent('bump 2'))
    expect(open).toHaveBeenCalledTimes(1)
  })
})

describe('useAddRequest', () => {
  function Deferred({ ready }: { ready: boolean }) {
    const add = useAddRequest()
    const [opened, setOpened] = useState(false)

    // The checks screen's shape: hold the request until the query has answered, then act on it.
    if (add.asked && ready && !opened) {
      add.taken()
      setOpened(true)
    }

    return (
      <>
        <output data-testid="opened">{String(opened)}</output>
        <Search />
      </>
    )
  }

  const renderDeferred = (ready: boolean) =>
    render(
      <MemoryRouter initialEntries={['/bt53akj/checks?add=1']}>
        <Routes>
          <Route path="/:reg/checks" element={<Deferred ready={ready} />} />
        </Routes>
      </MemoryRouter>,
    )

  it('holds the request until the caller is ready to act on it', async () => {
    renderDeferred(false)

    // The param goes immediately even though nothing has opened yet - stripping it is about the URL, not
    // about the sheet.
    await waitFor(() => expect(screen.getByTestId('search')).toHaveTextContent(''))
    expect(screen.getByTestId('opened')).toHaveTextContent('false')
  })

  it('reports the request once the data has arrived', async () => {
    renderDeferred(true)

    await waitFor(() => expect(screen.getByTestId('opened')).toHaveTextContent('true'))
    expect(screen.getByTestId('search').textContent).toBe('')
  })
})
