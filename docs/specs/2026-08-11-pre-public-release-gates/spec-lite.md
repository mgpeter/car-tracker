# Spec Summary (Lite)

Everything that must be true before a stranger can sign up: reference lists that belong to their owner, an
account you can delete, an export you can take with you, and a door that stays shut until then.

**The recorded gate understates itself.** `Garage`/`WashLocation` having no `OwnerId` is described on the
roadmap as one user being able to rename another's data. The mechanism is a cross-tenant *write*:
`ReferenceListEditor` does rename and re-home as `ExecuteUpdateAsync` statements matching on a **name** over
`ServiceRecords`, `MaintenanceTasks`, `WashEntries`, `ExpenseEntries` and `BudgetGroupCategories` - and Phase
4.5's isolation is one query filter on `Vehicle`, which works only because everything else is reached through
an already-scoped vehicle id. These statements are not. So two accounts holding a garage of the same name is
all it takes: a rename by one rewrites the other's service records. `ExpenseCategory` has the same shape, is
in no gate, and additionally leaks - `CountCategoryReferencesAsync` aggregates reference counts across every
account, so the settings screen already reports other people's usage as yours.

**Shape: `(OwnerId, Name)`, FK constraints dropped** - six of them, one more than the roadmap's tally, because
`BudgetGroupCategory.Category` is a cascading FK to `ExpenseCategory` that nothing had counted. This reverses
`roadmap.md:206`'s surrogate-id decision and needs a DEC. The reason: the six child columns are `varchar(80)`
natural-key FKs that are rendered directly in DTOs, matched by `useTableView`'s free-text search, and accepted
by name in MCP tool arguments - surrogate ids force a join into every one of those and make the contract diff
breaking. What is given up is `SetNull` on delete, which is the behaviour `ReferenceListEditor` exists to
*prevent* (CLAUDE.md: it would "silently blank" referencing rows), and rename-cascade, which that class
already implements in application code. Nothing actually relied upon is lost. Isolation then comes from
query filters mirroring the `Vehicle` one, and the cascades are scoped by a `context.Vehicles.Any(...)`
subquery that inherits that filter - owner-scoped by construction, not by a threaded ownerId.

**Deletion order is forced by the schema**, not chosen: `Vehicle.OwnerId` and `AssistantToken.OwnerId` are
both `DeleteBehavior.Restrict`, so the `User` row cannot go first. Vehicles (13 child tables cascade, 3 more
indirectly), then tokens (audits cascade), then reference rows, then the user - all inside
`CreateExecutionStrategy().ExecuteAsync`, because Aspire's enrichment refuses user-initiated transactions
outside it. **Document bytes come after the commit**, deliberately: `DocumentConfiguration.cs:28` cascades the
rows and nothing removes the files, and orphaned bytes are harmless where rows pointing at deleted files are
not. The Auth0 identity goes last, and a failure is *recorded* in `pending_identity_deletions` rather than
logged - a surviving identity means re-login provisions a fresh empty account, which is harmless but is not
erasure, and that distinction must not be silent.

**Export is raw rows only.** Nothing `IDerivedMetricsService` computes appears in it: derived figures are
recomputable by definition, and shipping them in an export is the stored-derived-value mistake this project
exists to correct. Document metadata is in, bytes are out, and the payload says so.

**The door**: an allowlist checked in `CurrentUserMiddleware.ResolveAuth0UserAsync` - the only place a local
account is created - refusing to provision before a `User` row exists. An empty allowlist means closed, not
open. DEC-016's `Users.CountAsync() == 1` adoption block becomes an explicit external id, default null.

Additive contract; two migrations. HTTPS remains the one gate this spec does not close.
