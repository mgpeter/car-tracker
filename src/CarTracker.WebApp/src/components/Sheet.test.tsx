import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { __resetScrollLock } from '../lib/useScrollLock'
import { axe } from '../test/axe'
import { Btn } from './Btn'
import { Field, Select, Sheet } from './Sheet'

// beforeEach, NOT afterEach. The scroll lock's counter is module-level, and RTL's cleanup runs in an afterEach
// registered by setup.ts — resetting the counter in a *later* afterEach means the unmount that follows
// decrements it to -1, and the next test's `depth === 0` branch never fires, so the lock silently stops
// applying. Resetting up front lets cleanup balance the counter normally and still recovers from any leak.
beforeEach(() => __resetScrollLock())

/** A realistic host: a trigger outside the sheet, so focus restore has somewhere to go back to. */
function Host({ onSubmit }: { onSubmit?: () => void }) {
  const [open, setOpen] = useState(false)
  return (
    <div id="root">
      <button type="button" onClick={() => setOpen(true)}>
        Log wash
      </button>
      <Sheet
        open={open}
        onClose={() => setOpen(false)}
        title="Log wash"
        subtitle="last 30 Jun · 14 days ago"
        {...(onSubmit !== undefined && { onSubmit })}
        footer={<Btn onClick={() => {}} type={onSubmit === undefined ? 'button' : 'submit'}>Save wash</Btn>}
      >
        <Field label="Mileage">{(p) => <input type="text" inputMode="numeric" {...p} />}</Field>
        <Field label="Cost £" hint="£0 skips the expense mirror">
          {(p) => <input type="text" inputMode="decimal" {...p} />}
        </Field>
        <Field label="Location">
          {(p) => (
            <Select {...p}>
              <option>IMO Kingston</option>
              <option>Driveway</option>
            </Select>
          )}
        </Field>
      </Sheet>
    </div>
  )
}

describe('Sheet — everything the design lacks', () => {
  it('is a real modal dialog, named by its heading', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Log wash' }))

    const dialog = screen.getByRole('dialog', { name: 'Log wash' })
    // The design has role="dialog" but no aria-modal, so a screen reader may still wander the page behind.
    expect(dialog).toHaveAttribute('aria-modal', 'true')
    // Named by the <h3> via id, not an aria-label duplicating it — the design duplicates, so the two drift.
    expect(dialog).toHaveAttribute('aria-labelledby')
    // The context line is announced rather than decorative.
    expect(dialog).toHaveAttribute('aria-describedby')
  })

  it('closes on Escape — which the design never handles', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Log wash' }))
    expect(screen.getByRole('dialog')).toBeInTheDocument()

    await user.keyboard('{Escape}')
    // Grepping all 17 design files for Escape/keydown returns zero matches.
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('closes on a scrim click', async () => {
    const user = userEvent.setup()
    const { container } = render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Log wash' }))

    const scrim = document.querySelector('.ovl')
    expect(scrim).not.toBeNull()
    await user.click(scrim!)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(container).toBeTruthy()
  })

  it('takes focus to the dialog itself, so the title is announced first', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Log wash' }))
    // Not the first input: focusing that skips the title and drops a screen-reader user into an unexplained
    // text box. The design autoFocuses the first input in six of its sheets.
    expect(screen.getByRole('dialog')).toHaveFocus()
  })

  it('restores focus to the trigger on close', async () => {
    const user = userEvent.setup()
    render(<Host />)
    const trigger = screen.getByRole('button', { name: 'Log wash' })
    await user.click(trigger)
    await user.keyboard('{Escape}')
    // The design leaves focus on <body> — the user is dropped at the top of the document.
    expect(trigger).toHaveFocus()
  })

  it('traps Tab inside the sheet', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Log wash' }))

    const dialog = screen.getByRole('dialog')
    // Tab all the way round; focus must never leave the dialog.
    for (let i = 0; i < 12; i++) {
      await user.tab()
      expect(dialog.contains(document.activeElement)).toBe(true)
    }
  })

  it('traps Shift+Tab backwards too', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Log wash' }))
    const dialog = screen.getByRole('dialog')

    for (let i = 0; i < 6; i++) {
      await user.tab({ shift: true })
      expect(dialog.contains(document.activeElement)).toBe(true)
    }
  })

  it('locks the page behind it and unlocks on close', async () => {
    const user = userEvent.setup()
    render(<Host />)
    expect(document.body.style.overflow).toBe('')

    await user.click(screen.getByRole('button', { name: 'Log wash' }))
    expect(document.body.style.overflow).toBe('hidden')

    await user.keyboard('{Escape}')
    expect(document.body.style.overflow).toBe('')
  })

  it('marks the page behind it inert', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Log wash' }))
    // Only possible because the sheet portals to body; inside #root it would inert itself.
    expect(document.getElementById('root')).toHaveAttribute('inert')

    await user.keyboard('{Escape}')
    expect(document.getElementById('root')).not.toHaveAttribute('inert')
  })

  it('renders nothing at all when closed', () => {
    render(<Host />)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(document.querySelector('.ovl')).toBeNull()
  })

  it('has no axe violations', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Log wash' }))
    expect(await axe(document.body)).toHaveNoViolations()
  })
})

describe('Sheet forms — Enter, which has never worked', () => {
  it('submits on Enter when given onSubmit', async () => {
    const onSubmit = vi.fn()
    const user = userEvent.setup()
    render(<Host onSubmit={onSubmit} />)
    await user.click(screen.getByRole('button', { name: 'Log wash' }))

    await user.click(screen.getByLabelText('Mileage'))
    await user.keyboard('80712{Enter}')

    // The design has ZERO <form> elements and sets enterKeyHint="done" on inputs — painting the right key on
    // a phone keyboard and wiring it to nothing. Enter does nothing in any of its 25 sheets.
    expect(onSubmit).toHaveBeenCalledOnce()
  })

  it('submits from the footer button', async () => {
    const onSubmit = vi.fn()
    const user = userEvent.setup()
    render(<Host onSubmit={onSubmit} />)
    await user.click(screen.getByRole('button', { name: 'Log wash' }))
    await user.click(screen.getByRole('button', { name: 'Save wash' }))
    expect(onSubmit).toHaveBeenCalledOnce()
  })

  it('is a plain div when it is not a form — the nav sheet is not one', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Log wash' }))
    expect(screen.getByRole('dialog').querySelector('form')).toBeNull()
  })
})

describe('Field — the label bug the design ships', () => {
  it('associates every label with its input', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Log wash' }))

    // The design's wash/garage/settings sheets use a bare <label> with the input as a SIBLING — associated
    // with nothing. Clicking does not focus it, and it announces as an unlabelled field. useId fixes it for
    // everyone at once, because the id is generated and handed over rather than typed twice.
    const mileage = screen.getByLabelText('Mileage')
    expect(mileage).toBeInTheDocument()

    await user.click(screen.getByText('Mileage'))
    expect(mileage).toHaveFocus()
  })

  it('announces the hint with the field', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Log wash' }))

    const cost = screen.getByLabelText('Cost £')
    const describedBy = cost.getAttribute('aria-describedby')
    expect(describedBy).not.toBeNull()
    expect(document.getElementById(describedBy!)).toHaveTextContent('£0 skips the expense mirror')
  })

  it('gives every field a distinct id', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Log wash' }))
    const ids = [...document.querySelectorAll<HTMLElement>('.field input, .field select')].map((e) => e.id)
    expect(new Set(ids).size).toBe(ids.length)
    expect(ids.every((i) => i !== '')).toBe(true)
  })

  it('labels a Select as well as an input', async () => {
    const user = userEvent.setup()
    render(<Host />)
    await user.click(screen.getByRole('button', { name: 'Log wash' }))
    expect(screen.getByLabelText('Location')).toBeInstanceOf(HTMLSelectElement)
  })
})
