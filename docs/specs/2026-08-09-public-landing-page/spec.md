# Spec Requirements Document

> Spec: Public Landing Page — what a signed-out visitor sees
> Created: 2026-08-09
> Status: Shipped 2026-08-09 in `f4a677b` (0.10.0), then **rewritten in 0.11.0** — the first cut was assembled
> from the README and mission and so was written for engineers, saying "MCP", "self-hosted", "derived" and
> "a class of bug the schema forecloses" to an audience of car owners. `LandingPage.test.tsx` now carries a
> jargon guard over those terms, which went red on all five and is how it earned its place.
>
> Tasks 6.4 and 6.5 remain open: both need a deployed build (Auth0's registration form actually being reached
> by `screen_hint`, and a 360px wrap check). Everything automatable is done.

## Overview

Replace the login wall's bare splash with a one-page welcome that says what Car Tracker is, shows it working,
and invites a visitor to sign in or sign up. It renders in place of the current signed-out branch of
`AuthGate`, above the router, so the security boundary is untouched.

**The calls to action already exist.** `AuthGate.tsx:50-57` already offers Log in and Sign up, the latter with
Auth0's `screen_hint: 'signup'`. What is missing is everything around them — the current signed-out screen is
a centred heading, one sentence, and two buttons, which asks a stranger to create an account in a product it
has not described.

This reverses a stated design constraint. `docs/design-brief.md:347` says *"Do not design a login, signup,
onboarding, or marketing page. Single user, self-hosted, already inside."* That was true when written and
stopped being true when Auth0 shipped (DEC-016); the brief gets a note rather than being quietly contradicted.

## User Stories

### Know what it is before being asked to join

As someone who has followed a link to Car Tracker, I want to understand what the app does before I decide
whether to create an account, so that "sign up" is a decision rather than a leap.

Today the page gives a stranger one sentence — "Sign in to reach your garage" — which presumes they already
know what the garage is and why they want one. The material to fix this is already written and does not need
inventing: the README's problem narrative (a spreadsheet whose Dashboard stores five provably wrong figures,
including a total that double-counts every fill) is the most convincing thing the project can say about
itself, because it is specific, checkable, and the reason the app exists.

### See it before believing it

As a visitor, I want to see the actual screens, so that I can judge whether this is a real, finished thing
rather than a landing page for a product that does not exist yet.

The repo already holds full-page captures of the dashboard, the fuel log, the service history and the garage.
They are the honest proof: every figure on them is computed live from BT53's real history, which is the claim
the whole product rests on.

### Sign up, not just sign in

As a newcomer with no account, I want the primary invitation to be registration, so that I am not staring at
a login form for credentials I do not have.

Both paths already work; they need to be presented as a choice, with the new-account path visible rather than
buried behind Auth0's own "Sign up" link on the login form.

## Spec Scope

1. **A `LandingPage` component** rendered by `AuthGate` when signed out, replacing the current `Splash`
   branch. The loading and authenticated branches are untouched.
2. **A hero in the app's own identity** — the dark full-bleed band, contour texture, Oswald display type and
   eyebrow caption the rest of the app uses, so the first screen a visitor sees is the product's face rather
   than a generic template.
3. **Copy assembled from what is already written** — the README's problem narrative, the mission's two
   differentiators (the assistant reads the live domain; derived-never-stored is enforced by the
   architecture), and a feature summary. Nothing invented, nothing claimed that is not built.

   > **Amended 2026-08-09, after the first cut shipped in 0.10.0.** Reusing the project's own prose turned out
   > to be the wrong instinct: it is written for engineers, and the page went live saying "MCP",
   > "self-hosted", "derived" and "a class of bug the schema forecloses". The audience is car owners. The copy
   > was rewritten from scratch in their language — the structure below is unchanged, only the words. The
   > spreadsheet story survives because it is concrete and checkable, told as an owner would tell it rather
   > than with the arithmetic. `LandingPage.test.tsx` now carries a **jargon guard** asserting the rendered
   > text matches none of `MCP`, `self-hosted`, `derived`, `schema`, `domain service` or `regression test`,
   > because the house voice will otherwise creep back the next time the file is edited.
   >
   > The same pass added an honest note that connecting an assistant currently takes a key and a config file.
   > "Ask an AI assistant about your car", unqualified, promises a non-technical owner something they cannot
   > reach until the in-app chat ships.
4. **Screenshots, downscaled and bundled** — copied into `src/assets/screens/`, converted to WebP under a
   size budget, and imported so Vite fingerprints them.
5. **Both calls to action, presented as a choice** — Sign up as the primary invitation, Log in beside it,
   both wired to the existing `loginWithRedirect` calls.

## Out of Scope

- **A routable `/about`, `/pricing` or any second page.** The landing page has no URL of its own, because
  `AuthGate` sits above `RouterProvider` and moving it would restructure the security boundary to gain a URL
  nobody needs to link to. Recorded as a known cost, not an oversight.
- **SEO and server rendering.** This is a client-rendered SPA behind a login wall; a marketing site that
  needs crawling is a different artefact.
- **Changing the auth flow.** No new scopes, no new Auth0 configuration, no change to redirect handling.
- **Pricing, terms, privacy copy.** Needed before a real public launch, and not this spec's subject.
- **The three public-release gates.** Per-user reference tables, HTTPS, and DEC-016's
  first-user-claims-unowned-vehicles are recorded on the roadmap by this work and fixed by none of it. The
  landing page is safe to ship without them; **opening sign-up to strangers is not.**

## Expected Deliverable

1. A signed-out visitor sees a page that names the product, explains the problem it solves, shows at least
   one real screen, and offers Sign up and Log in — with Sign up reaching Auth0's registration form, not its
   login form.
2. The page renders correctly in **both light and dark themes** and at **360px** without the page scrolling
   sideways, with an axe sweep passing in both themes.
3. A signed-in visitor sees no change whatever: the garage renders as it does today, and the loading state
   still reads "Checking your session…" before it.
