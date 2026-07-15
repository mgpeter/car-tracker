import { useVehicleSummary } from '../api/queries'
import { Panel, Section, Wrap } from '../components/layout'
import { AppLink } from '../lib/link'
import { useVehicleReg } from '../routes'
import { AppShell } from '../shell/AppShell'
import { AttentionPanel } from './dashboard/AttentionPanel'
import { ChecksPanel } from './dashboard/ChecksPanel'
import { Dossier } from './dashboard/Dossier'
import { RenewalsPanel } from './dashboard/RenewalsPanel'
import { SpendPanel } from './dashboard/SpendPanel'

/**
 * The dashboard.
 *
 * Every figure here is computed at render from the logs, which is the whole premise (spec §1) and the reason
 * the workbook's own dashboard is wrong in five places. Nothing on this page is read from a stored derived
 * value, because there are none to read.
 *
 * **Two of the design's sections are absent, and that is the point rather than an omission.** Action items
 * wants tasks and issues; data integrity wants `DataAnomaly`. None of the three is loaded by
 * `VehicleMetricsLoader` or reachable over HTTP, so both panels would be exactly what the design's are —
 * hardcoded prose about a car. They land in M2 behind real read paths. Budget is out of the spend panel for
 * the same reason: `GetBudgetSummaryAsync` exists and this screen does not call it, so "Budget used 43.2%"
 * would be a decoration.
 *
 * The quick-add band is also deferred. The design hardcodes five buttons and a Settings list whose drag grips
 * do not drag; wiring the write paths it implies belongs with the fuel screen in M1e, which is where the Add
 * fill sheet lands.
 */
export function DashboardPage() {
  const reg = useVehicleReg()
  const { data, isPending, isError, error, refetch } = useVehicleSummary(reg)

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="dashboard"
      center={{ kind: 'link', screen: 'fuel' }}
      footer={
        data === undefined ? undefined : (
          <>
            Reference date <b>{new Date(`${data.asOfDate}T00:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' })}</b> · vehicle{' '}
            <b>{data.registration}</b>. Every figure on this page is computed at render from the logs —
            countdowns, MPG, cost-per-mile and check status. Nothing derived is stored, so nothing here can go
            stale.
          </>
        )
      }
    >
      {isError ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <h2 className="panel-title">This vehicle's figures could not be loaded</h2>
              <p className="panel-empty">
                {error instanceof Error ? error.message : 'The request failed.'}
              </p>
              <button className="btn" type="button" onClick={() => void refetch()}>
                Try again
              </button>{' '}
              <AppLink className="btn ghost" to="garage">
                Back to the garage
              </AppLink>
            </Panel>
          </Wrap>
        </Section>
      ) : isPending || data === undefined ? (
        <Section last>
          <Wrap>
            <Panel className="pad">
              <p className="panel-empty">Loading…</p>
            </Panel>
          </Wrap>
        </Section>
      ) : (
        <>
          <Dossier summary={data} />
          <AttentionPanel summary={data} />
          <RenewalsPanel summary={data} />
          <SpendPanel summary={data} />
          <ChecksPanel summary={data} />
        </>
      )}
    </AppShell>
  )
}
