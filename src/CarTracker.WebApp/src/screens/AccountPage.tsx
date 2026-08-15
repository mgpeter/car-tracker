import { Section, SectionHead, Wrap } from '../components/layout'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { AppearancePanel } from './account/AppearancePanel'
import { AssistantAccessPanel } from './account/AssistantAccessPanel'
import { DangerZonePanel } from './account/DangerZonePanel'
import { ReferenceListsPanel } from './account/ReferenceListsPanel'

/**
 * The account - you, rather than any of your cars.
 *
 * These four sections lived on the vehicle-scoped settings screen, which meant deleting your account, minting
 * an assistant token, renaming a garage and choosing MPG-over-L/100 km were all reached through a URL that
 * named a registration - and were, in meaning, duplicated once per car you own. The settings screen's own code
 * had said so since it shipped: a comment above its last section called that panel "the only panel here that
 * is not about a car: it is about the person". There are four such panels, and this is where they belong.
 *
 * **It takes no registration, and must not reach for one.** `useVehicleReg()` throws outside a `:reg` route
 * and `usePlate()` calls it, so a stray plate here is a crash rather than a wrong label - which is the right
 * failure, and the page's own test asserts the absence.
 */
export function AccountPage() {
  return (
    <AppShell
      scope={{ kind: 'account' }}
      current="account"
      center={null}
      footer={
        <>
          Everything here belongs to the <b>account</b>, not to a car: it stays the same whichever vehicle you
          are looking at, and there is one of each however many you own. The display preference is narrower
          still - it lives in this browser and travels to no other device.
        </>
      }
    >
      <PageHead
        eyebrow="Account · you, not your cars"
        title="Account"
        pmeta={
          <>
            One of each, however many
            <br />
            vehicles the garage holds
          </>
        }
      />

      <Wrap>
        {/* First, not last. On the settings screen this sat at the foot because it was the dangerous thing on
            a page of harmless ones; here the page *is* the account, and the way out with your data is what
            someone arriving at a screen called Account most often came for. */}
        <Section>
          <SectionHead
            title="Your account"
            rule={<>take your data out, or destroy the account and the login behind it</>}
          />
          <DangerZonePanel />
        </Section>

        <Section>
          <SectionHead
            title="Assistant access"
            rule={<>scoped MCP tokens - the secret is shown once, and every write is logged</>}
          />
          <AssistantAccessPanel />
        </Section>

        <Section>
          <SectionHead title="Appearance" rule={<>display preferences, stored on this device</>} />
          <AppearancePanel />
        </Section>

        {/* Last because it is the longest and the least often visited - three editable lists, each with its
            own subheading. It is also the only section here that is per-account rather than per-person: the
            garages and wash locations are yours, and DEC-018 is what made that true. */}
        <Section last>
          <SectionHead
            title="Reference lists"
            rule={<>the pick-lists records point at - rename cascades, delete is guarded</>}
          />
          <ReferenceListsPanel />
        </Section>
      </Wrap>
    </AppShell>
  )
}
