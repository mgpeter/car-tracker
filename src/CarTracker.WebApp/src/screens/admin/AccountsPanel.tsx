import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Absent, DataTable, Sub, type Column } from '../../components/DataTable'
import { TableControls } from '../../components/TableControls'
import { useTableView } from '../../components/useTableView'
import { Panel } from '../../components/layout'
import { adminKeys, getAdminUsers, type AdminUserRow } from '../../api/admin'
import { AccountSheet } from './AccountSheet'

const day = (iso: string | null | undefined) =>
  iso === null || iso === undefined
    ? null
    : new Date(iso).toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })

const tokens = (n: number) => n.toLocaleString('en-GB')

/**
 * Every account, with enough beside each one to be worth opening.
 *
 * **The plates are masked and that happened on the server.** `RegistrationMask` runs in the domain, so no full
 * registration is in the payload this table renders - masking in the browser would be a claim about rendering
 * rather than about disclosure. It is data minimisation and not a security control: the operator has database
 * access anyway, and what this buys is a screen that can be screenshotted into a support thread.
 *
 * The search deliberately covers the masked plate as well as the address, because correlating "my car BT53
 * AKJ" from a support email against a row is the one workflow the plates are here for.
 */
export function AccountsPanel() {
  const { data, isError } = useQuery({ queryKey: adminKeys.users, queryFn: getAdminUsers })
  const [open, setOpen] = useState<number | null>(null)

  const rows = data?.users ?? []

  const view = useTableView(rows, {
    groups: [
      {
        id: 'plan',
        label: 'Plan',
        render: 'chips',
        options: [
          { id: 'pro', label: 'Pro', test: (r) => r.plan === 'Pro' },
          { id: 'free', label: 'Free', test: (r) => r.plan === 'Free' },
          { id: 'granted', label: 'Granted', test: (r) => r.planOverride !== null },
        ],
      },
      {
        id: 'address',
        label: 'Address',
        render: 'chips',
        options: [
          { id: 'verified', label: 'Verified', test: (r) => r.emailVerified },
          { id: 'unverified', label: 'Unverified', test: (r) => !r.emailVerified },
        ],
      },
    ],
    sorts: [
      { id: 'joined', label: 'Signed up', compare: (a, b) => a.createdAt.localeCompare(b.createdAt) },
      {
        id: 'seen',
        label: 'Last seen',
        // Never-seen sorts as the earliest possible, so "least recently seen" puts it first rather than
        // scattering nulls through the middle.
        compare: (a, b) => (a.lastSeenAt ?? '').localeCompare(b.lastSeenAt ?? ''),
      },
      { id: 'tokens', label: 'Tokens (30d)', compare: (a, b) => a.chatTokens30d - b.chatTokens30d },
      { id: 'vehicles', label: 'Vehicles', compare: (a, b) => a.vehicleCount - b.vehicleCount },
    ],
    defaultSortId: 'joined',
    defaultDir: 'desc',
    search: {
      label: 'Search accounts',
      fields: (r) => [r.email, r.displayName, ...r.maskedRegistrations],
    },
  })

  const columns: Column<AdminUserRow>[] = [
    {
      key: 'account',
      label: 'Account',
      width: '2fr',
      priority: 'essential',
      render: (r) => (
        <>
          {r.email}
          <Sub>
            {r.displayName ? `${r.displayName} · ` : ''}
            {r.emailVerified ? 'verified' : 'not verified'}
          </Sub>
        </>
      ),
    },
    {
      key: 'plan',
      label: 'Plan',
      width: '110px',
      priority: 'essential',
      render: (r) => (
        <>
          {r.plan}
          {r.planOverride !== null && <Sub>granted</Sub>}
        </>
      ),
    },
    {
      key: 'cars',
      label: 'Cars',
      width: '150px',
      render: (r) =>
        r.vehicleCount === 0 ? (
          <Absent>none</Absent>
        ) : (
          <>
            {r.vehicleCount}
            <Sub>{r.maskedRegistrations.join(', ')}</Sub>
          </>
        ),
    },
    {
      key: 'tokens',
      label: 'Tokens 30d',
      width: '120px',
      align: 'right',
      render: (r) =>
        r.chatTokens30d === 0 ? (
          <Absent>none</Absent>
        ) : (
          <>
            {tokens(r.chatTokens30d)}
            <Sub>{r.chatTurns30d} turns</Sub>
          </>
        ),
    },
    {
      key: 'seen',
      label: 'Last seen',
      width: '140px',
      priority: 'secondary',
      render: (r) => day(r.lastSeenAt) ?? <Absent>never</Absent>,
    },
    {
      key: 'joined',
      label: 'Signed up',
      width: '140px',
      priority: 'secondary',
      render: (r) => day(r.createdAt) ?? <Absent />,
    },
  ]

  if (isError) {
    return (
      <Panel>
        <p className="faint">Could not read the account list.</p>
      </Panel>
    )
  }

  if (data === undefined) {
    return (
      <Panel>
        <p className="faint">Reading accounts…</p>
      </Panel>
    )
  }

  return (
    <>
      <TableControls view={view} noun="accounts" />

      {view.rows.length === 0 ? (
        <Panel>
          <p className="faint">
            {view.filtered ? 'No account matches those filters.' : 'No accounts on this deployment yet.'}
          </p>
        </Panel>
      ) : (
        <DataTable
          columns={columns}
          rows={view.rows}
          rowKey={(r) => r.id}
          label="Accounts on this deployment"
          onRowClick={(r) => setOpen(r.id)}
          rowLabel={(r) => `Open ${r.email}`}
        />
      )}

      {/* The total is stated whenever it exceeds what was returned. An unpaged endpoint that truncates
          silently reads as the whole population, which is the one thing a list of accounts must not do. */}
      {data.totalAccounts > data.returned && (
        <p className="faint">
          Showing {data.returned} of {data.totalAccounts} accounts. This list is not paged.
        </p>
      )}

      {open !== null && <AccountSheet userId={open} onClose={() => setOpen(null)} />}
    </>
  )
}
