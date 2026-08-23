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

- [x] 1. **Public routes below the login wall** - move `AuthGate` into the router, and guard the boundary
  - [x] 1.1 Write `src/CarTracker.WebApp/src/routes.gating.test.tsx` first: walk the exported route tree,
        flatten it to full paths, and assert every path is a descendant of the gated layout except those in an
        explicit `PUBLIC_PATHS` list. **Checked by sabotage** - `dashboard` re-nested as a sibling of the gate,
        the test went red naming `/sabotage-dashboard`, then restored.
  - [x] 1.2 Restructure `routes.tsx`: two pathless layout branches under `Root`, the gated one rendering
        `<AuthGate><Outlet /></AuthGate>` around everything that exists today, the public one holding
        `privacy`, `cookies` and `terms`. `RouterLinks` stays outermost so both branches get the
        `LinkProvider`. Comment carried at the sibling boundary.
  - [x] 1.3 Change `main.tsx` to render `<RouterProvider />` directly, with a comment saying where the gate
        went. Every provider keeps its order and position; `onRedirectCallback` untouched.
  - [x] 1.4 **Changed from the plan: `AuthGate` keeps its `children` prop** and the route element is
        `<AuthGate><Outlet /></AuthGate>`. Rendering `<Outlet />` inside the component would have forced a
        router into all nine of its existing tests to prove behaviour that has nothing to do with routing.
        Only the doc comment changed, and it now records that the guarantee is positional.
  - [x] 1.5 Add `src/legal/` with `LegalPage` plus three placeholder documents, and `legal.test.tsx` -
        **`coverage.test.ts` required the last part**, since every exported component must be swept by axe or
        exempted with a reason. An exemption would have been the wrong answer for three public pages.
  - [x] 1.6 Verify: `tsc -b` clean, `vite build` clean, and the whole front-end suite green at **665, up from
        653** - the twelve new are four gating and eight legal.

- [x] 2. **The `Legal:` section, its polarity, and `meta.legal`**
  - [x] 2.1 Write tests in `tests/CarTracker.Domain.Tests` (or beside the other options types) for
        `LegalOptions`: both name and contact present publishes; either blank does not; jurisdiction defaults
        to `United Kingdom`; whitespace-only is blank; and the address and hosting summary are independently
        optional.
  - [x] 2.2 Add `LegalOptions` and bind it in `Program.cs`. Add the `Legal:` posture clause to the existing
        boot posture log line, naming the controller or stating that no documents are published.
  - [x] 2.3 Add `LegalInfo? Legal = null` last on `MetaResponse` and populate it in `MetaEndpoints`. Extend the
        XML doc block, including the sentence saying that here a defaulted parameter emitting as nullable is
        the wanted behaviour rather than the trap `:114-120` records.
  - [x] 2.4 Regenerate the OpenAPI contract and the typed client; confirm the diff is additive.
  - [x] 2.5 Five keys added to `deploy/.env.example`, `deploy/docker-compose.yml` and the README, with the
        blank polarity stated at each. Em-dash checked. **`src/CarTracker.WebApp/.env.example` was wrong in the
        plan and got nothing**: it carries only `VITE_*` build-time SPA config, and `Legal:` is server-side and
        reaches the client on `meta`.
  - [x] 2.6 Add the `Legal:` posture row to `/admin`'s configuration panel through the existing read service.
  - [x] 2.7 Verify all tests pass.

- [x] 3. **The three documents, and the storage registry they render**
  - [x] 3.1 Write `clientStorage.test.ts` first: it greps `src/` and `plugins/` for
        `localStorage.getItem|setItem|removeItem` and fails on any key literal or template not resolvable to a
        registry entry. Check it fails by adding an unregistered key, then remove it.
  - [x] 3.2 Add `src/lib/clientStorage.ts` with the five entries, and repoint `settings.ts`, `theme.ts`,
        `fuelUnit.ts` and `dismissed.ts` at it. Record on the Auth0 entry that its name is composed at runtime
        by a library and is registered by hand, and on the theme entry that `plugins/theme-csp.ts` is a second
        reader outside `src/`.
  - [x] 3.3 Write the three page tests: each renders the configured controller and not a literal; each matches
        none of the jargon guard's terms; `/cookies` states that no cookies are set; and a test asserts the
        components' source contains neither `usualexpat` nor an email literal.
  - [x] 3.4 Write the three components under `src/legal/` with `LEGAL_VERSION`, using `Wrap` and the shared
        `Footer`, no `AppShell`, no new CSS class, and the content outlined in the technical spec §4.
  - [x] 3.5 Write the processor-disclosure tests: render each document with `chatConfigured` and
        `vehicleLookupConfigured` false and assert Anthropic and DVLA/DVSA are absent; true and assert they are
        named; `hostingSummary` blank and assert no hosting paragraph.
  - [x] 3.6 Add the footer links, rendered only when `meta.legal != null` and tested `!= null` so an in-flight
        `meta` renders nothing. `Footer`'s meta line stays a sibling paragraph - the exact-text matches in
        `shell.test.tsx` and the screen tests are why.
  - [x] 3.7 Make the three routes render a splash while `meta` is pending and redirect to `/` when `legal` is
        null.
  - [x] 3.8 Verify all tests pass, including the existing `shell.test.tsx` and `GaragePage.test.tsx`
        exact-text assertions.

- [x] 4. **Retention that makes the policy true**
  - [x] 4.1 Write Data tests against real Postgres for the prune: a row one day outside the window is deleted;
        one day inside it is not; a run with `Retention:LedgerDays` unset or `0` deletes nothing; and all three
        tables are covered, seeding a row in each.
  - [x] 4.2 Add `RetentionOptions` (`Interval`, `LedgerDays` as `int?` for the `""`-binding reason) and
        `RetentionBackgroundService` beside `RemindersBackgroundService`, resolving a scope per tick, issuing
        one `ExecuteDeleteAsync` per table against `Clock.Today()` minus the window, and logging the count
        removed per table.
  - [x] 4.3 Two keys added to `deploy/.env.example`, `deploy/docker-compose.yml` and the README with the `0`
        polarity stated. **Caught late**: the README rows went in with group 2 and the two deploy files were
        missed until the task list was being closed out, which is the `deploy/.env.example` gap DEC-022 already
        records once.
  - [x] 4.4 Verify all tests pass, and that a deployment with `Legal:` blank still prunes.

- [x] 5. **Acceptance at sign-up, and the version it records**
  - [x] 5.1 Write Data tests: provisioning a new account stamps `TermsVersion` from the constant; a returning
        account's value is untouched by a sign-in, including after the constant has moved; and an account
        provisioned on a deployment publishing no documents is stamped null.
  - [x] 5.2 Add `User.TermsVersion`, its configuration, and migration `AddTermsAcceptance`. Read the generated
        SQL: exactly one `AddColumn`, no `DeleteData`, `UpdateData` or `Sql`.
  - [x] 5.3 Stamp it in `AccountProvisioner` **on the creation path only**, not inside `BackfillEmailAsync`.
  - [x] 5.4 Add `termsVersion` to the export's `account` block and confirm the importer reads and discards it
        with the rest of that block. Update the round-trip test's normalisation.
  - [x] 5.5 Add the acceptance line and its two links beside both `LandingPage` CTAs, gated on
        `meta.legal != null`, with a test for both states. Keep the jargon guard green.
  - [x] 5.6 Verify all tests pass and the contract diff is still additive.

- [~] 6. **Ship**
  - [x] 6.1 Whole suite green: **409 Domain, 346 Data, 61 Chat (one deliberate skip), 698 front-end**, plus
        `tsc -b` and `vite build` clean.
  - [ ] 6.2 Browser pass on a real database, signed out and signed in: the three pages in both themes and at
        phone width; a hard refresh directly onto `/privacy`; Back from a document to the landing page; an
        Auth0 round trip started from `/privacy`; and every legal link from the footer on a deep screen. The
        admin console's `0.27.1` correction is the reason this is a task and not an afterthought - the one
        defect no test held was found here.
  - [ ] 6.3 Run once with `Legal:` blank and confirm no links render, the three paths land on the landing page,
        and nothing else changed.
  - [x] 6.4 `./scripts/release.ps1 -Minor` run: `VERSION` is **0.28.0**. Still to be staged into the feature
        commit, which has not been made - nothing here is committed.
  - [x] 6.5 Updated `docs/product/roadmap.md`:: the Art. 5(1)(c)/(e) bullet is answered in three parts, and there is a
        *Shipped since the phases above* entry. `CLAUDE.md`'s state of play carries the routing change, the
        registry, the polarity and the BST defect. DEC-024 was written before group 2.


## Still outstanding

- **6.2 the browser pass, and 6.3 the blank-`Legal:` run.** Both need the stack up against a real database and
  a real Auth0 tenant, and both are where the admin console's one shipped defect was found (`0.27.1`) - a write
  that never left the browser still looks like a success on screen. Not done.
- **Nothing is committed.** The working tree carries the whole feature, `VERSION` at 0.28.0, and DEC-024.
