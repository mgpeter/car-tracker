import { Section, SectionHead, Wrap } from '../components/layout'
import { AppShell } from '../shell/AppShell'
import { PageHead } from '../shell/PageHead'
import { AccountsPanel } from './admin/AccountsPanel'
import { DeploymentPanel } from './admin/DeploymentPanel'
import { PosturePanel } from './admin/PosturePanel'

/**
 * The operator surface: the deployment, rather than a person or a car.
 *
 * **Reached only from the identity menu, and only by a principal holding `admin:read`** (DEC-023). Not in the
 * nav table for the reason the account screen is not: `hrefFor` returns `/` for any screen whose `scoped` is
 * false without reading the id, so an unscoped `ScreenId` silently resolves to the garage. It is also gated,
 * and a table whose value is being static is the wrong place for an entry almost nobody may follow.
 *
 * **It answers four questions nothing else could.** Who signed up and whether they came back; what the
 * assistant is costing across every account; what this container actually resolved for its configuration; and
 * whether somebody can be put on the paid tier without an edit to `deploy/.env` and a container recreate.
 *
 * **It reads counts and aggregates, and that is a boundary rather than a shortfall.** No fuel log, no service
 * history, no document, no anomaly message, no chat transcript, and no full registration - plates are masked
 * in the domain before they reach the payload. An operator screen that can read one account's records is a
 * tool for reading other people's data wearing an operations badge, and refusing it while nobody has asked
 * costs nothing.
 *
 * Like the account screen it takes no registration and must not reach for one: `usePlate()` throws off a
 * `:reg` route, so a stray plate here is a crash rather than a wrong label.
 */
export function AdminPage() {
  return (
    <AppShell
      scope={{ kind: 'admin' }}
      current="admin"
      center={null}
      footer={
        <>
          Everything here is about the <b>deployment</b> rather than about one account. Registrations are
          masked before they leave the server, and no account's records - logs, documents, conversations - are
          reachable from this screen.
        </>
      }
    >
      <PageHead
        eyebrow="Admin · the deployment, not a car"
        title="Admin"
        pmeta={
          <>
            Counts and posture,
            <br />
            never anybody's records
          </>
        }
      />

      <Wrap>
        {/* First, because it is the number that decides whether opening sign-up was affordable, and until
            0.26.0 it had no reader anywhere in the application. */}
        <Section>
          <SectionHead
            title="Deployment"
            rule={<>what the assistant is costing today, against the ceiling actually in force</>}
          />
          <DeploymentPanel />
        </Section>

        <Section>
          <SectionHead
            title="Accounts"
            rule={<>who signed up, what they hold, and whether they came back</>}
          />
          <AccountsPanel />
        </Section>

        <Section last>
          <SectionHead
            title="Posture"
            rule={<>what this container resolved - booleans and counts, never a secret</>}
          />
          <PosturePanel />
        </Section>
      </Wrap>
    </AppShell>
  )
}
