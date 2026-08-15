# Spec Tasks

## Tasks

- [x] 1. Prove the defect before fixing it
  - [x] 1.1 Write a Domain test: two owners, each with a vehicle and a garage named "K & P Motors" attached to
        a service record. Owner A renames theirs to "K&P Motors". **Assert owner B's service record still
        reads "K & P Motors".** This must fail on current code - B's row is rewritten by
        `ReferenceListEditor.cs:124`
  - [x] 1.2 The same test for a wash location (`WashEntry.Location`) and for an expense category, the latter
        asserting **both** `ExpenseEntry.Category` and `BudgetGroupCategory.Category` are untouched
  - [x] 1.3 A test asserting `ListGaragesAsync`/`ListCategoriesAsync` return a `referenceCount` covering only
        the caller's rows - today `CountCategoryReferencesAsync` aggregates across every account
  - [x] 1.4 Record the failing output in the commit message or here. These four tests are the entire
        justification for reversing a recorded roadmap decision, and "it failed" is worth more than "it
        would have failed"

  > Write these first and watch them go red. Every one of the four bugs CLAUDE.md records under *"read this
  > before adding a screen"* came from acting on a plausible guess instead of the source; this is the same
  > shape of mistake caught one step earlier.

  > **Done in `35a0f06` - `ReferenceListCrossTenantTests.cs` (410 lines), plus the per-owner `referenceCount`
  > assertions in `ReferenceTableTests` and `ReferenceListEditorTests`.** They were written first and seen red,
  > and the red output paid for itself immediately: **B's three references did not fail the same way.** Two came
  > back rewritten into a name B never chose, and the third - `vehicles.default_garage` - came back **NULL**,
  > because `context.Vehicles` *is* already filtered, so B's vehicle was correctly left out of the repointing
  > and then the `SetNull` foreign key blanked the field when the old row was dropped.
  >
  > That finding reordered the whole spec. **Partial scoping is worse than none**: scoping the editor's
  > statements without also changing the key would have produced that third line on all four garage/wash
  > columns. So the composite key and the FK drops became a *prerequisite* of scoping the cascade rather than a
  > tidy-up after it - which is the opposite of the order tasks 2 and 3 are written in. The failing output is
  > preserved in DEC-018's Context section (`decisions.md:1223-1231`), not just in the commit message.
  >
  > 1.3 also found the gate had never named `ExpenseCategory`, which had the identical defect twice over - and
  > `GET /api/reference/expense-categories` was reporting every account's usage as your own.

- [x] 2. Schema: per-owner reference lists
  - [x] 2.1 Add `OwnerId` to `Garage`, `WashLocation`, `ExpenseCategory`; composite `HasKey(x => new
        { x.OwnerId, x.Name })` in the three configurations; FK to `User` with `DeleteBehavior.Cascade`
  - [x] 2.2 Drop the **six** FK declarations: `ServiceRecordConfiguration.cs:31`,
        `MaintenanceTaskConfiguration.cs:35`, `VehicleConfiguration.cs:120-123`,
        `WashEntryConfiguration.cs:26`, `ExpenseEntryConfiguration.cs:30-33`,
        `BudgetGroupCategoryConfiguration.cs:17-20`. **The six child columns are unchanged** - only the
        constraints go
  - [x] 2.3 Remove `ExpenseCategoryConfiguration.HasData(SystemCategories)`; keep the array
        > The array stays but could not be handed to `AddRange` as it was: `SystemCategories` holds live entity
        > instances, so attaching them would put process-wide singletons on a `DbContext`.
        > `SystemCategoriesFor(ownerId)` projects fresh ones per account.
  - [x] 2.4 Migration `AddPerOwnerReferenceLists` with the backfill: copy each reference row per existing user,
        delete the ownerless originals, set `NOT NULL`, replace the primary keys
        > **Hand-written; EF's generated `Up()` was thrown away** - its `DeleteData` is keyed on the old primary
        > key and eats the per-user copies. Ordered SQL instead: drop the 6 FKs → drop the 3 single-column PKs →
        > add `owner_id` nullable → copy per user → `DELETE WHERE owner_id IS NULL` → `SET NOT NULL` → add the 3
        > composite PKs. `Down()` throws `NotSupportedException`; this is one-way.
  - [x] 2.5 ~~Verify the migration against a **restored dump**, not an empty database~~ - **retired, and
        replaced by something a machine can enforce.** Verifying against a dump is not an instruction anyone
        can execute in CI, and "someone checked once" is not a property. The migration instead **asserts
        `users` count ≤ 1 and aborts otherwise**: a per-user copy of a shared row is only unambiguous while
        there is one user, so the precondition is enforced rather than trusted.
        `PerOwnerReferenceListBackfillTests` proves both halves against a real database by migrating to the
        *previous* migration, seeding through the old schema, and migrating up - one account keeps every row
        and every child name; two accounts abort with the garage untouched and `__EFMigrationsHistory`
        unmoved. See `sub-specs/database-schema.md:83-86`.
  - [x] 2.6 Data test: two users each hold a garage of the same name and both rows exist

- [x] 3. Isolation: filters, stamping, and the cascades
  - [x] 3.1 Three query filters in `CarTrackerDbContext.OnModelCreating`, beside the `Vehicle` one at line 85,
        using the same `BypassOwnership || x.OwnerId == CurrentOwnerId` predicate
        > Now at `CarTrackerDbContext.cs:96-98`. One mechanism extended, not a second style introduced - which
        > is the argument DEC-018 rests on.
  - [x] 3.2 `ReferenceWriter` and `ReferenceListEditor` take `ICurrentUserAccessor` and stamp `OwnerId` on
        every insert, including the insert-new half of each rename. Guard: a write with no resolved owner
        throws with a clear message rather than inserting a null and surfacing as a 500
        > **The guard is narrower than this, deliberately.** `ReferenceOwner.Require` (new file) refuses in two
        > distinct sentences - *no request context* means the caller is wrong, *a request that resolved no
        > account* means the pipeline is wrong - but it guards the four **create** inserts only. Reads and edits
        > still run under a bypass context, because refusing there would make every existing Data test
        > unrunnable to prevent a hazard those tests do not exhibit. The three *rename* inserts take the owner
        > from the row being renamed: a rename changes one key component, not both.
        >
        > The real bypass hazard is not a null insert at all - it is
        > `Garages.Where(g => g.Name == name).ExecuteDeleteAsync` deleting **every** account's row, since
        > `BypassOwnership` is a runtime parameter and the filter then contributes nothing. That is closed by
        > naming the **whole primary key** on all six reference-table deletes, not by an exception.
  - [x] 3.3 Scope every cascade and count through `context.Vehicles.Any(v => v.Id == x.VehicleId)` - the eight
        methods listed in the technical spec's table. `context.Vehicles.Where(...)` is already filtered
        > **Fifteen statements, not eight** - five *counts* needed it too, and the technical spec's table
        > undercounted the cascades. Covered by `OwnerScopedBulkSqlTests.cs`.
  - [x] 3.4 **Inspect the generated SQL** for one `ExecuteUpdateAsync` to confirm EF translates the correlated
        subquery. If it does not, fall back to a materialised owned-id list + `Contains` - consciously, and
        noted here, because a silent rewrite that drops the correlation restores the bug
        > **The correlated subquery held; no `Contains` fallback was needed.** And rather than inspecting it
        > once by hand, `OwnerScopedBulkSqlTests.cs` (247 lines) asserts `owner_id` appears inside the generated
        > `EXISTS` - so a future EF upgrade that silently stops correlating fails a test instead of restoring
        > the bug quietly. A one-off inspection would not have survived the next package bump.
  - [x] 3.5 ~~Provision the 13 `SystemCategories` per user in `CurrentUserMiddleware.ResolveAuth0UserAsync`, in
        the **same** `SaveChangesAsync` as the `User` row~~ - **impossible as specified, and built as two
        saves.** `user.Id` is store-generated and the owner FK is navigation-less, so the categories cannot be
        written in the same `SaveChangesAsync` that creates the row they point at. Provisioning is therefore
        two saves in `AccountProvisioner` (domain, not the middleware - see 8.2), and the lost-the-race catch
        does `ChangeTracker.Clear()` rather than detaching the user alone, which would strand 13 Added rows.
  - [x] 3.6 The tests from task 1 now pass. Confirm `Fuel`/`Purchase` remain rename-locked per user and the
        13 remain undeletable
        > Both hold **within each owner's own set** now. One consequence worth carrying forward, recorded in
        > DEC-018's negatives: `FuelEntryFactory` and `VehiclePurchaseMirror` resolve `"Fuel"`/`"Purchase"` by
        > exact name, and the `Restrict` FK that backstopped them is gone - the guarantee now rests on
        > provisioning and the rename lock, with no database constraint behind it.

- [x] 4. Account deletion
  - [x] 4.1 Write tests first: every table empty for the deleted owner, every table untouched for a second
        owner, the `{root}/{vehicleId}/` folders gone, and a test that the operation respects the `Restrict`
        FKs by ordering rather than by disabling them
  - [x] 4.2 `PendingIdentityDeletion` entity + configuration + migration `AddPendingIdentityDeletions`
  - [x] 4.3 `AccountDeletionService` in `CarTracker.Domain`: collect vehicle ids → transaction inside
        `CreateExecutionStrategy().ExecuteAsync` (vehicles → tokens → reference rows → user → pending row) →
        commit → **then** document bytes → then the identity call
  - [x] 4.4 `IIdentityProviderClient` against Auth0 Management `DELETE /api/v2/users/{id}` with client
        credentials. Treat a 404 as success. Config under `Auth0:Management:`; **unset means the endpoint
        503s and deletes nothing**, following the `Lookup:` NotConfigured precedent
  - [x] 4.5 Retry hosted service for pending rows, registered like `RemindersBackgroundService`
  - [x] 4.6 `AccountEndpoints`: `DELETE /api/account` (body `confirmEmail`, must match; 403 for an
        assistant-token principal), `GET /api/account/summary`. Register in `Program.cs` beside the others
  - [x] 4.7 `GET /api/meta` gains the identity-deletion-configured flag

  > The endpoint requires `confirmEmail` in the body even though the UI already asks for it. The client is not
  > the only possible caller, and an account-deleting `DELETE` that succeeds on an empty body is one mis-wired
  > button away from being catastrophic.

  > **Every refusal lives in the service, not the endpoint** - the confirmation match, the not-configured 503
  > and the account-holder check - for the reason task 6 gives: there is no `CarTracker.WebApi.Tests` project,
  > and the most destructive operation in the app must not be the only untested one. The endpoint keeps the
  > mapping from an outcome to a status code and nothing else.
  > **The assistant-token case answers 401, not 403** (owner decision; `api-spec.md` amended): the Auth0
  > fallback policy refuses a `ct_…` bearer at the door, and admitting the assistant scheme to this group so it
  > could be told 403 would widen the surface in order to improve the wording. The 403 is what the service's
  > own subject check produces - defence in depth rather than a path anything takes.
  > **Vehicles are removed with `RemoveRange`, not `ExecuteDelete`**: `Vehicle` shares its table with four
  > owned blocks, an account holds a handful of vehicles, and the database's own cascades reach the 16 child
  > tables either way. `AccountDeletionTests` asserts the `Restrict` FK still refuses the wrong order, so the
  > ordering is proved load-bearing rather than assumed.

- [x] 5. Export
  - [x] 5.1 Test first: the payload contains no derived key (assert against a list of `IDerivedMetricsService`
        outputs - mpg, cost-per-mile, statuses, totals), contains no token secret, and contains no other
        owner's rows
  - [x] 5.2 `GET /api/account/export` built from `LogQueryService`, `DocumentService.GetLogAsync` and the
        existing vehicle/reference queries, reusing the row DTOs in `CarTracker.Shared/Logs/` so it cannot
        drift from what is stored
  - [x] 5.3 Stream the JSON rather than materialising the whole graph - correct once is cheaper than correct
        later, and several vehicles with years of history is the case that matters
        > **Shipped streaming, and shipped broken; corrected in `3f9f698` (0.13.2).** The technical spec said
        > "`System.Text.Json`'s async writer over a response stream costs nothing extra to write correctly the
        > first time" (`technical-spec.md:209-212`). That is the one sentence in this spec that was wrong, and
        > it cost a release: **`JsonSerializer.Serialize(Utf8JsonWriter, …)` calls `writer.Flush()` when it
        > returns** - synchronously, always, and there is no async overload taking a writer. So a writer
        > pointed at `HttpResponse.Body` writes synchronously on *every* property, and Kestrel refuses the
        > first one with `InvalidOperationException: Synchronous operations are disallowed`. The two awaited
        > `FlushAsync` calls in `AccountExportService` were correct and were never the ones doing the writing.
        >
        > Fixed with a `BufferedOutput` the writer owns, drained to the destination by an awaited
        > `CopyToAsync` at the same two points - **not** `AllowSynchronousIO`, which turns the guard off rather
        > than stopping the write, on the one response shaped like a long transfer. One vehicle's rows sit in
        > the buffer at a time, so the streaming property this task asked for is now the mechanism rather than
        > an aspiration.
        >
        > **Why 5.1's tests were green throughout:** they export to a `MemoryStream`, which permits synchronous
        > writes. No assertion about the *payload* could have caught it at any strength - the gap was the
        > destination. `Export_never_writes_synchronously_to_its_destination` now exports to an
        > `AsyncOnlyStream` that throws the real exception with the real wording, and was checked red against
        > the old code before being kept.
  - [x] 5.4 `Content-Disposition: attachment`, filename carrying the export date; `notes` array stating why no
        computed figures are present and that document bytes are excluded

  > **The wrappers are unwrapped, and two reads were missing.** `TaskLog`, `IssueLog` and `DocumentLog` all
  > carry derived figures beside their rows, so the export takes `TaskLog.Tasks` and reads issues and documents
  > raw instead - `LogQueryService.ListIssuesAsync` and `DocumentService.ListRowsAsync`, with `IssueRowItem`
  > and `DocumentRowItem` in `ExportRowDtos.cs` beside the four the read layer added. `IssueItem.Watch` is the
  > case that made this necessary: it is the linked checks' status *as at the download*, and an export carrying
  > it would present a stale verdict as a stored fact.
  > **The derived-key test walks every property name at every depth** rather than checking known places, so a
  > screen wrapper serialised in by accident fails on its name wherever it appears.
  > **The vehicle profile is the entity, not a projection.** A hand-listed set of ~40 columns ages silently -
  > add a column and the export stops carrying it with nothing to fail. The entity has no navigations, so it is
  > the row and its four owned blocks and nothing else.
  > The endpoint declares no response type: the payload is written straight to the stream, so it has no static
  > shape, and a declared schema would be a second definition of the format maintained by hand.

- [x] 6. The door
  - [x] 6.1 Test first: an email outside the allowlist creates **no** `User` row and returns the
        `signup-not-invited` 403; an empty allowlist admits nobody new
  - [x] 6.2 Allowlist check in `CurrentUserMiddleware.ResolveAuth0UserAsync` before provisioning
        (`Signup:AllowedEmails`, `Signup:AllowedDomains`)
  - [x] 6.3 Replace the `Users.CountAsync() == 1` adoption block (`CurrentUserMiddleware.cs:96-101`) with
        `Ownership:ClaimUnownedVehiclesFor`, default null → no adoption ever
  - [x] 6.4 Document in `.env.example` and the README Quickstart that an **empty allowlist means closed** -
        the opposite is the natural reading and would silently open registration

  > Built as `AccountProvisioner` in `CarTracker.Domain/Accounts/` rather than inline in the middleware: there
  > is no `CarTracker.WebApi.Tests` project, and the assertion worth making is "no row was written", which is a
  > Data test against a real database. The 403 is only how the refusal is reported. The address itself comes
  > from the Auth0 Management API (`IIdentityProviderClient`), because this tenant's access tokens carry no
  > `email` claim - so **an unconfigured Management API is a closed door too**, and the identity-deletion half
  > of task 4 lands on that same interface and credential.

  > **6.4's documentation was necessary and was not sufficient - corrected in `3f9f698` (0.13.2).** The empty
  > allowlist warning went into `.env.example`, `docker-compose.yml` and the README exactly as asked, and the
  > first real deployment still came up with the door shut: `docs/deployment-synology.md` - the file someone
  > actually follows to deploy - listed six `.env` keys and mentioned none of `SIGNUP_ALLOWED_*`,
  > `AUTH0_MANAGEMENT_*` or `OWNERSHIP_CLAIM_UNOWNED_FOR`, and its troubleshooting entry grepped only
  > `Lookup`. Following it end to end produced a deployment whose door had never opened, with nothing looking
  > wrong.
  >
  > Three mechanisms conspired, and all three are now written down there: the NAS runs a **copy** of
  > `deploy/docker-compose.yml` that nothing keeps current, a Container Manager **Project** keeps a third copy
  > inside DSM, and **Watchtower recreates from the running container's spec** rather than from either - so a
  > container can take a brand-new image while carrying an environment assembled before the keys in it
  > existed. The lesson for this task: documenting a setting in the file that *defines* it does not reach the
  > person following the file that *deploys* it.

- [x] 7. Client
  - [x] 7.1 Tests first: the panel states the counts before arming; the destructive button stays disabled
        until the email matches exactly; the panel degrades to export-only when `meta` reports deletion
        unconfigured; axe passes in both themes
  - [x] 7.2 `DangerZonePanel` in `screens/settings/`, reusing `Sheet`, `Field`'s `error` prop and
        `reportApiError`. **Not `ConfirmButton`** - its two-step is calibrated for one log row
  - [x] 7.3 Export download above deletion in the same panel
  - [x] 7.4 `AuthGate` gains the signed-in-but-refused state: a short "not yet invited" panel with sign-out,
        neither the app nor `LandingPage`
        > **Corrected in `0cbef01` (0.13.1): the panel asserted one refusal for three.** `AccountProvisioner`
        > writes three different sentences because they are three different things to do next - nobody could
        > read your address, nobody has proved it is yours, or it is yours and not on the list - and its own
        > comment says a generic "not invited" sends someone who needs to click a link in their inbox off to
        > ask for an invitation they already have. `AuthGate` detected the 403, discarded the detail the error
        > already carried, and asserted the third whichever had fired.
        >
        > What made it actively misleading is that the panel names the address from the **ID token**, which the
        > browser has and the API does not. So a deployment with no Management credential - one that could not
        > resolve any address at all - told its owner that a specific named address had not been invited. Both
        > halves wrong, and the named address is the half someone would trust. The panel now renders the
        > server's sentence and keeps naming the signed-in address, which is still worth showing: signing up
        > with a different address from the one the invitation went to is the commonest real cause.
  - [x] 7.5 `LandingPage` sign-up copy says access is by invitation - and still passes its jargon guard
        (shipped with task 6, alongside the door itself; the guard's three new terms are its record)
  - [x] 7.6 On successful deletion, call Auth0 `logout()`; never attempt to re-render the app

  > **The refusal needed a shape the client could see.** `ApiError` carried status and message only, and a
  > not-invited 403 is indistinguishable from every other 403 at that level - so it now carries the RFC 9457
  > `type`, which is the one thing that tells them apart, and `queries.ts` reads it in `isNotInvited`. That
  > file's retry short-circuit gained 403 beside 401/404, and the access check itself retries **nothing**: only
  > one answer changes what renders, and the others let the app through, so a retry would only hold everyone on
  > a splash through two backoffs to reach a conclusion already made.
  > **`AuthGate` fails open on anything but the invitation refusal.** The probe is `GET /api/meta/authenticated`
  > - it carries no data and exists to prove a credential is accepted, and the refusal is written by
  > `CurrentUserMiddleware` before any handler runs, so any protected route would answer the same. A 500 or a
  > dropped connection renders the app: a gate that locked everyone out whenever it could not reach the server
  > would turn a transient outage into a lockout.
  > **The export needed the server's filename, so `apiBlob` split.** An object URL carries no
  > `Content-Disposition` - a `blob:` href ignores it - so the save name has to be read off the response and put
  > on the anchor by hand. `apiDownload` returns `{ blob, filename }` and `apiBlob` is now two lines over it, so
  > the documents screen is untouched. Deriving the name client-side would have been a second definition of a
  > format the server owns, and the two would differ by a day for anyone downloading late in the evening west
  > of UTC.
  > **Two primitives were missing and are now there:** `.btn` had no `:disabled` rule at all - nothing had ever
  > disabled one - so an inert destructive button would have painted exactly like a live one, and `Btn` gained
  > `disabled` plus a `danger` variant (`.btn.ghost.danger`, the same `--due` treatment `.armed` already
  > borrows) for the one destructive action with no two-step to arm it.

- [x] 8. Record the decisions and close the gates
  - [x] 8.1 DEC in `docs/product/decisions.md`: reference lists keyed `(OwnerId, Name)` with the six FK
        constraints dropped, reversing `roadmap.md:206`'s surrogate-id shape. Carry the argument - the FK's
        only behaviours are a `SetNull` the editor exists to prevent, a `Restrict` it duplicates, and a
        `Cascade` it overrides
        > **DEC-018**, drafted during Foundations and corrected here against what was built. Three sections
        > added: the guard as built (it covers the four *create* inserts only, and the bypass hazard is closed
        > by naming the whole primary key on the six reference-table deletes rather than by an exception, since
        > refusing under bypass would make every existing Data test unrunnable); the correlated subquery held,
        > so no materialised-`Contains` fallback was needed, and **fifteen** statements are scoped rather than
        > eleven because five counts needed it too; and the account half - the export's no-derived-figure rule,
        > `Auth0:Management:` gating deletion as a 503-that-deletes-nothing, and the invitation refusal being
        > the only RFC 9457 `type` this app reads. One negative consequence was missing: `FuelEntryFactory` and
        > `VehiclePurchaseMirror` write `"Fuel"`/`"Purchase"` by exact name, and the `Restrict` that backstopped
        > them is gone - the guarantee now rests on provisioning and the rename lock, and on no database check.
  - [x] 8.2 DEC amendment retiring DEC-016, and noting that a refused sign-up still leaves an Auth0 identity
        > Two corrections to the drafted amendment: the allowlist is checked in `AccountProvisioner` (domain),
        > **not** `CurrentUserMiddleware` - moved so "a refused address creates no `User` row" is a Data test,
        > there being no `CarTracker.WebApi.Tests` project. And the amendment said nothing about **email**: the
        > access token carries no address, so the allowlist had nothing to match. The Management API resolves it
        > at provisioning and backfills rows where `Email == ExternalId`, which makes one credential gate two
        > things - unset closes sign-up *and* refuses deletion.
  - [x] 8.3 Update `roadmap.md:198-213`: gate 1 closed (and restated - it was a cross-tenant write, not a
        visibility problem), gate 3 closed, **HTTPS still open**. Add Art. 15/17/20 to the record so the next
        reader does not rediscover them
        > Gates 1 and 3 closed, HTTPS restated as *the only* remaining gate. Three other stale claims went with
        > them: Phase 4.5's last open item (`roadmap.md:140`) still described the surrogate-id shape; Phase 5's
        > export line (`:151`) read "not started"; and the gate itself never named `ExpenseCategory`. Art. 5(1)
        > (c)/(e) added beside 15/17/20 as the pair with no endpoint and no plan - retention is a decision
        > nobody has made, and it becomes a blocker the day this holds a stranger's data. The export line is
        > `[~]`, not `[x]`: JSON ships, and "parity with the old workflow as a safety net" meant a spreadsheet.
  - [x] 8.4 Update `CLAUDE.md`'s state-of-play with the new test counts and the per-owner reference lists
        > New entry in the house voice, leading with the cross-tenant write and the red test's NULL finding.
        > Counts at the top updated to the measured **272 Domain, 204 Data, 537 front-end**. Three older
        > passages were left saying things that are now false and are corrected in place: Phase 4.5's
        > "Deferred (its own next migration)" block, the three-gates paragraph under the landing page, and -
        > sharpest - the "Four bugs, one cause" item asserting `Garage`/`WashLocation` **are foreign keys**,
        > which is exactly the kind of stale certainty that section exists to prevent.
  - [x] 8.5 Regenerate `api-contract/` and the typed client; confirm the CI staleness gate passes
        > `dotnet build` then `npm run gen:api`; both re-run and verified byte-identical, so the gate is
        > satisfied. Purely additive: 138 added lines, **zero removed** - three paths (`/api/account`,
        > `/api/account/summary`, `/api/account/export`), `AccountSummary`, `DeleteAccountRequest`, and
        > `meta.identityDeletionConfigured`. The export declares no response schema, deliberately.
  - [x] 8.6 Bump `VERSION` a **minor**, `git add VERSION` **into the feature commit** - CI publishes nothing
        when it is unchanged
        > `VERSION` written 0.12.0 → **0.13.0**. **Not staged and not committed** - this run was told not to
        > commit, so whoever makes the feature commit must `git add VERSION` into it rather than after it.

- [~] 9. Verify end to end with two real accounts
  - [x] 9.1 `dotnet build`, `dotnet test` (needs Docker), `npm --prefix src/CarTracker.WebApp run test`
        > Green at **273 Domain, 216 Data, 539 front-end** (2026-08-14, at 0.13.2).
  - [~] 9.2 `dotnet run --project src/CarTracker.AppHost`; sign in as two different Auth0 accounts
        > Two accounts exist and both sign in - but on the **deployed NAS stack**, not a local AppHost, and
        > never side by side in the way 9.3 needs.
  - [ ] 9.3 Both create a garage named "K & P Motors" and attach it to a service record. A renames theirs.
        **B's record is unchanged.** Repeat for a wash location and a category rename
  - [ ] 9.4 B's `GET /api/reference/garages` lists only B's, with B's counts
  - [ ] 9.5 Export as A: contains A's vehicles, none of B's, no derived figures, no token secrets
  - [~] 9.6 Delete A: rows gone, `{Documents:RootPath}/{vehicleId}/` gone from disk, B untouched, the Auth0
        identity gone or a `pending_identity_deletions` row present
        > Exercised on the live deployment 2026-08-14 and reported working, once `delete:users` was granted.
        > The *endpoint* is verified; the four specific assertions above - document folder gone from disk, the
        > second account untouched, the Auth0 identity actually removed - were not checked individually.
  - [ ] 9.7 With `Auth0:Management:` unset, the delete endpoint 503s and **deletes nothing** - check the
        database afterwards rather than trusting the status code
        > The unset *state* was observed at length (`/api/meta` reported `identityDeletionConfigured: false`
        > and the client correctly hid the control), but `DELETE` was never called while it held, so the
        > "deletes nothing" half is still unproven outside `AccountDeletionTests`.
  - [ ] 9.8 An email outside the allowlist gets the not-invited state and creates no `User` row

  > **This task never ran, and that is the finding - not a formality left over.** Both defects that escaped
  > into production sit inside it. **9.5 is the export**, which answered `500 Synchronous operations are
  > disallowed` on the first request any real deployment made to it, and had done since it shipped - fixed in
  > `3f9f698`. **9.8 is the invitation path**, which was refusing an invited, verified address because the
  > Management credential had never reached the container, with no signal outside a per-refusal log line -
  > diagnosed and given a boot-time posture line in the same commit.
  >
  > Neither is a coverage gap in the suite: 9.5's payload assertions all pass against a `MemoryStream`, which
  > accepts the synchronous write Kestrel refuses, and 9.8's "creates no `User` row" is a Data test that was
  > green throughout. **Both are gaps only a real deployment could show**, which is precisely the job this task
  > exists to do and precisely the argument for not skipping it because the suite is green.
  >
  > 9.3 and 9.4 remain the genuinely unverified isolation claims. They are covered by
  > `ReferenceListCrossTenantTests` against a real database, which is strong - but the whole point of task 1
  > was that the test found a failure mode (`vehicles.default_garage` going NULL) nobody had predicted, and
  > two accounts on a live stack is the next place that kind of surprise lives.
