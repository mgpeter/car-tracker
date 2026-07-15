import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { IconSprite } from '../components/IconSprite'
import { __resetScrollLock } from '../lib/useScrollLock'
import { ToastProvider } from '../shell/Toast'
import { ThemeProvider } from '../theme/ThemeProvider'
import { axe } from '../test/axe'
import { GalleryPage } from './Gallery'

beforeEach(() => {
  __resetScrollLock()
  localStorage.clear()
  document.documentElement.removeAttribute('data-theme')
  vi.stubGlobal(
    'matchMedia',
    vi.fn(() => ({ matches: false, media: '', addEventListener: () => {}, removeEventListener: () => {} })),
  )
})

const renderGallery = () =>
  render(
    <ThemeProvider>
      <ToastProvider>
        <IconSprite />
        <div id="root">
          <GalleryPage />
        </div>
      </ToastProvider>
    </ThemeProvider>,
  )

describe('the gallery', () => {
  it('renders every due state', () => {
    renderGallery()
    for (const label of ['OK', 'Due soon', 'Overdue', 'Never logged']) {
      expect(screen.getAllByText(label).length).toBeGreaterThan(0)
    }
  })

  it('renders every priority, including the High the design could not', () => {
    renderGallery()
    for (const label of ['High', 'Medium', 'Low']) {
      expect(screen.getByText(label)).toBeInTheDocument()
    }
  })

  it('shows the four check buckets summing to the workbook total', () => {
    renderGallery()
    // 7 + 3 + 7 + 1 = 18. The sheet says 17 because never-logged joins no bucket.
    expect(screen.getByText('18')).toBeInTheDocument()
  })

  it('fires a toast through the one provider', async () => {
    const user = userEvent.setup()
    renderGallery()
    await user.click(screen.getByRole('button', { name: 'Fire a toast' }))
    expect(screen.getByRole('status')).toHaveTextContent(/one owner, one timer/)
  })

  it('opens the sheet and submits on Enter', async () => {
    const user = userEvent.setup()
    renderGallery()
    await user.click(screen.getByRole('button', { name: 'Add fuel' }))

    const dialog = screen.getByRole('dialog', { name: 'Add fuel' })
    expect(dialog).toHaveAttribute('aria-modal', 'true')

    await user.click(screen.getByLabelText('Mileage'))
    await user.keyboard('80712{Enter}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent(/MPG computed live/)
  })

  it('toggles the greyscale proof', async () => {
    const user = userEvent.setup()
    const { container } = renderGallery()
    expect(container.querySelector('#gallery')).not.toHaveClass('greyscale-proof')

    await user.click(screen.getByRole('button', { name: 'Remove colour' }))
    expect(container.querySelector('#gallery')).toHaveClass('greyscale-proof')
    // The states must still be readable — this is the mechanism half of the claim; the visual half is checked
    // in Chrome, because jsdom has neither colour nor layout.
    for (const label of ['Overdue', 'OK']) {
      expect(screen.getAllByText(label).length).toBeGreaterThan(0)
    }
  })

  // The whole-page sweep. This is the surface task 4.7 asks for.
  it('has no axe violations', async () => {
    const { container } = renderGallery()
    expect(await axe(container)).toHaveNoViolations()
  })

  it('has no axe violations with the sheet open', async () => {
    const user = userEvent.setup()
    renderGallery()
    await user.click(screen.getByRole('button', { name: 'Add fuel' }))
    expect(await axe(document.body)).toHaveNoViolations()
  })
})
