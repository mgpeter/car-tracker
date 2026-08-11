# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-08-11-pre-public-release-gates/spec.md

Four parts. Part 1 fixes a live defect; parts 2–4 add what a public deployment legally and practically needs.

---

## Part 1 — Per-owner reference lists

### The idiom: one filter mechanism, not two

Phase 4.5 chose "one global EF query filter on `Vehicle`" over threading an ownerId through ~35 call sites,
because a new endpoint cannot forget a filter it never has to remember. That choice is right and this work
extends it rather than introducing a second style.

`CarTrackerDbContext.OnModelCreating` gains three filters beside the existing one at line 85, using the same
predicate against the same private members:

```csharp
modelBuilder.Entity<Garage>().HasQueryFilter(g => BypassOwnership || g.OwnerId == CurrentOwnerId);
modelBuilder.Entity<WashLocation>().HasQueryFilter(w => BypassOwnership || w.OwnerId == CurrentOwnerId);
modelBuilder.Entity<ExpenseCategory>().HasQueryFilter(c => BypassOwnership || c.OwnerId == CurrentOwnerId);
```

Every read in `ReferenceListEditor`, `ReferenceWriter` and `ReferenceEndpoints` is then scoped with no
call-site change — `context.Garages.AnyAsync(g => g.Name == name)` becomes owner-scoped for free, and a name
belonging to another user simply does not resolve, so the existing `ReferenceOpResult.NotFound` path already
produces the right answer.

### Writes must stamp the owner

`ReferenceWriter` and `ReferenceListEditor` take `ICurrentUserAccessor` and set `OwnerId` on every insert:
`EnsureGarageAsync`, `EnsureWashLocationAsync`, `CreateGarageAsync`, `CreateWashLocationAsync`, and the
insert-new half of each rename.

The accessor is already populated for **both** surfaces by `CurrentUserMiddleware` — an Auth0 `sub` resolves
to a user, and an MCP principal carries `AssistantClaims.UserId` — so nothing needs threading and MCP writes
are covered by the same line of code as web writes.

`OwnerId` is `int?` on the entity to match `ICurrentUserAccessor.OwnerId`, but a write with no resolved owner
must throw rather than insert a null: an unowned reference row is the state this spec exists to eliminate, and
the database's `NOT NULL` would catch it as a 500 with no explanation. A guard in the writer with a clear
message is cheaper to diagnose.

### The cross-tenant cascade — the actual defect

Every `ExecuteUpdateAsync` and every reference count in `ReferenceListEditor` operates on a table with **no**
query filter, matching on a name. These are the statements that write into other accounts:

| Method | Statements over | Line |
|---|---|---|
| `CountGarageReferencesAsync` | `ServiceRecords`, `MaintenanceTasks` (+ filtered `Vehicles`) | `:73` |
| `UpdateGarageAsync` | `ServiceRecords`, `MaintenanceTasks` | `:124`, `:126` |
| `DeleteGarageAsync` | `ServiceRecords`, `MaintenanceTasks` | `:152`, `:154` |
| `CountWashReferencesAsync` | `WashEntries` | `:163` |
| `UpdateWashLocationAsync` / `DeleteWashLocationAsync` | `WashEntries` | `:207`, `:233` |
| `CountCategoryReferencesAsync` | `ExpenseEntries`, `BudgetGroupCategories` | `:249-251` |
| `UpdateCategoryAsync` / `DeleteCategoryAsync` | `ExpenseEntries`, `BudgetGroupCategories` | `:296-297`, `:327-328` |

Each is constrained by a subquery through `Vehicles`, which **inherits the vehicle query filter**:

```csharp
await context.ServiceRecords
    .Where(s => s.Garage == name && context.Vehicles.Any(v => v.Id == s.VehicleId))
    .ExecuteUpdateAsync(u => u.SetProperty(s => s.Garage, newName), ct);
```

This is owner-scoped by construction rather than by a threaded ownerId, which keeps the "one filter" property:
the correlation cannot be written correctly-but-unscoped, because there is no unscoped `Vehicles` to write it
against.

`context.Vehicles.Where(v => v.DefaultGarage == name)` in the garage paths is already filtered and needs
nothing. `BudgetGroupCategory` carries `VehicleId`, so the same subquery applies.

> **Verify the generated SQL rather than assuming it.** If EF declines to translate a correlated `Any()` inside
> an `ExecuteUpdate`, the fallback is to materialise `var ownedVehicleIds = await context.Vehicles.Select(v =>
> v.Id).ToListAsync(ct)` from the filtered set first and use `Contains`. Same guarantee, one extra round trip,
> and it must be a conscious fallback rather than a silent rewrite that loses the scoping.

### Per-user category provisioning

`CurrentUserMiddleware.ResolveAuth0UserAsync` adds the 13 `ExpenseCategoryConfiguration.SystemCategories` for
the new user in the **same** `SaveChangesAsync` as the `User` row (`CurrentUserMiddleware.cs:80-84`), so no
account can exist without categories and the existing lost-the-race `DbUpdateException` handler covers both.

`IsSystem` stays true on all 13, so they remain undeletable per user. `IsMirrorOwned` — `Fuel` and `Purchase`
— stays rename-locked per user; the mirrors resolve by the exact constant within the owner's own set, so the
constant does not change and `MirrorRenameLocked` needs no adjustment.

### Tests

- **Data (Testcontainers):** two users each insert a garage named "K & P Motors"; both rows exist. Today this
  fails on the primary key, which is the schema half of the bug.
- **Domain, and write it first:** user A renames their garage; user B's service records, tasks and default
  garage are unchanged. On current code this fails — B's rows are rewritten. That failing test is the
  justification for the whole part and should be seen red before anything is fixed.
- The same pair for wash locations, and for categories covering **both** `ExpenseEntries` and
  `BudgetGroupCategories`.
- Reference counts: user A's `referenceCount` for a shared name counts only A's rows.

---

## Part 2 — Account deletion

New `AccountDeletionService` in `CarTracker.Domain`, called by `AccountEndpoints`.

### The order is forced, not chosen

`Vehicle.OwnerId` (`VehicleConfiguration.cs:22`) and `AssistantToken.OwnerId`
(`AssistantTokenConfiguration.cs:17`) are both `DeleteBehavior.Restrict`. The `User` row cannot go first; the
database will refuse.

1. **Collect the owned vehicle ids first**, before anything is deleted — step 5 needs them and they are
   unobtainable afterwards.
2. Inside `context.Database.CreateExecutionStrategy().ExecuteAsync(...)`, in one transaction — **mandatory**:
   Aspire's `EnrichNpgsqlDbContext` installs a retrying strategy that refuses a user-initiated transaction
   outside it, and CLAUDE.md records that the tests do not catch this because the test context has no retry
   strategy:
   - `Vehicles` — 13 child tables cascade directly (`budget_groups`, `check_definitions`, `data_anomalies`,
     `documents`, `equipment_items`, `expense_entries`, `fuel_entries`, `issues`, `maintenance_tasks`,
     `mileage_readings`, `service_records`, `tyre_readings`, `wash_entries`) and 3 more indirectly
     (`check_logs` via definitions, `budget_group_categories` via groups, `issue_watch_checks` via both).
   - `AssistantTokens` — `AssistantWriteAudit` cascades from the token
     (`AssistantTokenConfiguration.cs:47`). Note `OwnerId` is nullable: tokens minted before Phase 4.5 may
     have none, and those are not this user's to delete.
   - The owner's `Garages`, `WashLocations`, `ExpenseCategories` — explicitly, even though the new
     `ON DELETE CASCADE` to `users` would do it. Relying on a cascade to perform something you intended is
     how the document bytes came to be forgotten in the first place.
   - The `User` row.
   - A `PendingIdentityDeletion` row for the external id.
3. **Commit.**
4. Delete the document bytes: remove `{Documents:RootPath}/{vehicleId}/` for each collected id.
5. Attempt the Auth0 identity deletion; on success, remove the pending row.

### Why bytes come after the commit

`DocumentConfiguration.cs:28` cascades the document *rows*; nothing removes the files. Content-addressing
means files live under a per-vehicle folder, so removing the folder is safe and no cross-vehicle sharing can
be orphaned — `DocumentStore.Delete(relativePath, stillReferenced)` handles the within-vehicle sharing case
and is not needed here.

The ordering is the same rows-then-bytes rule `DocumentService.DeleteAsync` already follows, for the same
reason: if the transaction rolls back after the files are gone, the rows survive pointing at nothing, and the
documents screen renders broken images for data the user still has. Bytes orphaned by a failed unlink are
invisible and harmless, and a sweep can reclaim them. The failure modes are not symmetric, so the order is not
arbitrary.

### Auth0 identity deletion, and why failure is recorded rather than logged

A new `IIdentityProviderClient` in `CarTracker.WebApi` (the HTTP lives with the other outbound HTTP, as
`Lookup/` does; the vocabulary can stay in `Domain` if it needs testing) calls the Management API's
`DELETE /api/v2/users/{id}`, authenticating with client credentials.

The three orderings were considered and only one is defensible:

- **Identity first, then data** — if the local delete then fails, the person cannot sign in and their data is
  stranded, unreachable and undeletable. Worst outcome.
- **Identity inside the transaction** — an external call inside a database transaction, and a commit failure
  after a successful identity deletion produces the same stranding.
- **Data first, identity after** — a failure leaves the identity alive with no data behind it. Signing in
  again provisions a fresh empty account. Harmless.

The third is chosen, and its failure is written to `pending_identity_deletions` rather than logged, because
"harmless" is not "erased" and the difference is exactly what a regulator would ask about. A small hosted
service (sibling to `RemindersBackgroundService`, same registration pattern) retries the pending rows on an
interval, incrementing `Attempts` and recording `LastError`.

Deletion is idempotent: Auth0 returning 404 for an already-deleted identity clears the pending row.

### Confirmation UI

New `DangerZonePanel` in `src/CarTracker.WebApp/src/screens/settings/`, alongside `AppearancePanel`,
`AssistantAccessPanel`, `ReferenceListsPanel` and the rest.

**`ConfirmButton` is deliberately not used.** That primitive's two-step is calibrated for deleting one fuel
fill from a table — the right weight for a mistake that takes thirty seconds to re-enter. Destroying an
account is not that, and reusing the component would be consistency for its own sake.

Instead: a `Sheet` that states the counts from `GET /api/account/summary` in prose ("1 vehicle, 214 log
entries, 6 documents"), lists what will be destroyed including the Auth0 login, and requires typing the
account email exactly before the destructive button enables. Reuse `Field` with its `error` prop for the typed
confirmation, and `reportApiError`/`fieldError` from `lib/formErrors.ts` so a server-side mismatch marks the
field rather than showing a banner.

The export download sits **above** the deletion in the same panel. Offering someone their data next to the
button that destroys it is the honest ordering, and it costs nothing.

On success: call Auth0 `logout()`. The client must not attempt to re-render the app — there is no account
behind the session any more.

If `GET /api/meta` reports identity deletion unconfigured, the panel shows the export and explains that
deletion is unavailable on this deployment, rather than offering a button that 503s.

---

## Part 3 — Export

`GET /api/account/export` builds its payload from existing services — `LogQueryService`,
`DocumentService.GetLogAsync`, and the vehicle/reference queries the endpoints already use. No new queries,
and specifically no new *shapes*: the export ships the same row DTOs from `CarTracker.Shared/Logs/` that the
screens receive, so it cannot drift from what the app actually stores.

Nothing from `IDerivedMetricsService`. Every derived figure is recomputable from the rows by definition, and
an export carrying stored derived values would reproduce the exact defect the five workbook figures document —
in the one artefact whose whole purpose is to be read later, when nothing can recompute it. The payload's
`notes` array says this in plain words, because the absence is otherwise indistinguishable from an oversight.

Memory: build it streaming rather than materialising every row of every vehicle into one object graph.
A single vehicle is small today; an account with several vehicles and years of history is the case that
matters, and `System.Text.Json`'s async writer over a response stream costs nothing extra to write correctly
the first time.

---

## Part 4 — The door

Both changes are in `CurrentUserMiddleware.ResolveAuth0UserAsync` — the only place in the codebase where a
local account comes into existence, which is why both belong there and nowhere else.

### Allowlist

Before provisioning an unseen `sub`, check the token's `email` claim against `Signup:AllowedEmails` and
`Signup:AllowedDomains`. Not allowed → **do not create the `User`**, and let the request fail with a distinct
`403` problem type (`signup-not-invited`).

Refusing before the row exists, rather than creating and flagging it, means a rejected person leaves no trace
to clean up and no half-state for the ownership filter to reason about.

An **empty allowlist means closed**. This is the fail-safe direction and the opposite of the natural reading,
so it is stated in `.env.example`, the README Quickstart and the config table in the API spec.

Caveat worth stating in the DEC: Auth0 will still create the identity in the tenant, so a refused person has
an Auth0 login and no app account. That is the standard shape of this pattern and is not a leak, but it means
the tenant accumulates identities that were never admitted. Disabling public sign-up in the Auth0 dashboard is
the belt to this braces and is a dashboard action, not code.

The client: `AuthGate` gains a third state between "loading" and "authenticated" — signed in, refused. It
renders a short "not yet invited" panel with a sign-out control, not the app and not `LandingPage`. The
`LandingPage` sign-up CTA copy should say access is currently by invitation; whatever wording is chosen still
has to pass that file's jargon guard.

### DEC-016 retired

Replace the `Users.CountAsync() == 1` adoption block (`CurrentUserMiddleware.cs:96-101`) with an explicit
`Ownership:ClaimUnownedVehiclesFor` external id. Adoption happens only when the provisioning `sub` matches it
exactly. Default null → no adoption, ever.

The current deployment is unaffected: BT53 was claimed in July 2026 and there are no unowned vehicles left to
adopt.

---

## Cross-cutting

- **Contract:** three new endpoints and one new `meta` flag, all additive. Regenerate `api-contract/` and the
  typed client so the CI staleness gate passes.
- **Tests:** the isolation regression tests are the load-bearing ones. Deletion needs a Data test asserting
  every table is empty for the deleted owner and untouched for a second owner, plus one asserting the
  `Restrict` FKs are respected in order. Export needs a test asserting no derived key appears in the payload.
  The allowlist needs a test asserting a refused email creates no `User` row.
- **`VERSION`:** a **minor** bump inside the feature commit. CI publishes nothing when `VERSION` is unchanged,
  and the run summary says so loudly — but a silent non-deploy is still the failure that gate introduced.
