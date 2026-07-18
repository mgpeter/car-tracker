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
    checks: { okCount: 5, dueSoonCount: 0, overdueCount: 0, neverLoggedCount: 0, totalCount: 5, checks: [] },
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
      checks: { okCount: 4, dueSoonCount: 0, overdueCount: 1, neverLoggedCount: 0, totalCount: 5, checks: [] },
    } as Partial<VehicleSummary>)
    rerender(withLink(alerting))
    expect(screen.getByText(/past its interval/)).toBeInTheDocument()

    // Resolved again: because the dismissal was reset, the fresh all-clear returns.
    rerender(withLink(summary()))
    expect(screen.getByText('Nothing is overdue, expired, or flagged')).toBeInTheDocument()
  })
})
