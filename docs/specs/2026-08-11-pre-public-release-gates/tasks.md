# Spec Tasks

## Tasks

- [ ] 1. Prove the defect before fixing it
  - [ ] 1.1 Write a Domain test: two owners, each with a vehicle and a garage named "K & P Motors" attached to
        a service record. Owner A renames theirs to "K&P Motors". **Assert owner B's service record still
        reads "K & P Motors".** This must fail on current code — B's row is rewritten by
        `ReferenceListEditor.cs:124`
  - [ ] 1.2 The same test for a wash location (`WashEntry.Location`) and for an expense category, the latter
        asserting **both** `ExpenseEntry.Category` and `BudgetGroupCategory.Category` are untouched
  - [ ] 1.3 A test asserting `ListGaragesAsync`/`ListCategoriesAsync` return a `referenceCount` covering only
        the caller's rows — today `CountCategoryReferencesAsync` aggregates across every account
  - [ ] 1.4 Record the failing output in the commit message or here. These four tests are the entire
        justification for reversing a recorded roadmap decision, and "it failed" is worth more than "it
        would have failed"

  > Write these first and watch them go red. Every one of the four bugs CLAUDE.md records under *"read this
  > before adding a screen"* came from acting on a plausible guess instead of the source; this is the same
  > shape of mistake caught one step earlier.

- [ ] 2. Schema: per-owner reference lists
  - [ ] 2.1 Add `OwnerId` to `Garage`, `WashLocation`, `ExpenseCategory`; composite `HasKey(x => new
        { x.OwnerId, x.Name })` in the three configurations; FK to `User` with `DeleteBehavior.Cascade`
  - [ ] 2.2 Drop the **six** FK declarations: `ServiceRecordConfiguration.cs:31`,
        `MaintenanceTaskConfiguration.cs:35`, `VehicleConfiguration.cs:120-123`,
        `WashEntryConfiguration.cs:26`, `ExpenseEntryConfiguration.cs:30-33`,
        `BudgetGroupCategoryConfiguration.cs:17-20`. **The six child columns are unchanged** — only the
        constraints go
  - [ ] 2.3 Remove `ExpenseCategoryConfiguration.HasData(SystemCategories)`; keep the array
  - [ ] 2.4 Migration `AddPerOwnerReferenceLists` with the backfill: copy each reference row per existing user,
        delete the ownerless originals, set `NOT NULL`, replace the primary keys
  - [ ] 2.5 Verify the migration against a **restored dump**, not an empty database — it deletes rows, and an
        empty database proves only that the no-op path works
  - [ ] 2.6 Data test: two users each hold a garage of the same name and both rows exist

- [ ] 3. Isolation: filters, stamping, and the cascades
  - [ ] 3.1 Three query filters in `CarTrackerDbContext.OnModelCreating`, beside the `Vehicle` one at line 85,
        using the same `BypassOwnership || x.OwnerId == CurrentOwnerId` predicate
  - [ ] 3.2 `ReferenceWriter` and `ReferenceListEditor` take `ICurrentUserAccessor` and stamp `OwnerId` on
        every insert, including the insert-new half of each rename. Guard: a write with no resolved owner
        throws with a clear message rather than inserting a null and surfacing as a 500
  - [ ] 3.3 Scope every cascade and count through `context.Vehicles.Any(v => v.Id == x.VehicleId)` — the eight
        methods listed in the technical spec's table. `context.Vehicles.Where(...)` is already filtered
  - [ ] 3.4 **Inspect the generated SQL** for one `ExecuteUpdateAsync` to confirm EF translates the correlated
        subquery. If it does not, fall back to a materialised owned-id list + `Contains` — consciously, and
        noted here, because a silent rewrite that drops the correlation restores the bug
  - [ ] 3.5 Provision the 13 `SystemCategories` per user in `CurrentUserMiddleware.ResolveAuth0UserAsync`, in
        the **same** `SaveChangesAsync` as the `User` row, so the existing lost-the-race handler covers both
  - [ ] 3.6 The tests from task 1 now pass. Confirm `Fuel`/`Purchase` remain rename-locked per user and the
        13 remain undeletable

- [ ] 4. Account deletion
  - [ ] 4.1 Write tests first: every table empty for the deleted owner, every table untouched for a second
        owner, the `{root}/{vehicleId}/` folders gone, and a test that the operation respects the `Restrict`
        FKs by ordering rather than by disabling them
  - [ ] 4.2 `PendingIdentityDeletion` entity + configuration + migration `AddPendingIdentityDeletions`
  - [ ] 4.3 `AccountDeletionService` in `CarTracker.Domain`: collect vehicle ids → transaction inside
        `CreateExecutionStrategy().ExecuteAsync` (vehicles → tokens → reference rows → user → pending row) →
        commit → **then** document bytes → then the identity call
  - [ ] 4.4 `IIdentityProviderClient` against Auth0 Management `DELETE /api/v2/users/{id}` with client
        credentials. Treat a 404 as success. Config under `Auth0:Management:`; **unset means the endpoint
        503s and deletes nothing**, following the `Lookup:` NotConfigured precedent
  - [ ] 4.5 Retry hosted service for pending rows, registered like `RemindersBackgroundService`
  - [ ] 4.6 `AccountEndpoints`: `DELETE /api/account` (body `confirmEmail`, must match; 403 for an
        assistant-token principal), `GET /api/account/summary`. Register in `Program.cs` beside the others
  - [ ] 4.7 `GET /api/meta` gains the identity-deletion-configured flag

  > The endpoint requires `confirmEmail` in the body even though the UI already asks for it. The client is not
  > the only possible caller, and an account-deleting `DELETE` that succeeds on an empty body is one mis-wired
  > button away from being catastrophic.

- [ ] 5. Export
  - [ ] 5.1 Test first: the payload contains no derived key (assert against a list of `IDerivedMetricsService`
        outputs — mpg, cost-per-mile, statuses, totals), contains no token secret, and contains no other
        owner's rows
  - [ ] 5.2 `GET /api/account/export` built from `LogQueryService`, `DocumentService.GetLogAsync` and the
        existing vehicle/reference queries, reusing the row DTOs in `CarTracker.Shared/Logs/` so it cannot
        drift from what is stored
  - [ ] 5.3 Stream the JSON rather than materialising the whole graph — correct once is cheaper than correct
        later, and several vehicles with years of history is the case that matters
  - [ ] 5.4 `Content-Disposition: attachment`, filename carrying the export date; `notes` array stating why no
        computed figures are present and that document bytes are excluded

- [ ] 6. The door
  - [ ] 6.1 Test first: an email outside the allowlist creates **no** `User` row and returns the
        `signup-not-invited` 403; an empty allowlist admits nobody new
  - [ ] 6.2 Allowlist check in `CurrentUserMiddleware.ResolveAuth0UserAsync` before provisioning
        (`Signup:AllowedEmails`, `Signup:AllowedDomains`)
  - [ ] 6.3 Replace the `Users.CountAsync() == 1` adoption block (`CurrentUserMiddleware.cs:96-101`) with
        `Ownership:ClaimUnownedVehiclesFor`, default null → no adoption ever
  - [ ] 6.4 Document in `.env.example` and the README Quickstart that an **empty allowlist means closed** —
        the opposite is the natural reading and would silently open registration

- [ ] 7. Client
  - [ ] 7.1 Tests first: the panel states the counts before arming; the destructive button stays disabled
        until the email matches exactly; the panel degrades to export-only when `meta` reports deletion
        unconfigured; axe passes in both themes
  - [ ] 7.2 `DangerZonePanel` in `screens/settings/`, reusing `Sheet`, `Field`'s `error` prop and
        `reportApiError`. **Not `ConfirmButton`** — its two-step is calibrated for one log row
  - [ ] 7.3 Export download above deletion in the same panel
  - [ ] 7.4 `AuthGate` gains the signed-in-but-refused state: a short "not yet invited" panel with sign-out,
        neither the app nor `LandingPage`
  - [ ] 7.5 `LandingPage` sign-up copy says access is by invitation — and still passes its jargon guard
  - [ ] 7.6 On successful deletion, call Auth0 `logout()`; never attempt to re-render the app

- [ ] 8. Record the decisions and close the gates
  - [ ] 8.1 DEC in `docs/product/decisions.md`: reference lists keyed `(OwnerId, Name)` with the six FK
        constraints dropped, reversing `roadmap.md:206`'s surrogate-id shape. Carry the argument — the FK's
        only behaviours are a `SetNull` the editor exists to prevent, a `Restrict` it duplicates, and a
        `Cascade` it overrides
  - [ ] 8.2 DEC amendment retiring DEC-016, and noting that a refused sign-up still leaves an Auth0 identity
  - [ ] 8.3 Update `roadmap.md:198-213`: gate 1 closed (and restated — it was a cross-tenant write, not a
        visibility problem), gate 3 closed, **HTTPS still open**. Add Art. 15/17/20 to the record so the next
        reader does not rediscover them
  - [ ] 8.4 Update `CLAUDE.md`'s state-of-play with the new test counts and the per-owner reference lists
  - [ ] 8.5 Regenerate `api-contract/` and the typed client; confirm the CI staleness gate passes
  - [ ] 8.6 Bump `VERSION` a **minor**, `git add VERSION` **into the feature commit** — CI publishes nothing
        when it is unchanged

- [ ] 9. Verify end to end with two real accounts
  - [ ] 9.1 `dotnet build`, `dotnet test` (needs Docker), `npm --prefix src/CarTracker.WebApp run test`
  - [ ] 9.2 `dotnet run --project src/CarTracker.AppHost`; sign in as two different Auth0 accounts
  - [ ] 9.3 Both create a garage named "K & P Motors" and attach it to a service record. A renames theirs.
        **B's record is unchanged.** Repeat for a wash location and a category rename
  - [ ] 9.4 B's `GET /api/reference/garages` lists only B's, with B's counts
  - [ ] 9.5 Export as A: contains A's vehicles, none of B's, no derived figures, no token secrets
  - [ ] 9.6 Delete A: rows gone, `{Documents:RootPath}/{vehicleId}/` gone from disk, B untouched, the Auth0
        identity gone or a `pending_identity_deletions` row present
  - [ ] 9.7 With `Auth0:Management:` unset, the delete endpoint 503s and **deletes nothing** — check the
        database afterwards rather than trusting the status code
  - [ ] 9.8 An email outside the allowlist gets the not-invited state and creates no `User` row
