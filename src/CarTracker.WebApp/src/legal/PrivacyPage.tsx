import { ControllerLine, LegalLoading, LegalPage } from './LegalPage'
import { useLegal, useProcessors } from './useLegal'

/**
 * What this deployment does with what you log.
 *
 * **Written for car owners.** `legal.test.tsx` carries the jargon guard `LandingPage.test.tsx` established,
 * because a privacy policy is the most tempting place in any codebase to describe the implementation instead
 * of the effect.
 *
 * **The processors are read from the deployment, not from this file.** See `useProcessors`.
 */
export function PrivacyPage() {
  const legal = useLegal()
  const processors = useProcessors()

  if (legal === null) return <LegalLoading title="Privacy" />

  return (
    <LegalPage title="Privacy" current="privacy" version={legal.version}>
      <ControllerLine
        controllerName={legal.controllerName}
        controllerContact={legal.controllerContact}
        controllerAddress={legal.controllerAddress}
      />

      <h2>What is collected</h2>
      <p>
        Two things, and they arrive differently. <b>Your email address</b> comes from the sign-in service when
        you create an account, and is used to know whose garage is whose and to contact you about it.
        <b> Everything about your vehicles you put in yourself</b>: registrations, mileage, fill-ups, receipts,
        servicing, notes, and any photographs or documents you upload.
      </p>
      <p>
        Nothing is bought in, nothing is inferred about you, and there is no tracking of any kind on this site.
        No advertising, no analytics, and no third-party scripts. What is stored in your browser is listed on
        the <a href="/cookies">cookies page</a>, and none of it needs your permission.
      </p>

      <h2>Why it is held</h2>
      <p>
        To run the account you asked for, and to keep the service working and affordable to run. There is no
        other purpose, and your records are never sold, shared for marketing, or used to train anything.
      </p>

      <h2>Who else sees it</h2>
      <p>These are the only outside services involved, and each is here because it does a specific job:</p>
      <ul>
        <li>
          <b>Auth0</b> (Okta) handles signing in. It holds your email address and your password; this site
          never sees a password at all.
        </li>
        {processors.assistant && (
          <li>
            <b>Anthropic</b> runs the in-app assistant. When you use it, what you type and any photograph you
            attach are sent to be read and answered. If you never open the assistant, nothing goes there.
          </li>
        )}
        {processors.lookup && (
          <li>
            <b>DVLA and DVSA</b> answer registration look-ups. When you look up a plate while adding a car,
            that registration is sent to them to fetch the details back. Typing the details in by hand sends
            nothing.
          </li>
        )}
      </ul>
      {legal.hostingSummary !== null && (
        <>
          <h2>Where your data is held</h2>
          <p>{legal.hostingSummary}</p>
        </>
      )}

      <h2>How long it is kept</h2>
      <p>
        <b>Your vehicles and everything logged against them are kept until you delete your account.</b> Delete
        it and they go with it, in one step, along with your uploaded files.
      </p>
      <p>
        Some small internal counters are kept for a shorter time: a daily tally of assistant use, a daily tally
        of registration look-ups, and a record of anything an AI assistant wrote on your behalf. Those are held
        for about thirteen months and then removed automatically.
      </p>
      <p>
        <b>An account is never deleted for being unused.</b> If you stop signing in, your records stay as you
        left them until you ask for them to go.
      </p>

      <h2>What you can do</h2>
      <ul>
        <li>
          <b>Get a copy of everything.</b> Your account page has a download that gives you every row held about
          you, in one file. It can be read back in, here or on another copy of this software.
        </li>
        <li>
          <b>Correct anything.</b> Every figure on every screen can be edited or removed from the screen it
          appears on.
        </li>
        <li>
          <b>Delete your account.</b> Also on the account page. It removes your records and your sign-in, and
          it cannot be undone.
        </li>
        <li>
          <b>Object, or complain.</b> Write to {legal.controllerName} at the address above. You can also
          complain to the data protection regulator in {legal.jurisdiction}.
        </li>
      </ul>
    </LegalPage>
  )
}
