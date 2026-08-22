# Spec Tasks

These are the tasks to be completed for the spec detailed in @docs/specs/2026-08-22-admin-console/spec.md

> **The order is the risk order, not the layer order.** Group 1 is the gate, because everything after it is
> unreachable without one and because it is the part with no test project to catch a mistake. Group 2 is the
> plan resolver extraction, which touches code every request already runs: it lands before anything depends on
> it so that its regression - every existing `AccountEntitlementsTests` case still green with a null override -
> is proved on its own rather than inside a larger diff. Group 3 is the cross-owner read service, the one file
> allowed to widen the ownership filter, and it lands before any endpoint can call it. Groups 4 and 5 are the
> surfaces. Group 6 ships.
>
> **Two tasks are the ones to not skip when this gets long.** 3.1's isolation test is the only thing that
> proves `IgnoreQueryFilters()` is on every query, and it must be checked against a deliberately un-widened
> query before it is kept, exactly as `Export_never_writes_synchronously_to_its_destination` was checked
> against the old code. And 6.3's Auth0 dashboard steps are the half of this feature that lives outside the
> repository and cannot be tested from inside it.

## Tasks

- [ ] 1. **The gate** - two permissions, a domain predicate, two policies
  - [ ] 1.1 Write tests for `AdminAccess.Grants` in `tests/CarTracker.Domain.Tests/AdminAccessTests.cs`:
        holds the required permission; holds a different one only; holds several including it; holds none;
        a null sequence; an empty sequence; matching is ordinal and exact, so neither `admin` nor
        `admin:read:extra` satisfies `admin:read`; and explicitly that `admin:plan:write` does not grant
        `admin:read`.
  - [ ] 1.2 Add `src/CarTracker.Domain/Admin/AdminAccess.cs` with `PermissionClaim`, `ReadPermission`,
        `PlanWritePermission` and `Grants`.
  - [ ] 1.3 Add the `AdminRead` and `AdminPlanWrite` policies to the `AddAuthorization` block at
        `src/CarTracker.WebApi/Program.cs:224-237`, using `RequireAssertion` over `AdminAccess.Grants` with
        `ctx.User.FindAll(...)` - not `FindFirst`, which breaks on the second permission - and
        `.AddAuthenticationSchemes("Auth0")` so an assistant bearer is refused at the door with 401.
  - [ ] 1.4 Verify tests pass.

- [ ] 2. **The plan override, and extracting the ladder two callers now need**
  - [ ] 2.1 Write tests for `PlanResolver.Resolve` in `tests/CarTracker.Domain.Tests/PlanResolverTests.cs`:
        an override of `Pro` beats an empty comp list; an override of `Pro` beats a non-matching list; an
        override of `Free` beats a comp match; a null override reproduces `NobodyIsComped`, `AddressUnknown`
        (the `Email == ExternalId` sentinel), `AddressNotVerified`, `Comped` and `NotOnCompList` in the
        documented order.
  - [ ] 2.2 Add `AccountPlan?  PlanOverride` and `DateTimeOffset? LastSeenAt` to `src/CarTracker.Data/User.cs`
        and `Configuration/UserConfiguration.cs` (`integer` and `timestamptz`, both nullable, no index, no
        check constraint), then generate migration `AddAdminObservability` and read it: exactly two
        `AddColumn` calls, no `DeleteData`, `UpdateData` or `Sql`, and a `Down` that drops both.
  - [ ] 2.3 Add `PlanReason.AdminGranted` to `src/CarTracker.Domain/Accounts/AccountPlan.cs` with an XML doc
        saying what the reader should do about it, which is nothing.
  - [ ] 2.4 Extract `src/CarTracker.Domain/Accounts/PlanResolver.cs` and rewrite
        `AccountEntitlements.ResolveUncachedAsync` (`AccountEntitlements.cs:83-124`) to load the user then
        call it, keeping the caller-shaped cases (no owner, no row) where they are. **Amend, do not delete,
        the comment at `:85-87`**: its reasoning about `NobodyIsComped` is still true, it is simply no longer
        the first question. Record in a comment that the empty-comp-list short circuit is gone, because an
        override might exist and only the row knows.
  - [ ] 2.5 Write tests in `tests/CarTracker.Data.Tests/AdminPlanOverrideTests.cs` against real PostgreSQL:
        setting an override changes what `AccountEntitlements` resolves for that account on a fresh context
        and changes nothing for a second owner; clearing it restores the comp-list answer. Confirm every
        existing case in `AccountEntitlementsTests` is still green unchanged - that is the regression test for
        the extraction.
  - [ ] 2.6 Verify tests pass.

- [ ] 3. **Reading across owners** - the one file allowed to widen the filter
  - [ ] 3.1 Write tests for `AdminReadService` in `tests/CarTracker.Data.Tests/AdminReadServiceTests.cs`,
        modelled on `AccountDeletionTests.cs`: **two seeded owners**, vehicles created through
        `VehicleFactory.CreateAsync`, contexts built with `TestOwner.As(ownerA)` so the accessor is pinned the
        way the request pipeline pins it. Assert the service returns owner B's vehicle count, masked plates,
        document count, chat tokens and open anomaly count. **Check each assertion fails against a query with
        `IgnoreQueryFilters()` removed before keeping it** - a test that passes either way proves nothing, and
        `TestOwner.As`'s doc comment warns about the mirror image of this hazard.
  - [ ] 3.2 Write tests for `RegistrationMask.Mask` in
        `tests/CarTracker.Domain.Tests/RegistrationMaskTests.cs`: `BT53 AKJ` to `BT** **J`; the import suffix
        `BT53 AKJ-2` to `BT** ***-2`; a plate with no space; one, two and three characters all fully masked;
        null and whitespace to empty; and the load-bearing negative, that for any input of four characters or
        more the output differs from the input.
  - [ ] 3.3 Add `src/CarTracker.Domain/Admin/RegistrationMask.cs`.
  - [ ] 3.4 Add `src/CarTracker.Domain/Admin/AdminReadService.cs` and register it scoped in
        `src/CarTracker.Domain/ServiceCollectionExtensions.cs` beside `AccountEntitlements`. Grouped queries
        joined in memory on `UserId`, never a loop issuing a query per account. A class-level comment stating
        that this is the only file permitted to call `IgnoreQueryFilters()` for the admin surface, and why not
        `BypassOwnership`.
  - [ ] 3.5 Verify tests pass.

- [ ] 4. **The endpoints**
  - [ ] 4.1 Write tests for the pieces that can be tested without a WebApi test project: the diagnostics
        projection (given options objects, it reports the right booleans and counts and no secret), and the
        `limit`/`days` clamps as pure helpers. Note in the spec's own terms that the routes themselves are
        proved by the browser pass in 6.4, because there is no `CarTracker.WebApi.Tests`.
  - [ ] 4.2 Add `src/CarTracker.WebApi/Endpoints/AdminEndpoints.cs`: one `MapGroup("/api/admin")` with
        `.RequireAuthorization("AdminRead")` - the first explicit `RequireAuthorization` in the codebase, so
        say so in a comment - carrying `GET /users`, `GET /users/{id}`, `GET /usage` and `GET /diagnostics`,
        plus `PUT` and `DELETE /users/{id}/plan` with `.RequireAuthorization("AdminPlanWrite")` on top.
        Register it in the `Program.cs:392-413` block.
  - [ ] 4.3 Add `AdminCapabilities` to `AuthenticatedResponse` in `MetaEndpoints.cs`, **required and
        non-nullable** per the remarks already on that record at `:114-120`, populated from
        `AdminAccess.Grants` over the request's own principal so the client and the policy cannot disagree.
  - [ ] 4.4 Log the plan write at Information with the acting `sub`, the target user id and the override
        before and after. Add the code comment recording that a second administrator is the point at which a
        log line stops being an audit trail.
  - [ ] 4.5 Regenerate `api-contract/v1.json` and the TypeScript
        (`dotnet build && cd src/CarTracker.WebApp && npm run gen:api`) and confirm the diff is additive: six
        paths, the new schemas, `admin` on `AuthenticatedResponse`, `AdminGranted` on `PlanReason`, and
        nothing removed, renamed or retyped.
  - [ ] 4.6 Verify tests pass.

- [ ] 5. **The screen**
  - [ ] 5.1 Write tests: `src/screens/AdminPage.test.tsx` (renders the account list; plates in the DOM are
        masked and no full plate appears; the deployment figure renders; `axe(container)` is clean; statuses
        pass `expectStateIsReadable`) and an extension to `src/shell/shell.test.tsx` (the Admin link is absent
        without `canReadAdmin`, present with it, absent while the access response is in flight). Stub
        `globalThis.fetch` per `AccountPage.test.tsx:50-81` and **match `/api/meta/authenticated` before
        `/api/meta`**, the ordering trap its comment records.
  - [ ] 5.2 Add `useAdminAccess()` to `src/api/queries.ts`, reading the `queryKeys.access` cache entry
        `AuthGate` already fills, and `src/api/admin.ts` with the fetchers and `adminKeys`.
  - [ ] 5.3 Wire the shell: `/admin` as a sibling of `:reg` in `routes.tsx` beside `/account`; `CurrentScreen`
        gains `'admin'` in `nav.ts` and **`ScreenId` does not**; `ShellScope` gains `{kind:'admin'}` in
        `scope.ts` (its three consumers test `!== 'vehicle'`, so none need editing); `ADMIN_PATH` beside
        `ACCOUNT_PATH` in `UserMenu.tsx:6`, rendered above Account and gated on `canReadAdmin === true`.
  - [ ] 5.4 Build `AdminPage.tsx` and `screens/admin/{DeploymentPanel,AccountsPanel,PosturePanel,AccountSheet}.tsx`.
        `PageHead` with an eyebrow and **no `plate`**; `<DataTable>` driven by `useTableView` with search over
        address and masked plate, Free/Pro and verified chips, and sorts by signed-up, last seen, tokens and
        vehicles; `DerivedRow` for the posture list; the existing `TimeChart` for the thirty-day series.
  - [ ] 5.5 Add a sentence for `AdminGranted` to `REASON` in `screens/account/PlanPanel.tsx:22` - the build
        will already be failing without it, which is that `Record<PlanReason, string>` doing its job.
  - [ ] 5.6 Satisfy the guards: add the four `screens/admin/*` panels to `EXEMPT` in `test/coverage.test.ts`
        with the wording the account panels use at `:46-49`; no raw hex (`tokens.test.ts`); any new control
        row declares `flex-wrap: wrap` and `min-width: 0` and is added to `overflow.test.ts`.
  - [ ] 5.7 Verify tests pass.

- [ ] 6. **Ship it**
  - [ ] 6.1 Bump `VERSION` (minor, 0.25.0 to 0.26.0) in the feature commit.
  - [ ] 6.2 Write DEC-023 in `docs/product/decisions.md`, `Amends:` DEC-022, following the file's entry format.
        It must answer DEC-022 head-on on both halves: why an operator permission is not the entitlement that
        decision refused, and that the "nothing here can assert tenant state" half is **conceded, not
        answered**. Record the plan-override reversal of DEC-022's "no admin UI, and no per-account override",
        and the residual risk of a tenant misconfiguration this repository cannot see.
  - [ ] 6.3 Document the Auth0 dashboard steps in the README Configuration section: enable **RBAC** *and*
        **Add Permissions in the Access Token** on `cartracker.api`; define `admin:read` and
        `admin:plan:write`; assign through a role rather than to the user; **sign out and back in**, because a
        token issued before the assignment does not carry it and rotation will not add it. State that no new
        configuration key exists.
  - [ ] 6.4 Browser pass against a real database with two accounts: the link is absent without the
        permission and every route 403s; with it, the list renders with masked plates and the deployment
        figure is right; put the second account on Pro and confirm its own plan panel says so on its next
        request with no restart; clear the override and confirm it falls back.
  - [ ] 6.5 Update `docs/product/roadmap.md` (dateline plus a shipped entry) and the CLAUDE.md state-of-play
        entry with the new test counts.
  - [ ] 6.6 Full suite: `dotnet test`, `npx tsc -b`, `npm test`, `npm run build`.
