# Spec Tasks

These are the tasks to be completed for the spec detailed in
@docs/specs/2026-08-23-privacy-cookies-and-terms/spec.md

> **The order is the risk order, not the layer order.** Group 1 is the routing change, because it is the only
> part of this spec that touches a security boundary and because everything else in it is unreachable without a
> URL. It lands first, on its own, with its guard test written before the routes move - so that "no gated route
> escaped" is proved in a diff containing nothing else. Group 2 is the configuration and the polarity, which
> every document then reads. Group 3 is the documents. Group 4 is retention, which is independent of all three
> and could be done in either order. Group 5 is acceptance. Group 6 ships.
>
> **Two tasks are the ones to not skip when this gets long.** 1.1's route-gating test is the only thing
> standing between "the gate is above everything" and "the gate is above whatever somebody remembered to nest
> under it", and like `AdminReadServiceTests` it must be checked against a deliberately mis-nested route before
> it is kept. And 3.5's processor test is what stops a NAS install telling a household their photographs go to
> a company the deployment has no credential for.

## Tasks

- [ ] 1. **Public routes below the login wall** - move `AuthGate` into the router, and guard the boundary
  - [ ] 1.1 Write `src/CarTracker.WebApp/src/routes.gating.test.ts` first, against the router as it is today:
        walk the exported route tree, flatten it to full paths, and assert every path is a descendant of the
        gated layout except those in an explicit `PUBLIC_PATHS` list. **Check it fails** by temporarily nesting
        one real screen as a sibling of the gate, exactly as `AdminReadServiceTests` was checked against an
        un-widened query, then restore.
  - [ ] 1.2 Restructure `routes.tsx`: two pathless layout branches under `Root`, the gated one rendering
        `<AuthGate><Outlet /></AuthGate>` around everything that exists today, the public one holding
        `privacy`, `cookies` and `terms`. `RouterLinks` stays outermost so both branches get the
        `LinkProvider`. Carry the comment at the sibling boundary saying what nesting a route in the wrong
        branch does.
  - [ ] 1.3 Change `main.tsx` to render `<RouterProvider />` directly. `Auth0Provider`, `ThemeProvider`,
        `QueryClientProvider`, `ToastProvider` and `IconSprite` keep their order and their positions;
        `onRedirectCallback` is untouched.
  - [ ] 1.4 Update `AuthGate.tsx` to render `<Outlet />` in place of `children`, and its doc comment to
        describe a layout route rather than a wrapper. `AuthGate.test.tsx` needs a router around it; keep the
        existing three-state assertions intact.
  - [ ] 1.5 Add stub `LegalRoutes` rendering a placeholder, so group 1 is shippable and testable on its own.
  - [ ] 1.6 Verify: the gating test passes, `AuthGate.test.tsx` and `shell.test.tsx` pass, a signed-out visit
        to `/bt53akj/fuel` still shows the landing page, and a signed-in visit still shows the screen.

- [ ] 2. **The `Legal:` section, its polarity, and `meta.legal`**
  - [ ] 2.1 Write tests in `tests/CarTracker.Domain.Tests` (or beside the other options types) for
        `LegalOptions`: both name and contact present publishes; either blank does not; jurisdiction defaults
        to `United Kingdom`; whitespace-only is blank; and the address and hosting summary are independently
        optional.
  - [ ] 2.2 Add `LegalOptions` and bind it in `Program.cs`. Add the `Legal:` posture clause to the existing
        boot posture log line, naming the controller or stating that no documents are published.
  - [ ] 2.3 Add `LegalInfo? Legal = null` last on `MetaResponse` and populate it in `MetaEndpoints`. Extend the
        XML doc block, including the sentence saying that here a defaulted parameter emitting as nullable is
        the wanted behaviour rather than the trap `:114-120` records.
  - [ ] 2.4 Regenerate the OpenAPI contract and the typed client; confirm the diff is additive.
  - [ ] 2.5 Add the five keys to `deploy/.env.example` and `src/CarTracker.WebApp/.env.example` with the blank
        polarity stated where they are set, and to the README. **Check no em-dash reaches either file** - the
        `0.24.0` sweep filtered on `.cs/.ts/.tsx/.yml/.md` and missed both.
  - [ ] 2.6 Add the `Legal:` posture row to `/admin`'s configuration panel through the existing read service.
  - [ ] 2.7 Verify all tests pass.

- [ ] 3. **The three documents, and the storage registry they render**
  - [ ] 3.1 Write `clientStorage.test.ts` first: it greps `src/` and `plugins/` for
        `localStorage.getItem|setItem|removeItem` and fails on any key literal or template not resolvable to a
        registry entry. Check it fails by adding an unregistered key, then remove it.
  - [ ] 3.2 Add `src/lib/clientStorage.ts` with the five entries, and repoint `settings.ts`, `theme.ts`,
        `fuelUnit.ts` and `dismissed.ts` at it. Record on the Auth0 entry that its name is composed at runtime
        by a library and is registered by hand, and on the theme entry that `plugins/theme-csp.ts` is a second
        reader outside `src/`.
  - [ ] 3.3 Write the three page tests: each renders the configured controller and not a literal; each matches
        none of the jargon guard's terms; `/cookies` states that no cookies are set; and a test asserts the
        components' source contains neither `usualexpat` nor an email literal.
  - [ ] 3.4 Write the three components under `src/legal/` with `LEGAL_VERSION`, using `Wrap` and the shared
        `Footer`, no `AppShell`, no new CSS class, and the content outlined in the technical spec §4.
  - [ ] 3.5 Write the processor-disclosure tests: render each document with `chatConfigured` and
        `vehicleLookupConfigured` false and assert Anthropic and DVLA/DVSA are absent; true and assert they are
        named; `hostingSummary` blank and assert no hosting paragraph.
  - [ ] 3.6 Add the footer links, rendered only when `meta.legal != null` and tested `!= null` so an in-flight
        `meta` renders nothing. `Footer`'s meta line stays a sibling paragraph - the exact-text matches in
        `shell.test.tsx` and the screen tests are why.
  - [ ] 3.7 Make the three routes render a splash while `meta` is pending and redirect to `/` when `legal` is
        null.
  - [ ] 3.8 Verify all tests pass, including the existing `shell.test.tsx` and `GaragePage.test.tsx`
        exact-text assertions.

- [ ] 4. **Retention that makes the policy true**
  - [ ] 4.1 Write Data tests against real Postgres for the prune: a row one day outside the window is deleted;
        one day inside it is not; a run with `Retention:LedgerDays` unset or `0` deletes nothing; and all three
        tables are covered, seeding a row in each.
  - [ ] 4.2 Add `RetentionOptions` (`Interval`, `LedgerDays` as `int?` for the `""`-binding reason) and
        `RetentionBackgroundService` beside `RemindersBackgroundService`, resolving a scope per tick, issuing
        one `ExecuteDeleteAsync` per table against `Clock.Today()` minus the window, and logging the count
        removed per table.
  - [ ] 4.3 Add the two keys to `deploy/.env.example` with the `0` polarity stated, and to the README.
  - [ ] 4.4 Verify all tests pass, and that a deployment with `Legal:` blank still prunes.

- [ ] 5. **Acceptance at sign-up, and the version it records**
  - [ ] 5.1 Write Data tests: provisioning a new account stamps `TermsVersion` from the constant; a returning
        account's value is untouched by a sign-in, including after the constant has moved; and an account
        provisioned on a deployment publishing no documents is stamped null.
  - [ ] 5.2 Add `User.TermsVersion`, its configuration, and migration `AddTermsAcceptance`. Read the generated
        SQL: exactly one `AddColumn`, no `DeleteData`, `UpdateData` or `Sql`.
  - [ ] 5.3 Stamp it in `AccountProvisioner` **on the creation path only**, not inside `BackfillEmailAsync`.
  - [ ] 5.4 Add `termsVersion` to the export's `account` block and confirm the importer reads and discards it
        with the rest of that block. Update the round-trip test's normalisation.
  - [ ] 5.5 Add the acceptance line and its two links beside both `LandingPage` CTAs, gated on
        `meta.legal != null`, with a test for both states. Keep the jargon guard green.
  - [ ] 5.6 Verify all tests pass and the contract diff is still additive.

- [ ] 6. **Ship**
  - [ ] 6.1 Run the whole suite: Domain, Data, Chat and front-end. Record the four counts.
  - [ ] 6.2 Browser pass on a real database, signed out and signed in: the three pages in both themes and at
        phone width; a hard refresh directly onto `/privacy`; Back from a document to the landing page; an
        Auth0 round trip started from `/privacy`; and every legal link from the footer on a deep screen. The
        admin console's `0.27.1` correction is the reason this is a task and not an afterthought - the one
        defect no test held was found here.
  - [ ] 6.3 Run once with `Legal:` blank and confirm no links render, the three paths land on the landing page,
        and nothing else changed.
  - [ ] 6.4 `./scripts/release.ps1 -Minor`, `git add VERSION` **into the feature commit**.
  - [ ] 6.5 Update `docs/product/roadmap.md`: the Art. 5(1)(c)/(e) bullet in *What the law wants, written down
        once* is no longer "no endpoint and no plan" for the ledgers, and says what is still absent for
        accounts. Update `CLAUDE.md`'s state of play with the new counts and the routing change.
