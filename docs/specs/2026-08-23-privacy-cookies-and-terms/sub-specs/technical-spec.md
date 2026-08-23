# Technical Specification

This is the technical specification for the spec detailed in
@docs/specs/2026-08-23-privacy-cookies-and-terms/spec.md

## 1. Public routes below the login wall

This is the only part of the spec that touches a security boundary, so it is first and it is the part to get
right.

**What the boundary currently is.** `main.tsx` renders `<AuthGate><RouterProvider /></AuthGate>`. `AuthGate`
returns `LandingPage` when there is no session and `children` when there is, so no route element is ever
constructed for a signed-out visitor. That is what stops a screen flashing another user's data before a
redirect settles, and `LandingPage`'s own doc comment records the cost: the landing page has no URL, and giving
it one means moving the gate inside the router.

**What it becomes.** `main.tsx` renders `<RouterProvider />` directly. The router grows two sibling layout
branches under the existing root:

```
/                       Root  (RouterLinks - the LinkProvider, unchanged, wraps BOTH branches)
├── (public layout)     LegalRoot   - no session required
│   ├── privacy
│   ├── cookies
│   └── terms
└── (gated layout)      <AuthGate><Outlet /></AuthGate>
    ├── index           GaragePage
    ├── gallery, account, admin
    └── :reg/*          every vehicle-scoped screen, unchanged
```

Both branches are pathless layout routes, so no URL changes and no existing path moves.

**The gate is now per-route, and that is a weakening unless something enforces it.** Above the router, a new
route was gated because everything was. As a layout route, a route added as a sibling of the gated branch
rather than as a child of it is public, silently, with nothing failing. So:

- `routes.tsx` carries a comment saying exactly that, at the sibling boundary.
- `routes.gating.test.ts` walks the exported router's route tree, flattens it to full paths, and asserts that
  every path except the three in an explicit `PUBLIC_PATHS` constant is a descendant of the gated layout.
  Adding a fourth public route is then a deliberate edit to a named list rather than an accident of nesting.
  This test is the reason the change is safe, and it is written before the routes move.

**Five consequences to handle rather than discover.**

1. `AuthGate` registers the access-token provider at `client.ts`'s single fetch seam and gates its children on
   `tokenReady`, so no query can fire without a bearer. As a layout route it still mounts before any gated
   screen renders, and the mechanism is unchanged. The legal pages sit outside it and must therefore make **no
   authenticated call** - they use `useMeta()`, which is anonymous and is the same cache entry `Footer`
   already fills.
2. `setAccessTokenProvider` is module-global. Navigating from `/privacy` into the app mounts the gate and sets
   it; navigating out unmounts and clears it. Nothing in the legal pages fetches, so the window where it is
   null is a window in which nothing asks.
3. `main.tsx`'s `onRedirectCallback` does `window.history.replaceState({}, title, window.location.pathname)`
   outside the router. It stays in `Auth0Provider`, which stays outermost, and is unaffected - but note that it
   preserves the path, so an Auth0 return to `/privacy` lands on the policy rather than the garage. That is
   correct and should be left alone.
4. **The landing page still has no URL, deliberately.** It is what the gate renders for a signed-out visitor at
   any gated path, exactly as now. `/privacy` renders the policy; `/` renders the landing page; Back from the
   policy returns to it.
5. **The legal pages render standalone, not in `AppShell`.** They use `Wrap` and the shared `Footer` and no
   nav, in both session states. This is not a shortcut: putting them in the shell needs a `CurrentScreen`
   value, and `hrefFor` (`link.tsx:27`) returns `/` for any screen whose `scoped` is false without reading the
   id - the trap the account screen documented and avoided the same way. `CurrentScreen` therefore gains
   nothing, `ScreenId` gains nothing, and neither nav table is touched.

## 2. The `Legal:` configuration section

`LegalOptions`, bound in `Program.cs` beside the existing options types:

| Key | Meaning | Blank means |
|---|---|---|
| `Legal:ControllerName` | The person or organisation responsible for the data. | **Not published.** |
| `Legal:ControllerContact` | An address a data subject can write to. | **Not published.** |
| `Legal:ControllerAddress` | Postal address. Optional. | Omitted from the pages. |
| `Legal:Jurisdiction` | Which law the terms are read under. | `United Kingdom`. |
| `Legal:HostingSummary` | One sentence naming where the deployment runs and where its backups go. | Omitted from the pages. |

**Publication requires both the name and the contact**, and blank means unpublished. That polarity is
deliberate and is written into `LegalOptions`' remarks, into `Program.cs`, into `deploy/.env.example` and into
the README - the discipline DEC-022 paid for, where a blank section's meaning reversed and the release notes
were the only place that said so. Here the fail-safe direction is *unpublished*: a document naming the wrong
controller is worse than no document, and the self-hoster is the majority case for a blank section.

**There is no `Legal:PolicyVersion` key.** The version and effective date are properties of the committed
prose, so they are a `const` beside it (`LEGAL_VERSION`, e.g. `2026-08-23`). A deployment cannot version text
it did not write, and a configurable version number over fixed text is a lie with a config key in front of it.

**The boot posture line names it.** `Program.cs` already logs a `Sign-up posture:` line so a shut door is a
stated fact rather than an inference; it gains a clause naming the controller or saying the documents are
unpublished, for the reason recorded there - a posture somebody believes is one thing and is provably another
is the fault that line exists to catch.

## 3. Processors disclosed from capability flags

The interesting property in this spec, and the founding premise applied to prose: **the list of third parties
each document discloses is derived from what this deployment is configured to do**, not written into the text.

| Processor | Disclosed when | Because |
|---|---|---|
| Auth0 / Okta | always | It is how you sign in; there is no deployment without it. |
| Anthropic | `meta.chatConfigured === true` | Only a deployment holding `Chat:ApiKey` sends a message or a photograph to a model API. |
| DVLA VES / DVSA MOT History | `meta.vehicleLookupConfigured === true` | Only a deployment holding `Lookup:VesApiKey` sends a registration to a government API, and the button is already hidden without it. |
| Hosting and backup | `Legal:HostingSummary` non-blank | Cannot be sourced from the app: `cambelt.app` runs on Asgard and backs up to Azure blob (DEC-020), and this repository cannot see either. One configured sentence, or nothing. |

A NAS install with no chat key therefore does not tell its household that their photographs go to Anthropic,
which would be false. Tested directly: render each document with each flag false and assert the processor is
absent, and with each true and assert it is named.

## 4. The documents themselves

**Committed as TSX components, not markdown fetched at runtime.** The CSP would permit a same-origin fetch, so
this is not a CSP argument. It is `MapFallbackToFile`: an unresolved path returns `index.html` with a **200**,
which is how `docs/images/` produced a broken image reporting success. A markdown file that failed to ship
would render the app under a legal URL and report success, on the one page whose purpose is being reliably
readable. A component that fails to ship fails the build.

Each document is one component under `src/legal/`, using the existing type scale and `Wrap`, with the
controller details interpolated from `meta.legal`. No new CSS class, so neither `tokens.test.ts`'s ALLOWED
band list nor `overflow.test.ts`'s wrap list is engaged.

**They carry a jargon guard, on `LandingPage.test.tsx`'s precedent.** That guard exists because the house voice
crept back into the one page written for car owners, and these three are the other pages written for them. The
rendered text of all three must match none of `MCP`, `derived`, `query filter`, `EF Core`, `entity`,
`endpoint`, `mirror`. A policy nobody can read is not a policy.

**What each document says, in outline:**

- `/privacy` - who the controller is; what is collected (an email address from the identity provider, and
  whatever the owner logs about their vehicles: registrations, mileage, receipts, photographs, documents);
  the lawful bases (contract for running the account, legitimate interests for keeping the service working and
  its ledgers); the processors above; the rights, each pointing at the endpoint that satisfies it -
  `GET /api/account/export` for Art. 15 and 20, `DELETE /api/account` for Art. 17, both already on the account
  screen; retention per section 6; and that data is stored in the region the hosting summary names.
- `/cookies` - **leads with "this site sets no cookies"**, because that is true and unusual. Then the storage
  table from section 5, then one paragraph on Auth0's own cookies on its own domain during the login redirect,
  which this deployment neither sets nor controls. It says there is no banner and why: nothing here is
  measurement, advertising or shared, so there is nothing to ask permission for.
- `/terms` - what the service is and is not, in particular that **every figure is computed from what the owner
  logged and the app is not a source of statutory truth**: an MOT expiry seeded from DVSA is superseded by the
  first logged pass, a reminder is a convenience and not a legal notice, and nothing here excuses missing an
  MOT or an insurance renewal. Then: acceptable use, the assistant's output being model-generated and
  reviewed by the owner before any write (which is what the draft card already enforces), no warranty for a
  free tier, account suspension and termination, the plan and what it bounds, and the governing law from
  `Legal:Jurisdiction`.

## 5. The storage registry

**One registry, imported by the code that writes the keys.** `src/lib/clientStorage.ts` exports an array of
`{ key, label, purpose, lifetime, essential }`, and `settings.ts`, `theme.ts`, `fuelUnit.ts` and `dismissed.ts`
take their key constants from it rather than declaring their own. The notice renders the same array. This is
the "four bugs, one cause" rule: a hand-typed list of what the app stores drifts from what it stores, exactly
as the hand-typed expense categories drifted from the seed.

The five entries, as they stand today:

| Key | Written by | Purpose | Essential |
|---|---|---|---|
| `@@auth0spajs@@::…` | `@auth0/auth0-react`, via `cacheLocation="localstorage"` in `main.tsx` | The session: the access and refresh tokens. | Yes |
| `cartracker.settings` | `lib/settings.ts` | The legacy API key (DEC-009), which grants no vehicle access. | Yes while it exists |
| `ct-theme` | `lib/theme.ts` and the pre-paint script | Light or dark, chosen by the visitor. | Preference |
| `ct-fuel-unit` | `lib/fuelUnit.ts` | MPG or L/100 km, chosen by the visitor. | Preference |
| `ct-attn-dismissed:<REG>` | `lib/dismissed.ts` | Which attention panel this browser has dismissed. | Preference |

**Two limits on the guard, both stated rather than papered over.**

- The Auth0 key is written by a library and its exact name is composed at runtime. It is registered by hand and
  the test cannot source it. The registry entry says so.
- `ct-theme` is also read by the pre-paint script in `plugins/theme-csp.ts`, which is outside `src/` and whose
  bytes are hashed into the CSP. The scan covers that file too, or the key it contains is a sixth site the
  guard cannot see.

`clientStorage.test.ts` greps `src/` and `plugins/` for `localStorage.getItem|setItem|removeItem` and fails on
any key literal or template not resolvable to a registry entry - the shape `coverage.test.ts` already uses to
fail the build on `plate={reg}`.

**The registry is also the trigger for reversing the no-banner decision.** Adding a key marked
`essential: false` and not a preference is the moment the consent question is re-asked, and the registry is
where that becomes visible in a diff.

## 6. Retention, and what it prunes

**Stated in the policy, in three parts:**

1. **Vehicle and account data: kept until the account is deleted.** Built already. `DELETE /api/account`
   deletes the data in one transaction, then the document folders, then the identity, and queues
   `pending_identity_deletions` on a failed identity call.
2. **Operational ledgers: pruned after `Retention:LedgerDays`, default 400.** `chat_usage`,
   `vehicle_lookup_usage` and `assistant_write_audits`. One number for all three, because a policy with three
   windows is three sentences that can disagree; 400 days rather than 365 so a year-on-year comparison still
   has a predecessor. `assistant_write_audits` rows whose `vehicle_id` was released to null by a vehicle
   deletion are pruned on the same rule as any other, since the rule is about age.
3. **Dormant accounts: not deleted.** Stated plainly, with the reason - see the spec's Out of Scope.

**`RetentionBackgroundService`**, on `RemindersBackgroundService`'s shape and beside
`IdentityDeletionRetryService`: a hosted service waking on `Retention:Interval` (24 h default), resolving a
scope per tick, and issuing one `ExecuteDeleteAsync` per table against `Clock.Today()` minus the window. It
logs the count removed per table at Information, because a prune that silently removes nothing and a prune that
silently removes everything look identical in a container log otherwise.

Three details:

- **None of the three tables carries a query filter** (only `Vehicle`, `Garage`, `WashLocation` and
  `ExpenseCategory` do), so a deployment-wide `ExecuteDelete` here is correct and needs no
  `IgnoreQueryFilters`, and must not reach for `BypassOwnership` - the request-wide hammer `AdminReadService`
  deliberately refused.
- **`Retention:LedgerDays=0` means never prune**, the third polarity `deploy/.env.example` already documents
  for the chat ceiling, and it is stated where it is set. It binds as `int?` for the reason
  `ChatSettings.DailyTokensPerOwner` and `Signup:Mode` both record: the compose file writes every key it knows,
  an unset one arrives as `""`, and `""` bound to a plain `int` throws at boot.
- **A deployment publishing no legal documents still prunes.** Retention is a property of the data, not of
  whether a policy page renders; tying them would mean a self-hoster's ledgers grew for ever.

## 7. Acceptance at sign-up

- `LandingPage` gains one line beside both sign-up CTAs: *By signing up you agree to the terms and the privacy
  policy*, with the two links. Rendered only when `meta.legal` is non-null, tested `!= null` so an in-flight
  `meta` renders nothing rather than a link to a page that will not exist - the hide-when-absent polarity of
  the three capability flags, not `signupInviteOnly`'s.
- `AccountProvisioner` stamps `user.TermsVersion = LegalVersion.Current` **on the creation path only**.
  This is the `TouchLastSeenAsync` lesson repeated: `BackfillEmailAsync` returns early once the address is
  present and verified, so anything folded into it is not written for the accounts that matter most. It is a
  separate assignment at creation and a test asserts a returning account's value is untouched.
- **Never backfilled.** Null means the account was created before these documents existed, or on a deployment
  that publishes none. Writing a version into those rows would assert that somebody accepted a document that
  did not exist when they signed up, which is the single thing the column exists to avoid.
- The value is a stored row, so it travels in `GET /api/account/export`'s `account` block and is read back by
  the importer's provenance panel like the rest of it.

## 8. Testing

Beyond the per-section tests named above:

- **Contract gate**: the diff is additive (`MetaResponse.Legal`, `termsVersion` on the export's account block),
  so the committed OpenAPI contract and the generated client move together in the same commit.
- **`meta.legal` shape**: declared as a nullable block. `MetaEndpoints.cs:114-120`'s lesson runs the other way
  here - a defaulted record parameter emits as nullable, and nullable is what is wanted, so `LegalInfo? Legal =
  null` is appended last beside the four existing defaulted flags rather than made required.
- **Controller is never hardcoded**: a test asserts the three components' source contains neither
  `usualexpat` nor an email address literal, so the self-hoster property cannot regress into a committed
  string.
- **Data tests against real Postgres** for the prune: a row one day outside the window is deleted, a row one
  day inside it is not, and a run with `LedgerDays` unset deletes nothing.
- **`AccountPage`'s existing panels are untouched.** The links belong in the footer and on the landing page,
  not as a sixth account panel.

## 9. Version and release

One minor bump (`VERSION`), folded into the feature commit, per the repository rule. `Directory.Build.props`
reads it, so `GET /api/meta`'s `version`, the export's `schemaVersion` and the image tag stay one fact.

## External Dependencies

None. No markdown renderer (the chat's `Prose` is for model output and these are components), no consent
library, no analytics, and no new package of any kind.
