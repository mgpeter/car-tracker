import { useVehicleSummary } from '../api/queries'
import { Panel, Section, SectionHead, Wrap } from '../components/layout'
import { PageHead } from '../shell/PageHead'
import { AppShell } from '../shell/AppShell'
import { useVehicleReg } from '../routes'
import { AppearancePanel } from './settings/AppearancePanel'
import { AssistantAccessPanel } from './settings/AssistantAccessPanel'
import { CheckDefinitionsPanel } from './settings/CheckDefinitionsPanel'
import { DangerZonePanel } from './settings/DangerZonePanel'
import { FuelTankPanel } from './settings/FuelTankPanel'
import { ReferenceListsPanel } from './settings/ReferenceListsPanel'
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

      {/* <Section>, not a raw <section>. This page was the one place that hand-rolled the primitive, which is
          how it ended up being the one page whose section rhythm was inconsistent. */}
      <Wrap>
        <Section>
          <SectionHead title="Statutory & policies" rule={<>drives the renewals panel on the dashboard</>} />
          {isPending ? <Panel><p style={{ padding: 18, margin: 0, color: 'var(--muted)' }}>Loading…</p></Panel> : <StatutoryPanel reg={reg} summary={data} />}
        </Section>

        <Section>
          <SectionHead title="Fuel tank" rule={<>drives the full-tank range on the dashboard</>} />
          <FuelTankPanel reg={reg} />
        </Section>

        <Section>
          <SectionHead title="Reference lists" rule={<>the pick-lists records point at — rename cascades, delete is guarded</>} />
          <ReferenceListsPanel />
        </Section>

        <Section>
          <SectionHead title="Appearance" rule={<>display preferences, stored on this device</>} />
          <AppearancePanel />
        </Section>

        <Section>
          <SectionHead title="Assistant access" rule={<>scoped MCP tokens — the secret is shown once, and every write is logged</>} />
          <AssistantAccessPanel />
        </Section>

        <Section>
          <CheckDefinitionsPanel reg={reg} />
        </Section>

        {/* Last on the page, and the only panel here that is not about a car: it is about the person. The
            export sits inside it above the deletion, so the way out with your data is the one you meet first. */}
        <Section last>
          <SectionHead
            title="Your account"
            rule={<>take your data out, or destroy the account and the login behind it</>}
          />
          <DangerZonePanel />
        </Section>
      </Wrap>
    </AppShell>
  )
}
