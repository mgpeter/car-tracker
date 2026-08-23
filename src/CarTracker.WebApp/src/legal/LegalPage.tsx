import type { ReactNode } from 'react'
import { Footer } from '../shell/AppShell'
import { Section, Wrap } from '../components/layout'

/**
 * The shell the three public documents share.
 *
 * **Deliberately not `AppShell`.** These pages render in both session states and belong to neither a vehicle
 * nor an account, so putting them in the shell would need a `CurrentScreen` value - and `hrefFor`
 * (`lib/link.tsx`) returns `/` for any screen whose `scoped` is false without ever reading the id, which is
 * the trap the account screen documented and avoided the same way. `ScreenId` gains nothing here, neither nav
 * table is touched, and a signed-out visitor gets no menu pointing at screens they cannot reach.
 *
 * It keeps the shared `Footer`, because the version line belongs on every page of the site.
 */
export function LegalPage({
  title,
  current,
  version,
  children,
}: {
  title: string
  /** Which document this is, so its own link is not offered back to the reader. */
  current: 'privacy' | 'cookies' | 'terms'
  /** From `meta.legal`, so the page and `users.terms_version` read one definition. */
  version: string
  children: ReactNode
}) {
  const others = LINKS.filter((l) => l.id !== current)

  return (
    <main className="legal">
      <Wrap>
        <Section last>
          <h1>{title}</h1>
          {children}

          <p className="legal-meta">
            {/* Served from the API rather than held as a second constant here. It is not configurable - a
                deployment cannot version text it did not write - but it must be the SAME value that
                `AccountProvisioner` stamps on `users.terms_version`, and a copy in the bundle is a copy that
                can disagree about which text somebody was actually shown. */}
            Version {version}.{' '}
            {others.map((link, i) => (
              <span key={link.id}>
                {i > 0 && ' · '}
                <a href={link.href}>{link.label}</a>
              </span>
            ))}
          </p>
        </Section>
      </Wrap>
      <Footer>
        Made by <a href="https://usualexpat.com">usualexpat.com</a>. The source is on{' '}
        <a href="https://github.com/mgpeter/car-tracker">GitHub</a> - read exactly what it does with what you
        log.
      </Footer>
    </main>
  )
}

const LINKS = [
  { id: 'privacy' as const, href: '/privacy', label: 'Privacy' },
  { id: 'cookies' as const, href: '/cookies', label: 'Cookies' },
  { id: 'terms' as const, href: '/terms', label: 'Terms' },
]

/**
 * The line every document opens with, once the deployment has said who it is.
 *
 * Rendered by all three, so the controller is stated once per page in one voice rather than three times in
 * three. Null while `meta` is in flight, which is a fraction of a second and reads as a page still loading.
 */
export function ControllerLine({
  controllerName,
  controllerContact,
  controllerAddress,
}: {
  controllerName: string
  controllerContact: string
  controllerAddress?: string | null
}) {
  return (
    <p>
      This site is run by <b>{controllerName}</b>. If you have a question about your data, or want to make any
      of the requests below, write to <a href={`mailto:${controllerContact}`}>{controllerContact}</a>.
      {controllerAddress !== null && controllerAddress !== undefined && <> The postal address is {controllerAddress}.</>}
    </p>
  )
}

/**
 * What a page shows while the anonymous `meta` call is in flight.
 *
 * **A splash rather than a redirect**, because "in flight" and "this deployment publishes nothing" are
 * indistinguishable from here and the two want opposite treatment. `LegalRoute` below waits for the answer
 * before deciding, so a slow network never bounces somebody off a page that was about to render.
 */
export function LegalLoading({ title }: { title: string }) {
  return (
    <main className="legal">
      <Wrap>
        <Section last>
          <h1>{title}</h1>
          <p>Loading…</p>
        </Section>
      </Wrap>
    </main>
  )
}
