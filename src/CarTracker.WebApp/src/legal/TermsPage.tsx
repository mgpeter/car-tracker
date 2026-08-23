import { ControllerLine, LegalLoading, LegalPage } from './LegalPage'
import { useLegal, useProcessors } from './useLegal'

/**
 * What this service promises and what it does not.
 *
 * **The load-bearing clause is that it is not a source of statutory truth.** Every figure here is worked out
 * from what the owner logged, an MOT date fetched from DVSA is superseded by the first pass they record, and a
 * reminder is a convenience rather than a legal notice. That is the honest description of what this app does,
 * and stating it is what stops somebody treating a green dashboard as proof their car is legal.
 */
export function TermsPage() {
  const legal = useLegal()
  const processors = useProcessors()

  if (legal === null) return <LegalLoading title="Terms" />

  return (
    <LegalPage title="Terms" current="terms" version={legal.version}>
      <ControllerLine
        controllerName={legal.controllerName}
        controllerContact={legal.controllerContact}
        controllerAddress={legal.controllerAddress}
      />
      <p>Using this site means accepting what is on this page.</p>

      <h2>What this is</h2>
      <p>
        A place to keep the records for your vehicles: what you spend, what you have had done, what is due
        next. You keep the records; it does the arithmetic and shows you what it works out.
      </p>

      <h2>What it is not</h2>
      <p>
        <b>It is not a legal record and it is not proof of anything.</b> Every figure on every screen is worked
        out from what has been entered, so a date that was never logged is a date it does not know about. An
        MOT or tax date shown here may be out of date, superseded, or simply wrong because of a typo.
      </p>
      <p>
        <b>A reminder is a convenience, not a notice.</b> Nothing here relieves you of keeping your vehicle
        taxed, insured, MOT'd and roadworthy, and no reminder appearing or failing to appear changes that. Do
        not rely on this site as your only warning for anything that matters.
      </p>
      <p>
        <b>It is not advice about your vehicle.</b> Guidance shown on checks and issues is general and comes
        from the records you keep; it is not an inspection and it is no substitute for a mechanic.
      </p>
      {processors.assistant && (
        <p>
          <b>The assistant can be wrong.</b> Its answers are produced by a language model reading your records.
          It never writes anything to your logs on its own: everything it proposes is shown to you, filled in,
          for you to check and confirm before it is saved. Check the figures, particularly the ones read off a
          photograph.
        </p>
      )}
      {processors.lookup && (
        <p>
          <b>Look-up results come from DVLA and DVSA</b> and are only as current as those services are. They
          fill in a form for you to check; they do not confirm that anything is taxed or has passed a test.
        </p>
      )}

      <h2>Your account</h2>
      <ul>
        <li>Keep your sign-in to yourself. What happens under your account is your responsibility.</li>
        <li>Put in your own records, and nothing unlawful. Do not upload anything you have no right to hold.</li>
        <li>
          Do not try to reach anyone else's data, disrupt the service, or put load on it beyond ordinary use.
        </li>
        <li>You can delete your account at any time from the account page. It takes your records with it.</li>
      </ul>

      <h2>Availability, and ending it</h2>
      <p>
        The service is offered as it is, with no promise that it will be available, uninterrupted, or free of
        faults, and no warranty beyond what the law requires. Features may change or be withdrawn. Keep your
        own copy of anything you would miss: the account page will hand you every record it holds, in one file,
        whenever you ask.
      </p>
      <p>
        An account may be suspended or closed if it is used against these terms, or in a way that puts the
        service or anyone else's data at risk. Where it is reasonable to do so, you will be told first and
        given the chance to take a copy of your records.
      </p>
      <p>
        Nothing here limits liability for death or personal injury caused by negligence, for fraud, or for
        anything else that cannot lawfully be limited.
      </p>

      <h2>Your data</h2>
      <p>
        What is collected and how long it is kept is on the <a href="/privacy">privacy page</a>. What is stored
        in your browser is on the <a href="/cookies">cookies page</a>.
      </p>

      <h2>Governing law</h2>
      <p>These terms are governed by the law of {legal.jurisdiction}.</p>
    </LegalPage>
  )
}
