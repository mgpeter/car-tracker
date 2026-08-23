import { CLIENT_STORAGE, everythingIsExempt } from '../lib/clientStorage'
import { ControllerLine, LegalLoading, LegalPage } from './LegalPage'
import { useLegal } from './useLegal'

/**
 * What this site puts on your device, and why there is no banner asking about it.
 *
 * **The table is rendered from `lib/clientStorage.ts`**, which is the registry the code itself reads its keys
 * from, so this page cannot describe a set of keys the app does not write. A hand-written list here would go
 * stale the first time somebody added one, and the staleness would be a false statement in a published legal
 * document rather than an out-of-date comment.
 */
export function CookiesPage() {
  const legal = useLegal()

  if (legal === null) return <LegalLoading title="Cookies" />

  return (
    <LegalPage title="Cookies" current="cookies" version={legal.version}>
      <ControllerLine
        controllerName={legal.controllerName}
        controllerContact={legal.controllerContact}
        controllerAddress={legal.controllerAddress}
      />

      <h2>This site sets no cookies</h2>
      <p>
        That is unusual enough to say plainly. There are no cookies here, no advertising, no analytics, no
        third-party scripts, and nothing that follows you to any other site.
      </p>
      <p>
        What it does do is remember a few things in your own browser so the app works and looks the way you
        left it. Those are listed below. They stay on your device, they are readable only by this site, and
        clearing your browser data removes all of them.
      </p>

      <h2>What is stored, and why</h2>
      <table className="legal-table">
        <thead>
          <tr>
            <th scope="col">What</th>
            <th scope="col">Why</th>
            <th scope="col">How long</th>
          </tr>
        </thead>
        <tbody>
          {CLIENT_STORAGE.map((entry) => (
            <tr key={entry.key}>
              <th scope="row">{entry.label}</th>
              <td>{entry.purpose}</td>
              <td>{entry.lifetime}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <h2>Why there is no consent banner</h2>
      {everythingIsExempt() ? (
        <p>
          Because there is nothing here to consent to. Permission is needed for things that are not necessary
          to run the service you asked for: tracking, advertising, measuring what you do. This site does none
          of them. Everything in the table above is either the sign-in that keeps you logged in, or a setting
          you chose yourself. A banner over that list would ask your permission to keep you signed in, which
          teaches people to click through banners without reading them.
        </p>
      ) : (
        <p>
          Something on this deployment now stores more than the sign-in and your own settings, and this page
          has not yet been updated to describe it. Please write to {legal.controllerName} before relying on
          the table above.
        </p>
      )}
      <p>
        If that changes, this page changes with it, and you will be asked before anything of that kind is
        stored.
      </p>

      <h2>Signing in</h2>
      <p>
        Signing in sends you briefly to Auth0, the service that handles logins. Auth0 sets its own cookies on
        its own address while it does that, so that it can recognise you next time. Those cookies belong to
        Auth0 rather than to this site, and are covered by their privacy notice.
      </p>
    </LegalPage>
  )
}
