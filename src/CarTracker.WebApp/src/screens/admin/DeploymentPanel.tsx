import { useQuery } from '@tanstack/react-query'
import { DerivedRow } from '../../components/SettingRow'
import { Panel } from '../../components/layout'
import { adminKeys, getAdminUsage } from '../../api/admin'

const tokens = (n: number) => n.toLocaleString('en-GB')

const bytes = (n: number) =>
  n >= 1_000_000 ? `${(n / 1_000_000).toFixed(1)} MB` : `${Math.round(n / 1_000)} kB`

/**
 * What this deployment is spending, and how much of it there is.
 *
 * **The top figure is the one nothing in the application showed before 0.26.0.** `chat_usage` has held
 * per-account, per-day tokens since the assistant shipped and `ChatBudget` reads them on every turn to enforce
 * two ceilings; the deployment-wide total against the deployment-wide ceiling was visible to nobody. An owner
 * could see their own allowance on their own plan panel, and the number that decides whether opening sign-up
 * is affordable had no reader at all.
 *
 * `DerivedRow` rather than a stat tile, because a `StatTile`'s label is derived from a `DueStatus` and none of
 * these figures sit on that axis - there is no "overdue" amount of spend, only an amount.
 */
export function DeploymentPanel() {
  const { data, isError } = useQuery({ queryKey: adminKeys.usage, queryFn: getAdminUsage })

  if (isError) {
    return (
      <Panel>
        <p className="faint">Could not read deployment usage.</p>
      </Panel>
    )
  }

  const usage = data?.usage
  const today = usage?.today
  const spentToday = today
    ? today.inputTokens + today.outputTokens + today.cacheReadTokens + today.cacheWriteTokens
    : undefined

  return (
    <Panel>
      <DerivedRow
        label="Tokens today"
        badge={<span className="pill">chat</span>}
        value={spentToday === undefined ? '…' : tokens(spentToday)}
        source={
          data === undefined ? (
            'reading'
          ) : data.chatConfigured ? (
            <>
              of {tokens(data.dailyTokenCeiling)} allowed across the deployment · {usage?.accountsActiveToday ?? 0}{' '}
              {usage?.accountsActiveToday === 1 ? 'account' : 'accounts'} active · {today?.turns ?? 0} turns
            </>
          ) : (
            // The ceiling is meaningless without a credential, and printing it would suggest the assistant is
            // merely idle rather than switched off for everybody.
            'no model credential on this deployment, so the assistant is off for every account'
          )
        }
      />

      <DerivedRow
        label="Per-account ceiling"
        badge={<span className="pill">chat</span>}
        value={data === undefined ? '…' : tokens(data.perOwnerTokenCeiling)}
        source="tokens a day, before the deployment-wide ceiling above applies on top"
      />

      <DerivedRow
        label="Accounts"
        badge={<span className="pill">total</span>}
        value={usage === undefined ? '…' : usage.totals.accounts}
        source={
          usage === undefined ? (
            'reading'
          ) : (
            <>
              holding {usage.totals.vehicles} {usage.totals.vehicles === 1 ? 'vehicle' : 'vehicles'} and{' '}
              {usage.totals.documents} {usage.totals.documents === 1 ? 'document' : 'documents'} (
              {bytes(usage.totals.documentBytes)})
            </>
          )
        }
      />

      <DerivedRow
        label="Registration lookups"
        badge={<span className="pill">today</span>}
        value={usage === undefined ? '…' : usage.vehicleLookupsToday}
        source="DVLA calls that reached the upstream, across every account"
      />
    </Panel>
  )
}
