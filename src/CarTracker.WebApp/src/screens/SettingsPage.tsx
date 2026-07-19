import { useVehicleSummary } from '../api/queries'
import { Panel, SectionHead, Wrap } from '../components/layout'
import { PageHead } from '../shell/PageHead'
import { AppShell } from '../shell/AppShell'
import { useVehicleReg } from '../routes'
import { AppearancePanel } from './settings/AppearancePanel'
import { CheckDefinitionsPanel } from './settings/CheckDefinitionsPanel'
import { FuelTankPanel } from './settings/FuelTankPanel'
import { StatutoryPanel } from './settings/StatutoryPanel'

/**
 * Settings — the only place stored values live.
 *
 * This is the M1 slice: statutory policies and check definitions. Both are load-bearing rather than
 * convenience, because two other screens have nothing to show without them — the dashboard's renewals derive
 * from the policies, and the checks screen renders a vehicle's definitions, which nothing else creates.
 *
 * The rest of the design's Settings — reference lists, budget targets, quick-add order, reminders, appearance,
 * MCP tokens, export/backup — lands with M2 and later phases.
 */
export function SettingsPage() {
  const reg = useVehicleReg()
  const { data, isPending } = useVehicleSummary(reg)

  return (
    <AppShell
      scope={{ kind: 'vehicle', reg }}
      current="settings"
      center={{ kind: 'link', screen: 'settings' }}
      footer={
        <>
          Settings holds the stored inputs: reference data, policies, targets and definitions. Everything
          downstream — countdowns, MPG, budgets, check status — is <b>computed from the logs</b> and can never
          go stale.
        </>
      }
    >
      <PageHead
        eyebrow="Settings · the only place stored values live"
        title="Settings"
        // The vehicle's OWN registration, not the URL slug. The route param is normalised for matching
        // ("bt53akj"), which is right for a URL and wrong on a plate — it renders "BT53AKJ" and no plate in
        // Britain looks like that. The URL locates the car; the summary says what it is called.
        plate={data?.registration ?? reg}
        pmeta={
          <>
            Everything else in the app is computed —<br />
            this screen holds the inputs and the policies
          </>
        }
      />

      <Wrap>
        <section>
          <SectionHead title="Statutory & policies" rule={<>drives the renewals panel on the dashboard</>} />
          {isPending ? <Panel><p style={{ padding: 18, margin: 0, color: 'var(--muted)' }}>Loading…</p></Panel> : <StatutoryPanel reg={reg} summary={data} />}
        </section>

        <section>
          <SectionHead title="Fuel tank" rule={<>drives the full-tank range on the dashboard</>} />
          <FuelTankPanel reg={reg} />
        </section>

        <section>
          <SectionHead title="Appearance" rule={<>display preferences, stored on this device</>} />
          <AppearancePanel />
        </section>

        <section className="last">
          <CheckDefinitionsPanel reg={reg} />
        </section>
      </Wrap>
    </AppShell>
  )
}
