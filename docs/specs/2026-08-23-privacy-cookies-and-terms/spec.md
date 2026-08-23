# Spec Requirements Document

> Spec: Privacy policy, cookie notice and terms - three public documents at real URLs
> Created: 2026-08-23
> Status: Planning

## Overview

Sign-up opened to strangers on 2026-08-22 (DEC-022) and this deployment publishes no privacy policy, no
statement of what it stores on a visitor's device, and no terms. The roadmap already records the gap in its
narrowest form - Art. 5(1)(c)/(e) are "the ones with no endpoint and no plan" - but the wider one is that a
controller of other people's data has to say, in public and before they sign up, what it collects, who it
sends it to and how long it keeps it.

This spec adds `/privacy`, `/cookies` and `/terms` as public routes below the login wall, with the controller's
identity supplied by configuration so a self-hosted deployment names its own operator rather than this one. It
ships **no consent banner**, because nothing non-essential is stored and a banner would be asking permission
for the session the app needs to work at all. It settles the retention question by stating a policy the code
then enforces, and by saying plainly which part of that policy is deliberately absent.

## User Stories

### A stranger deciding whether to hand over four years of receipts

As someone who has found `cambelt.app` and is weighing up signing up, I want to read what happens to what I
log before I create an account, so that I can decide on the facts rather than on trust.

Today the footer offers "The source is on GitHub - read exactly what it does with what you log". That is a
genuine answer and it is an answer for engineers; it asks a car owner to read a C# repository to find out
whether their photographs reach a third party. The three facts that actually decide it are all knowable and
none of them is written anywhere a visitor can reach: the identity provider holds their email address, the
assistant sends whatever they attach to a model API, and a registration typed into the add-car sheet goes to
DVLA.

### What is on my device, answered by a list rather than a banner

As a visitor, I want to know what this site stores in my browser, so that I can tell a site that needs a
session to work from one that is following me.

The honest answer is unusual and worth stating rather than hiding behind a banner: this app sets **no cookies
at all**. Auth0's token cache, the theme, the fuel unit and the dismissed-banner flags are `localStorage`
entries on this origin, and every one of them is either the session itself or a preference the visitor chose.
None of it is measurement, none of it is shared, and there is no third-party script on the page for the CSP to
allow even if there were. A consent banner over that set would be theatre. A list of the five is not.

### A self-hoster whose users are not mine

As someone running this on a NAS for their own household, I want the shipped policy to name me as the
controller or not to render at all, so that my install does not tell my family to email a stranger about their
data.

This is the boundary DEC-020 drew for the host, arriving in a second place: a document defining somebody's
obligations cannot be committed to a repository somebody else deploys. The mitigation is the polarity - **a
deployment that configures no controller publishes no documents and renders no links** - which is fail-safe
for the self-hoster and is one configuration key for the public deployment.

### A retention period that is true

As the operator, I want the policy's retention section to describe what the code actually does, so that the
one document whose entire value is being accurate is not the one that is wrong.

Two of the three answers are built already and need only stating: vehicle data lives until the account is
deleted, and `DELETE /api/account` deletes it in one transaction. The third is the operational ledgers -
`chat_usage`, `vehicle_lookup_usage` and `assistant_write_audits` - which accumulate for ever, are read by
nobody after a season, and have no expiry. Saying they are pruned means pruning them.

## Spec Scope

1. **Three public documents at real URLs** - privacy policy, cookie and storage notice, and terms of use, at
   `/privacy`, `/cookies` and `/terms`, rendered signed-out and signed-in, reached from the footer that already
   appears on the landing page and on every screen.
2. **Public routes below the login wall** - `AuthGate` moves from above `RouterProvider` to a layout route
   inside the router, so those three paths render without a session while every other route stays gated by
   construction, with a test that fails the build on a route added outside the gated branch.
3. **Configurable controller identity, and processors named from capability flags** - a `Legal:` section
   supplies the controller, contact and jurisdiction; the processors the documents disclose are read from the
   deployment's existing `chatConfigured` and `vehicleLookupConfigured` flags, so a deployment cannot claim a
   processor it does not use.
4. **A storage disclosure sourced from the code** - one registry of every client-side key, imported by the
   modules that write them and rendered by the notice, with a test that fails when a key appears in `src/`
   that the registry does not name.
5. **Acceptance evidenced, and a retention policy enforced** - the sign-up CTA carries the two links,
   `users.terms_version` records which document version was in force when an account was provisioned, and a
   `RetentionBackgroundService` prunes the three operational ledgers on the schedule the policy states.

## Out of Scope

- **A consent banner, consent categories, or a stored consent record.** Nothing non-essential is stored, so
  there is nothing to consent to. This is a decision with a trigger rather than a permanent one: the day
  anything measurement-shaped is added - analytics, a session recorder, an error reporter that fingerprints -
  it is re-decided, and the storage registry in scope item 4 is what will make that addition visible.
- **Automatic erasure of dormant accounts.** Deleting somebody's data has to be preceded by telling them, and
  the only notification channel that exists is the in-app badge (DEC-006), which a dormant account by
  definition does not see. `users.last_seen_at` is the column such a policy needs and it has existed since
  `0.26.0`, so the blocker is the channel, not the data. The policy states that there is no dormancy deletion
  rather than promising a period nobody enforces.
- **Anything that reads as a compliance programme rather than a document**: a record of processing activities,
  a data protection officer, processor agreements with Auth0/Anthropic/Microsoft, a breach procedure, a DPIA.
- **Auth0's own cookies on `usualexpat.uk.auth0.com`.** They are set on a different origin by a different
  controller during the login redirect. The notice names them and says whose they are; it cannot enumerate or
  control them.
- **Age gating, marketing consent, a mailing list, translations, and a cookie policy written for a NAS
  deployment's household.**
- **Rewriting the landing page.** It gains one line of links beside the sign-up CTA and nothing else.

## Expected Deliverable

1. `https://cambelt.app/privacy`, `/cookies` and `/terms` load for a signed-out visitor in both themes and on a
   phone, name the configured controller, and disclose exactly the processors this deployment holds credentials
   for. Every other path still shows the landing page to a signed-out visitor, and nothing renders a garage
   before Auth0 settles.
2. A deployment with no `Legal:` configuration renders no footer legal links, shows the landing page at those
   three paths, and is otherwise unchanged - checked by running the stack with the section blank.
3. Adding a `localStorage` key anywhere in `src/` without registering it fails the front-end suite; and after
   the retention service runs, a `chat_usage` row older than the stated window is gone while one inside it is
   not.

## Sub-specs

- Technical: @docs/specs/2026-08-23-privacy-cookies-and-terms/sub-specs/technical-spec.md
- Database schema: @docs/specs/2026-08-23-privacy-cookies-and-terms/sub-specs/database-schema.md
- API: @docs/specs/2026-08-23-privacy-cookies-and-terms/sub-specs/api-spec.md
