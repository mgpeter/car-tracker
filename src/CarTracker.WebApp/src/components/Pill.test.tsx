import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { DUE_STATUS, PRIORITY, type DueStatus, type Priority } from '../lib/status'
import { axe } from '../test/axe'
import { expectStatesDistinguishable, expectStateIsReadable } from '../test/greyscale'
import { DueBadge, IntegrityPill, Pill, PrioTag } from './Pill'

const DUE_STATES = Object.keys(DUE_STATUS) as DueStatus[]
const PRIORITIES = Object.keys(PRIORITY) as Priority[]

describe('Pill', () => {
  it('renders its text with the tone as a class', () => {
    render(<Pill tone="ok">Owned</Pill>)
    const el = screen.getByText('Owned')
    expect(el).toHaveClass('pill', 'ok')
  })

  // The design proves .pill is generic, not a status component: `pill ok` carries "Owned"/"Best"/"Healthy",
  // and `pill soon` carries "Read-write" — a token scope. Modelling it as status-only would have been wrong.
  it.each(['Owned', 'Best', 'Read-write', 'On order', 'No budget'])('carries arbitrary label %s', (label) => {
    render(<Pill tone="plain">{label}</Pill>)
    expect(screen.getByText(label)).toBeInTheDocument()
  })

  it('has no axe violations', async () => {
    const { container } = render(<Pill tone="due">Over</Pill>)
    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('DueBadge — the greyscale property', () => {
  // Driven off the record, so a fifth domain status is covered the moment it is added rather than silently
  // untested.
  it.each(DUE_STATES)('%s renders its state as readable text', (due) => {
    const result = render(<DueBadge due={due} />)
    expectStateIsReadable(result, DUE_STATUS[due].label)
  })

  it('renders no state as colour alone', () => {
    for (const due of DUE_STATES) {
      const { container, unmount } = render(<DueBadge due={due} />)
      expect(container.textContent?.trim(), `${due} rendered no text`).not.toBe('')
      unmount()
    }
  })

  it('gives every state a distinct label, so colour is never the only difference', () => {
    expectStatesDistinguishable(DUE_STATES.map((d) => DUE_STATUS[d].label))
  })

  it('never renders "Never logged" as OK — the workbook bug this project exists to kill', () => {
    render(<DueBadge due="NeverLogged" />)
    // The sheet has 18 check definitions and counts 17: the never-logged one silently joins no bucket.
    // Collapsing NeverLogged into Ok reproduces that exactly.
    expect(screen.getByText('Never logged')).toBeInTheDocument()
    expect(screen.queryByText('OK')).not.toBeInTheDocument()
  })

  it('keeps never-logged off the data-integrity axis', () => {
    const { container } = render(<DueBadge due="NeverLogged" />)
    // Blue means "this datum is unreliable". Never-logged is a real due state, not a data problem.
    expect(container.querySelector('.pill')).not.toHaveClass('info')
  })

  it.each(DUE_STATES)('%s has no axe violations', async (due) => {
    const { container } = render(<DueBadge due={due} />)
    expect(await axe(container)).toHaveNoViolations()
  })
})

describe('the axes cannot be conflated — enforced by the type checker', () => {
  // `@ts-expect-error` FAILS THE BUILD if the line below compiles. So these are not comments about the
  // guard; they are the guard's test, run by `tsc -b`. If someone widens PillTone to include 'info', or
  // gives DueBadge a label prop, tsc reports "unused @ts-expect-error" and the build breaks.
  it('rejects the info tone on a Pill', () => {
    const rejected = (
      // @ts-expect-error — 'info' is the data-integrity axis; <Pill> must never accept it. Use <IntegrityPill>.
      <Pill tone="info">Overdue</Pill>
    )
    expect(rejected).toBeDefined()
  })

  it('rejects an invented tone', () => {
    const rejected = (
      // @ts-expect-error — PillTone is a closed union; colour is not an open channel.
      <Pill tone="purple">Whatever</Pill>
    )
    expect(rejected).toBeDefined()
  })

  it('gives DueBadge no way to be handed an empty label', () => {
    const rejected = (
      // @ts-expect-error — there is no label prop by design: the label is derived, so it cannot be blanked.
      <DueBadge due="Overdue" label="" />
    )
    expect(rejected).toBeDefined()
  })

  it('rejects a due status the domain does not have', () => {
    const rejected = (
      // @ts-expect-error — DueStatus mirrors CarTracker.Shared.Metrics.CheckStatus exactly.
      <DueBadge due="Pending" />
    )
    expect(rejected).toBeDefined()
  })
})

describe('IntegrityPill', () => {
  it('is the only thing that renders the info tone', () => {
    render(<IntegrityPill>Unreliable</IntegrityPill>)
    expect(screen.getByText('Unreliable')).toHaveClass('pill', 'info')
  })

  it('exists so that Pill need not accept info', () => {
    render(
      <>
        <IntegrityPill>Recomputed</IntegrityPill>
        <DueBadge due="Overdue" />
      </>,
    )
    expect(screen.getByText('Recomputed')).toHaveClass('info')
    expect(screen.getByText('Overdue')).not.toHaveClass('info')
  })
})

describe('PrioTag', () => {
  it.each(PRIORITIES)('%s renders its label as text', (priority) => {
    const result = render(<PrioTag priority={priority} />)
    expectStateIsReadable(result, PRIORITY[priority].label)
  })

  // The domain has Priority.High; the design renders only med and low, and orphaned a `.prio.crit` rule.
  // Dropping it as "dead" would have left the most important priority with no rendering at all.
  it('renders High, which the design never did', () => {
    render(<PrioTag priority="High" />)
    expect(screen.getByText('High')).toHaveClass('prio', 'due')
  })

  it('gives every priority a distinct label', () => {
    expectStatesDistinguishable(PRIORITIES.map((p) => PRIORITY[p].label))
  })
})
