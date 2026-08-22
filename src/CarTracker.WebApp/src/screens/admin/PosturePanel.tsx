import { useQuery } from '@tanstack/react-query'
import { DerivedRow } from '../../components/SettingRow'
import { Panel } from '../../components/layout'
import { adminKeys, getAdminDiagnostics } from '../../api/admin'

const yes = (on: boolean) => (on ? 'Configured' : 'Not configured')

/**
 * What the running container actually resolved.
 *
 * **This is the boot posture line, on demand, and two incidents are the argument for it.** `0.13.1` reached
 * the NAS with `Auth0__Management__*` empty and refused an invited, verified address with "not yet invited".
 * `0.24.0` reached cambelt.app with no comp list, put every account on the free tier and hid the assistant
 * from its own owner. Both were one line of configuration, both read as application faults, and both needed
 * shell access to diagnose. A line printed once at start-up only helps somebody already reading logs.
 *
 * **Every credential is a boolean.** No key, no secret, no connection string, and the comp and invitation
 * lists are counts rather than addresses - the account list already shows which accounts resolved to which
 * plan and why, which answers "is my address comped" better than printing the list would.
 */
export function PosturePanel() {
  const { data, isError } = useQuery({ queryKey: adminKeys.diagnostics, queryFn: getAdminDiagnostics })

  if (isError) {
    return (
      <Panel>
        <p className="faint">Could not read the deployment posture.</p>
      </Panel>
    )
  }

  if (data === undefined) {
    return (
      <Panel>
        <p className="faint">Reading…</p>
      </Panel>
    )
  }

  return (
    <Panel>
      <DerivedRow
        label="Sign-up"
        badge={<span className="pill">{data.signup.mode}</span>}
        value={data.signup.isClosed ? 'Closed' : 'Open'}
        source={
          data.signup.allowlistIsInert ? (
            // The same condition the 0.24.1 boot warning reports, and the shape every pre-0.24.0 deployment
            // arrives in: a populated allowlist was the door until then, so taking the release opened it.
            <>
              an invitation list is populated ({data.signup.allowedEmailCount} addresses,{' '}
              {data.signup.allowedDomainCount} domains) and is being read for nothing, because the mode is Open
            </>
          ) : (
            <>
              {data.signup.allowedEmailCount} allowed addresses, {data.signup.allowedDomainCount} allowed
              domains
            </>
          )
        }
      />

      <DerivedRow
        label="Comp list"
        badge={<span className="pill">plans</span>}
        value={data.plans.compEmailCount + data.plans.compDomainCount}
        source={
          data.plans.compEmailCount + data.plans.compDomainCount === 0 ? (
            // The 0.24.1 fault, named on the screen rather than left in a log.
            <>
              nobody is comped on this deployment, so every account without an override resolves to Free
            </>
          ) : (
            <>
              {data.plans.compEmailCount} addresses, {data.plans.compDomainCount} domains, matched against a
              verified address
            </>
          )
        }
      />

      <DerivedRow
        label="Assistant"
        badge={<span className="pill">chat</span>}
        value={yes(data.chat.configured)}
        source={
          data.chat.configured ? (
            <>
              {data.chat.model} · {data.chat.dailyTokensPerOwner.toLocaleString('en-GB')} per account,{' '}
              {data.chat.dailyTokensGlobal.toLocaleString('en-GB')} across the deployment
            </>
          ) : (
            'no model credential, so the assistant is off for every account whatever their plan'
          )
        }
      />

      <DerivedRow
        label="Registration lookup"
        badge={<span className="pill">DVLA</span>}
        value={yes(data.lookup.vesConfigured)}
        source={
          data.lookup.vesConfigured
            ? `MOT history ${data.lookup.motConfigured ? 'configured too' : 'not configured, so no expiry seed'}`
            : 'the add-car sheet offers no lookup button at all'
        }
      />

      <DerivedRow
        label="Identity management"
        badge={<span className="pill">Auth0</span>}
        value={yes(data.identity.managementConfigured)}
        source={
          data.identity.managementConfigured
            ? `${data.identity.pendingIdentityDeletions} identity deletions awaiting retry`
            : 'account deletion refuses with 503, and no address can be read for a new account'
        }
      />

      <DerivedRow
        label="Documents volume"
        badge={<span className="pill">{data.documents.writable ? 'writable' : 'read only'}</span>}
        value={data.documents.exists ? 'Present' : 'Missing'}
        source={data.documents.rootPath}
      />

      <DerivedRow
        label="Database"
        badge={<span className="pill">schema</span>}
        value={data.database.pendingMigrationCount === 0 ? 'Up to date' : `${data.database.pendingMigrationCount} pending`}
        source={data.database.lastAppliedMigration ?? 'no migration has been applied'}
      />

      <DerivedRow
        label="Build"
        badge={<span className="pill">{data.environment}</span>}
        value={data.version}
        source={
          <>
            {data.timeZone} · server time {new Date(data.serverTimeUtc).toLocaleString('en-GB')}
            {data.ownership.claimUnownedVehiclesForConfigured && ' · unowned-vehicle adoption is configured'}
          </>
        }
      />
    </Panel>
  )
}
