# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-08-22-admin-console/spec.md

## The decision this argues with, and why it is not the same decision

DEC-022 rejected an Auth0 `permissions` claim. The rejection is in `docs/product/decisions.md:1811-1824` and
repeated as a doc comment on the class it governs, `AccountEntitlements.cs:37-41`: *"a column a Stripe webhook
flips, or a `permissions` claim in the access token, and both are the same mistake in different clothes: a
copy of a fact owned somewhere else, free to go stale in both directions."*

That objection is about **entitlement**. It does not reach **authorisation of an operator surface**, for three
reasons that DEC-023 records:

1. **The fact is owned by the tenant.** A plan is owned by this application and its billing relationship, so a
   copy of it in a token is a copy. Who administers a deployment is a property of the identity tenant, and a
   claim asserting it is the original rather than a copy.
2. **Staleness in both directions is survivable here.** A cancelled subscriber keeping access and a new
   subscriber locked out are both money and both urgent. A revoked administrator keeping read access until
   their token rotates is neither, and it is recoverable by rotating the token.
3. **The seam is already named.** `Program.cs:220` says of the existing MCP policies: *"The MCP policies check
   the scope claim, not the scheme - the seam the Auth0/JWT scheme could also drop into (DEC-014): give a JWT
   the same scope claims and the tools would not change."* This is the JWT dropping into that seam.

The second half of DEC-022's objection - *"Auth0 roles are tenant state: nothing in this repository can assert
them, test them or restore them, while there is no `CarTracker.WebApi.Tests` project"* - is **conceded, not
answered**. Two mitigations, both partial and both stated as such: the predicate is a domain type with real
unit tests, and the surface it opens is read-only apart from one write and never renders a full plate. The
residual risk is a tenant misconfiguration - most plausibly a role default-assigned to new users through an
Auth0 Action - which this repository cannot see, cannot warn about and cannot test. DEC-023 says so.

## Part 1 - The gate

### The predicate lives in the domain

`src/CarTracker.Domain/Admin/AdminAccess.cs`. A pure static, for the reason `AccountProvisioner.cs:56-59`
gives about itself: there is no `CarTracker.WebApi.Tests`, so anything worth being sure about goes somewhere a
test can reach it. `SignupPolicy` and `EmailAllowlist` are the precedents.

```csharp
public static class AdminAccess
{
    /// The claim Auth0 adds when RBAC and "Add Permissions in the Access Token" are enabled on the API.
    public const string PermissionClaim = "permissions";

    public const string ReadPermission = "admin:read";
    public const string PlanWritePermission = "admin:plan:write";

    public static bool Grants(IEnumerable<string>? held, string required);
}
```

`Grants` is ordinal, trims nothing and matches exactly. A null or empty sequence is false. There is no
wildcard, no prefix match and no implication between the two permissions: `admin:plan:write` does not confer
`admin:read`, and the endpoints require both for the write precisely so that the two can be assigned
separately later.

### Two policies, beside the three that exist

`src/CarTracker.WebApi/Program.cs:224-237`, in the `AddAuthorization` block:

```csharp
options.AddPolicy("AdminRead", policy => policy
    .AddAuthenticationSchemes("Auth0")
    .RequireAssertion(ctx => AdminAccess.Grants(
        ctx.User.FindAll(AdminAccess.PermissionClaim).Select(c => c.Value),
        AdminAccess.ReadPermission)));

options.AddPolicy("AdminPlanWrite", policy => policy
    .AddAuthenticationSchemes("Auth0")
    .RequireAssertion(ctx => AdminAccess.Grants(
        ctx.User.FindAll(AdminAccess.PermissionClaim).Select(c => c.Value),
        AdminAccess.PlanWritePermission)));
```

Four things about that block are worth writing down, because each is a way to get it subtly wrong:

- **`FindAll`, not `FindFirst`.** A JSON array claim arrives from the JWT handler as *repeated claims of the
  same name*, not as one comma-joined value. `FindFirst` would work perfectly while the operator holds one
  permission and start refusing the moment a second is assigned - a bug that appears on the day you expand,
  which is the day the user has already said is coming.
- **`RequireAssertion` over a domain call, not `RequireClaim`.** `RequireClaim(PermissionClaim, ReadPermission)`
  would behave identically today and is shorter. It is refused because it puts the predicate in `Program.cs`
  where nothing can test it, and the whole reason `AdminAccess` exists is to be testable.
- **`MapInboundClaims = false` is already set** (`Program.cs:197-216`), so `permissions` arrives unmapped
  under its own name. If that ever changes, this policy breaks silently by refusing everybody, which is the
  safe direction.
- **`.AddAuthenticationSchemes("Auth0")` is required**, because the default would include the `ApiKey` and
  `AssistantToken` schemes. Naming the scheme means an assistant bearer presented at an admin route fails at
  the door with 401 rather than reaching a policy that would tell it 403 - the behaviour `AccountEndpoints.cs:9-27`
  documents for the account routes, and for the same reason.

### The endpoint group

`.RequireAuthorization("AdminRead")` on the `MapGroup` in `AdminEndpoints.cs` is **the first explicit
`RequireAuthorization` in the codebase** - every other group rides the global fallback policy, and the only
existing overrides are `.AllowAnonymous()`. Called out here so a later reader does not read it as an accident
or as a pattern to copy onto groups that do not need it.

The two plan routes additionally carry `.RequireAuthorization("AdminPlanWrite")`. **Group and route policies
are combined with AND**, so the write requires both permissions. That is deliberate rather than incidental: an
account that may change a plan may certainly see the account it is changing, and requiring both means
`admin:plan:write` alone is not a back door into an unlisted account id.

### The SPA needs no change

`main.tsx:28` sends only `redirect_uri` and `audience`. With RBAC and *Add Permissions in the Access Token*
enabled on the `cartracker.api` API, Auth0 adds the `permissions` claim for that audience without a requested
scope. No `scope` parameter, no `Auth0Provider` change, and the CSP is untouched.

## Part 2 - Reading across owners

### One file, one widening

`CarTrackerDbContext.cs:102-112` applies `BypassOwnership || X.OwnerId == CurrentOwnerId` to `Vehicle`,
`Garage`, `WashLocation` and `ExpenseCategory`. An administrator's request is provisioned exactly like anyone
else's, so `ICurrentUserAccessor` is pinned to *their* owner id and those filters are live. Every cross-owner
query therefore needs `IgnoreQueryFilters()`. The precedent is `AccountProvisioner.cs:264`.

`src/CarTracker.Domain/Admin/AdminReadService.cs` is the only file permitted to do this for the admin surface.
Scoped, registered beside `AccountEntitlements` in `src/CarTracker.Domain/ServiceCollectionExtensions.cs:39`,
and injected only by `AdminEndpoints`.

**Deliberately not `BypassOwnership`.** Setting it would widen the whole request, including code with no idea
it is running under an administrator, and it is a runtime parameter rather than a compile-time one, so nothing
would flag the reach. Targeted `IgnoreQueryFilters()` on named queries keeps the widening visible in a diff.

**Which tables need it, and which do not:**

| Table | Filtered? | How the admin service reads it |
|---|---|---|
| `users` | No | Directly. |
| `chat_usage`, `vehicle_lookup_usage` | No | Directly, grouped by `OwnerId` and by `Day`. `CarTrackerDbContext.cs:72,79` exposes them unfiltered on purpose, so `ChatBudget`'s deployment-wide ceiling is correct. |
| `assistant_tokens`, `assistant_write_audits` | No | Directly. `AssistantToken.OwnerId` is nullable. |
| `vehicles` | **Yes** | `db.Vehicles.IgnoreQueryFilters()`, grouped by `OwnerId`. |
| `documents`, `data_anomalies` | No, but keyed by `VehicleId` | Joined to `db.Vehicles.IgnoreQueryFilters()` to reach an owner. The join itself must ignore filters or the join side contributes the predicate. |

### Grouped queries, never a loop

Per-account figures come from a handful of grouped queries stitched together in memory on `UserId`, not from
iterating accounts and issuing queries per row. At the scale this is built for the difference does not matter;
written as a loop it becomes an N+1 that nobody revisits until it is a hundred accounts and slow.

### Registration masking

`src/CarTracker.Domain/Admin/RegistrationMask.cs`:

```csharp
public static string Mask(string? registration);
```

Keep the first two characters and the last one; replace every other alphanumeric with `*`; preserve spaces and
hyphens so the result still reads as a plate. Under four characters, mask everything, because keeping two of
three reveals it. Null or whitespace returns an empty string.

- `BT53 AKJ` becomes `BT** **J`
- `BT53 AKJ-2` becomes `BT** ***-2`, so the import suffix survives, which is useful when two rows differ only
  by it

**Masking happens server-side and a full registration never appears in an admin response.** Masking in the
browser would not be masking, because the data would already have left the server. The spec states plainly
that this is **data minimisation rather than a security control**: the operator has database access anyway.
What it buys is a screen that can be opened, screenshotted and shared without spreading other people's plates,
and friction against curiosity. An address plus a masked plate is still enough to correlate a support request,
which is the only workflow that needs it.

## Part 3 - The plan override

### The ladder gets extracted

`AccountEntitlements.ResolveUncachedAsync` (`AccountEntitlements.cs:83-124`) reads `ICurrentUserAccessor` and
answers for one account. The admin list answers for all of them. A second copy of that ordering is a second
chance to get it wrong, and the ordering is load-bearing - its own doc comment says *"the order of the
refusals is the whole value of the reason."*

New pure static, `src/CarTracker.Domain/Accounts/PlanResolver.cs`:

```csharp
public static PlanResolution Resolve(
    EmailAllowlist comped,
    AccountPlan? planOverride,
    string? email,
    string? externalId,
    bool emailVerified);
```

`AccountEntitlements` loads its one user and calls it. `AdminReadService` loads N and calls it per row. The
two cases that are about the *caller* rather than the account - no resolved owner, no user row - stay in
`AccountEntitlements`, because "there is nobody to ask about" is not a fact about a user.

The ladder:

1. **`planOverride` is set** -> `(that plan, AdminGranted)`. First, so an explicit per-account decision beats
   both a comp match and the empty-comp-list case. An override of `Free` is meaningful and is why the column
   is a nullable plan rather than a boolean: it pins an account below a comp list it would otherwise match.
2. Comp list empty -> `(Free, NobodyIsComped)`
3. The sentinel `Email == ExternalId` -> `(Free, AddressUnknown)`
4. `!EmailVerified` -> `(Free, AddressNotVerified)`
5. Comp match -> `(Pro, Comped)`, otherwise `(Free, NotOnCompList)`

Steps 2 to 5 are byte-for-byte the existing behaviour, and `AccountEntitlementsTests` should still pass
unchanged for every account with a null override. That is the regression test for the extraction.

### One behaviour change, recorded rather than buried

`AccountEntitlements` currently returns `NobodyIsComped` at `:88` **before touching the database**. It can no
longer do that, because an override might exist and only the user row knows. The cost is one extra single-row
primary-key lookup per request on deployments with no comp list, already cached for the life of the request by
the existing `_resolved` field.

The comment at `AccountEntitlements.cs:85-87` explaining why `NobodyIsComped` is asked first needs **amending,
not deleting**. Its reasoning is still true - with no list, "you are not on the list" is true and useless -
it is simply no longer the first question, because there is now a question that is about this account
specifically and it outranks a statement about the deployment.

### `PlanReason` gains `AdminGranted`

An additive enum member on `AccountPlan.cs:37-67`. The account holder's own screen must say why they are on a
tier, and "granted by an administrator" is a different instruction from every existing reason: there is
nothing for them to do.

`PlanPanel.tsx:22` declares `const REASON: Record<PlanReason, string>` off the generated union specifically so
a new server-side reason fails the build rather than rendering a blank line. It will, and that is the guard
working. The comment above it at `:18-21` already explains why.

### Writing it

`PUT` and `DELETE /api/admin/users/{id}/plan` set and clear `users.plan_override`. Both are ordinary
`SaveChangesAsync` calls against a row found by primary key - `users` carries no query filter, so nothing needs
ignoring here. 404 for an unknown id.

The write is **logged at Information** with the acting `sub` from the `ClaimsPrincipal`, the target user id,
and the override before and after. Not a table: with one administrator a structured log line is proportionate,
and the point at which it stops being proportionate is the second administrator. That is recorded in the spec
under Out of Scope so it is a known gap rather than an oversight.

**There is no cache to invalidate.** `AccountEntitlements` caches for the life of one request, so the target
account's very next request resolves the new plan. That is the whole benefit over a config key and a restart,
and it falls out of the derive-on-read premise rather than being built.

## Part 4 - Last seen

`users.last_seen_at`, nullable, stamped in `AccountProvisioner.ResolveAsync` on the existing-account path
(`AccountProvisioner.cs:93-103`) **only when the stored value is null or older than fifteen minutes**. One
write per account per quarter hour rather than one per request. `AccountProvisioner` already takes a
`TimeProvider clock`, so nothing new is injected.

**It cannot go inside `BackfillEmailAsync`.** That method returns at `:225` when the address is present and
verified, which is the common case for every established account - so a stamp placed after that line would
fire only for accounts whose address is still being backfilled, and the column would be null for exactly the
accounts anyone wants to look at. A separate `TouchLastSeenAsync(existing, cancellationToken)` call sits
beside the backfill call in `ResolveAsync`, so neither method's early return can swallow the other's job.

It is an **observation, not a derived value**, so DEC-002 is not engaged. Worth stating in the code comment
because the file it lands in is otherwise entirely about the derive-on-read premise, and a reader finding a
new stored column there deserves the distinction. The alternative considered and rejected was deriving last
activity from `chat_usage.day`, `assistant_tokens.last_used_at` and the `IAuditable` timestamps on rows the
account created: it needs no migration and is blind to somebody who signs in, reads their dashboard and writes
nothing, which is most of a first session and precisely the visit worth knowing about.

## Part 5 - The client

### How the browser learns it is an administrator

The access token is never decoded client-side; `AuthGate` only registers `getAccessTokenSilently`. So the
capability arrives as a new required block on `AuthenticatedResponse` (`MetaEndpoints.cs:121-125`):

```csharp
public sealed record AdminCapabilities(bool CanReadAdmin, bool CanWritePlans);
```

That response is already fetched above the router by `AuthGate` and cached under `queryKeys.access`, so this
costs no extra request - the same free ride `useAllowances()` and `usePlan()` take. The endpoint reads the two
permissions off its own `ClaimsPrincipal` through `AdminAccess.Grants`, so the client and the policy cannot
disagree about what the token says.

**Required and non-nullable, not a defaulted parameter.** The remarks already on that record at `:114-120`
record the lesson: a defaulted record parameter emits as nullable in the OpenAPI document, the generated
client gets `AdminCapabilities | null`, and every consumer handles a null the endpoint cannot return. That
cost a CI break a commit before `0.24.1`.

### Screen and shell wiring

| File | Change |
|---|---|
| `src/api/queries.ts` | `useAdminAccess()` reading the `queryKeys.access` cache entry, returning `{ canReadAdmin, canWritePlans }`, each tested `=== true` at the call site so an in-flight response hides a control rather than offering one that answers 403 - the rule `useChatAvailable()` (`:178`) already follows |
| `src/routes.tsx` | `/admin` as a **sibling of `:reg`**, beside `/account` at `:118`, for the reason the comment there gives: a static segment outranks `:reg` and no registration reads "ADMIN" |
| `src/shell/nav.ts` | `CurrentScreen` gains `'admin'`. **`ScreenId` does not** - `hrefFor` returns `/` for any screen whose `scoped` is false without ever reading the id (`link.tsx:27`), so an unscoped `ScreenId` silently resolves to the garage. That trap is documented on `CurrentScreen` at `:37-51` and this is the second screen to avoid it |
| `src/shell/scope.ts` | `ShellScope` gains `{kind:'admin'}` rather than reusing `account`, following the argument in the comment at `:15-19`. All three consumers (`BottomNav.tsx:25`, `TopNav.tsx:55-79`, `NavMoreSheet.tsx:34,59,64`) test `scope.kind !== 'vehicle'`, so **none of them need editing** |
| `src/auth/UserMenu.tsx` | `export const ADMIN_PATH = '/admin'` beside `ACCOUNT_PATH` at `:6`, rendered through `renderLink` above Account and gated on `canReadAdmin === true` |
| `src/api/admin.ts` | Fetchers and `adminKeys`, following `api/import.ts` |
| `src/screens/AdminPage.tsx`, `src/screens/admin/*` | `DeploymentPanel`, `AccountsPanel`, `PosturePanel`, `AccountSheet` |

The page composes the house scaffolding: `PageHead` with an eyebrow and **no `plate`** (`coverage.test.ts:139`
fails the build on `plate={reg}`, and `usePlate()` throws off a `:reg` route), then `Wrap` and three
`Section`s. The account table is `<DataTable>` driven by `useTableView` - search over address and masked plate,
chips for Free/Pro and verified/unverified, sort by signed-up, last seen, tokens or vehicles - with
`<TableControls>` printing the "N of M" count. The posture list is `DerivedRow`, which is the component built
for a derived figure with no action. A row click opens `AccountSheet`, matching how every other table in the
application opens a row. The thirty-day series uses the existing `TimeChart` rather than a new chart.

## Testing

There is no `CarTracker.WebApi.Tests`, so everything provable lives in Domain or Data.

**Domain.Tests** (pure, no database):

- `AdminAccessTests` - holds the permission; holds a different one; holds several including this one; holds
  none; null and empty; exact ordinal match with no prefix or wildcard behaviour; and explicitly that
  `admin:plan:write` does not grant `admin:read`.
- `RegistrationMaskTests` - a normal plate, the import suffix, a plate with no space, inputs of one to three
  characters, null and whitespace, and the load-bearing negative: for any registration of four characters or
  more the output is not equal to the input.
- `PlanResolverTests` - an override of `Pro` beats an empty comp list; an override of `Free` beats a comp
  match; a null override reproduces each of the four existing reasons in the documented order.

**Data.Tests** (real PostgreSQL via Testcontainers, per class database):

- `AdminReadServiceTests` - **two seeded owners**, contexts built through `TestOwner.As(ownerA)` so the
  accessor is pinned the way the request pipeline pins it. Asserts the service returns owner B's vehicle
  count, masked plates, document count, token spend and anomaly count. This is the only thing that proves
  `IgnoreQueryFilters()` is actually on every query, and it must be checked against a deliberately
  un-widened query before it is kept - `TestOwner.As`'s own doc comment warns that a bypassed context makes an
  isolation test a false green, and this test has the mirror-image hazard.
- `AdminPlanOverrideTests` - setting an override changes what `AccountEntitlements` resolves for that account
  on a fresh context, and changes nothing for the other owner. Clearing it restores the comp-list answer.
- Extend `AccountEntitlementsTests` for override precedence, keeping every existing case green.
- Extend an account-provisioning test for the last-seen coalescing: a first resolve stamps it, an immediate
  second does not write, and one fifteen minutes later does. `FakeTimeProvider` makes this exact.

**Frontend** (vitest, `globalThis.fetch` stubbed - note the ordering trap at `AccountPage.test.tsx:58-63`,
match `/api/meta/authenticated` before `/api/meta`):

- `AdminPage.test.tsx` - renders the list, plates are masked in the DOM, the deployment figure appears,
  `axe(container)` is clean, statuses pass `expectStateIsReadable`.
- Extend `shell.test.tsx` - the Admin link is absent without `canReadAdmin`, present with it, and absent while
  the access response is in flight.
- `coverage.test.ts` - add the four `screens/admin/*` panels to `EXEMPT` with the wording the four account
  panels already use at `:46-49`, since `AdminPage.test.tsx` sweeps the whole page with axe.

## Configuration, and the absence of it

**No new configuration key.** The gate is tenant state, so `deploy/.env.example`, `docker-compose.yml` and
`appsettings.json` are all untouched. Worth stating explicitly given how much of this repository's
documentation exists because a key went missing between the compose file, the host `.env` and a Watchtower
recreate.

What is needed instead is four steps in the Auth0 dashboard, which nothing here can perform or verify, and
which belong in the README:

1. Applications -> APIs -> `cartracker.api` -> Settings: enable **RBAC** *and* **Add Permissions in the Access
   Token**. Both. The first alone adds nothing to the token.
2. Permissions tab: add `admin:read` and `admin:plan:write` with descriptions.
3. Create a role holding both and assign the role, rather than assigning the two permissions to the user
   directly. That is what makes expanding later one assignment instead of several.
4. **Sign out and sign back in.** An access token issued before the permission was assigned does not carry it,
   and refresh-token rotation will not add it either. This is the step that will otherwise look exactly like a
   broken feature.

## Contract and release chores

- `dotnet build && cd src/CarTracker.WebApp && npm run gen:api`, committing both `api-contract/v1.json` and
  `src/CarTracker.WebApp/src/api/generated/schema.d.ts`. CI fails on a stale diff (`ci.yml:38-60`).
- The diff is **additive**: six new paths and their shapes, `AdminCapabilities` on `AuthenticatedResponse`,
  and `AdminGranted` on `PlanReason`. No existing path changes and no existing field is removed or renamed.
- `VERSION` minor bump, 0.25.0 to 0.26.0, **in the feature commit itself**.
- `docs/product/roadmap.md` (dateline plus a shipped entry) and the CLAUDE.md state-of-play entry.
- DEC-023 in `docs/product/decisions.md`, amending DEC-022.
