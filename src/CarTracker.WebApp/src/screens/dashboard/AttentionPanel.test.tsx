import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import type { VehicleSummary } from '../../api/client'
import { LinkProvider } from '../../lib/link'
import { AttentionPanel } from './AttentionPanel'

/** A minimal all-clear summary — the fields AttentionPanel actually reads. */
function summary(overrides: Partial<VehicleSummary> = {}): VehicleSummary {
  const renewal = (name: string) => ({ name, expiryDate: '2027-06-01', daysRemaining: 300, urgency: 'Ok', source: null })
  return {
    registration: 'BT53 AKJ',
    mileage: { hasNonMonotonicHistory: false, highestRecordedMileage: 80_712, currentMileage: 80_712 },
    renewals: {
      mot: renewal('MOT'),
      insurance: renewal('Insurance'),
      roadTax: renewal('Road tax'),
      nextServiceDate: renewal('Next service'),
      nextServiceMiles: null,
    },
    checks: { okCount: 5, dueSoonCount: 0, overdueCount: 0, neverLoggedCount: 0, attentionCount: 0, totalCount: 5, checks: [] },
    ...overrides,
  } as unknown as VehicleSummary
}

const renderPanel = (s: VehicleSummary) =>
  render(
    <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
      <AttentionPanel summary={s} />
    </LinkProvider>,
  )

beforeEach(() => localStorage.clear())
afterEach(() => localStorage.clear())

describe('AttentionPanel — the dismissible all-clear', () => {
  it('dismisses the all-clear and remembers it', async () => {
    const user = userEvent.setup()
    const { rerender } = renderPanel(summary())

    expect(screen.getByText('Nothing is overdue, expired, or flagged')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Dismiss' }))

    // The panel is gone, and the choice is remembered — a fresh mount stays hidden.
    expect(screen.queryByText('Nothing is overdue, expired, or flagged')).not.toBeInTheDocument()
    rerender(
      <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
        <AttentionPanel summary={summary()} />
      </LinkProvider>,
    )
    expect(screen.queryByText('Nothing is overdue, expired, or flagged')).not.toBeInTheDocument()
  })

  it('resets the dismissal when something needs attention, then shows the fresh all-clear', async () => {
    const user = userEvent.setup()
    const { rerender } = renderPanel(summary())
    await user.click(screen.getByRole('button', { name: 'Dismiss' }))

    const withLink = (s: VehicleSummary) => (
      <LinkProvider render={({ href, children, ...rest }) => <a href={href} {...rest}>{children}</a>}>
        <AttentionPanel summary={s} />
      </LinkProvider>
    )

    // An overdue check appears: the alert shows (never dismissible) and the stored dismissal is cleared.
    const alerting = summary({
      checks: { okCount: 4, dueSoonCount: 0, overdueCount: 1, neverLoggedCount: 0, attentionCount: 0, totalCount: 5, checks: [] },
    } as Partial<VehicleSummary>)
    rerender(withLink(alerting))
    expect(screen.getByText(/past its interval/)).toBeInTheDocument()

    // Resolved again: because the dismissal was reset, the fresh all-clear returns.
    rerender(withLink(summary()))
    expect(screen.getByText('Nothing is overdue, expired, or flagged')).toBeInTheDocument()
  })

  it('raises an alert for a check flagged on its last log', () => {
    // In-interval by date but its latest verdict was bad, so it sits in the attention bucket, not overdue.
    renderPanel(
      summary({
        checks: { okCount: 4, dueSoonCount: 0, overdueCount: 0, neverLoggedCount: 0, attentionCount: 1, totalCount: 5, checks: [] },
      } as Partial<VehicleSummary>),
    )
    expect(screen.getByText(/flagged on its last log/)).toBeInTheDocument()
    expect(screen.queryByText('Nothing is overdue, expired, or flagged')).not.toBeInTheDocument()
  })
})

/**
 * The head-gasket watch — the thing this panel could not say before.
 *
 * The design leads with "Head-gasket watch · lapsed"; the port could only ever count overdue checks, because
 * nothing modelled which checks were the watch. These pin that a named watch appears, that it outranks the
 * generic count, and that it never contradicts the issue's own status.
 */
describe('AttentionPanel — a named early-warning watch', () => {
  const headGasketWatch = (lapsed: number, total = 2, status = 'Resolved') => ({
    watches: [
      {
        issueId: 1,
        issueTitle: 'Head gasket — K-series risk',
        issueStatus: status,
        totalCheckCount: total,
        lapsedCheckCount: lapsed,
      },
    ],
  })

  it('names the lapsed watch instead of only counting checks', () => {
    renderPanel(summary(headGasketWatch(2) as Partial<VehicleSummary>))

    expect(screen.getByText('Head gasket — K-series risk · watch lapsed')).toBeInTheDocument()
    expect(screen.getByText(/All 2 checks watching this issue have lapsed/)).toBeInTheDocument()
  })

  it('says a Resolved issue stays resolved — the watch flags, it does not reopen', () => {
    renderPanel(summary(headGasketWatch(2) as Partial<VehicleSummary>))

    expect(screen.getByText(/status is deliberately unchanged/)).toBeInTheDocument()
    expect(screen.getByText(/resolved is contingent on them, not permanent/)).toBeInTheDocument()
  })

  it('ranks the named watch above the generic overdue-check alert', () => {
    renderPanel(
      summary({
        ...headGasketWatch(2),
        checks: { okCount: 0, dueSoonCount: 0, overdueCount: 7, neverLoggedCount: 0, attentionCount: 0, totalCount: 7, checks: [] },
      } as Partial<VehicleSummary>),
    )

    const kickers = screen.getAllByText(/watch lapsed|Regular checks · lapsed/)
    // "The two checks keeping the head gasket resolved have stopped" is a different claim from "7 checks are
    // overdue", and it is the one that explains why the count matters.
    expect(kickers[0]).toHaveTextContent('watch lapsed')
    expect(kickers[1]).toHaveTextContent('Regular checks · lapsed')
  })

  it('stays silent when every watched check is current', () => {
    renderPanel(summary(headGasketWatch(0) as Partial<VehicleSummary>))

    expect(screen.queryByText(/watch lapsed/)).not.toBeInTheDocument()
    // Nothing else is wrong either, so the panel reaches its all-clear.
    expect(screen.getByText('Nothing is overdue, expired, or flagged')).toBeInTheDocument()
  })

  it('reports a partial lapse as a fraction, not an absolute', () => {
    renderPanel(summary(headGasketWatch(1) as Partial<VehicleSummary>))

    expect(screen.getByText(/1 of 2 checks watching this issue has lapsed/)).toBeInTheDocument()
  })

  it('words a Monitoring issue differently from a Resolved one', () => {
    renderPanel(summary(headGasketWatch(2, 2, 'Monitoring') as Partial<VehicleSummary>))

    expect(screen.getByText(/still being monitored/)).toBeInTheDocument()
    expect(screen.queryByText(/status is deliberately unchanged/)).not.toBeInTheDocument()
  })
})
