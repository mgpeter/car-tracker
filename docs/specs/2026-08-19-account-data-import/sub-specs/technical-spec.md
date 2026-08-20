# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-08-19-account-data-import/spec.md

The endpoints are in @docs/specs/2026-08-19-account-data-import/sub-specs/api-spec.md

---

## The decision everything else follows from: rows are inserted, not replayed

The obvious implementation is to feed the file through the write paths - `FuelEntryFactory`, `ServiceRecordFactory`,
`ExpenseService`, `CheckSetAdder` - so that every invariant is enforced by the code that already enforces it.
**That is wrong here, and the reason is the mirrors.**

`FuelEntryFactory.CreateAsync` writes three rows: the fill, a `MileageReading` stamped `MileageOrigin.Fuel`,
and a mirrored `ExpenseEntry` in the `Fuel` category. **The export contains all three**, because they are three
stored rows. Replaying the fill would produce a second reading and a second expense on top of the ones the file
already carries. The same is true of `ServiceRecordFactory` (record, reading, mirrored expense),
`VehiclePurchaseMirror` (the `IsVehiclePurchase` expense), the wash mirror and the equipment mirror. An import
built on the factories would inflate every money figure on the dashboard by roughly the value of its own
mirrors, silently, and the workbook's doubled-litres defect is what that looks like from the outside.

So the import writes rows directly through the `DbContext`, and the invariants the factories would have
enforced become **assertions on the way in** rather than side effects. That trade is stated plainly because it
is the risk in this spec: the import is a second write path into tables that had one, and it earns that by
validating before it writes and by being tested against the export it consumes.

The same reasoning rules out `VehicleFactory.CreateAsync` for the vehicle itself: it creates the opening
`MileageReading`, applies a `CheckTemplate` and calls the purchase mirror. All three are in the file.

## Ordering, and the id map

Nothing in the file can be trusted to arrive in a usable order, and every id in it belongs to another database.
One `ImportIdMap` (old id to new id, per table) is threaded through the whole commit, and the insert order is
the foreign-key order:

1. **Reference lists** - garages, wash locations, expense categories. Keyed `(OwnerId, Name)` since DEC-018, so
   these are matched by name against the account's own and inserted only when absent. **Never updated:** a
   file's garage that names an address different from yours leaves yours alone. Letting an import rewrite the
   account's own reference data is the cross-tenant write DEC-018 closed, arriving through the front door.
   Inserts go through `ReferenceWriter` so `ReferenceOwner.Require` still guards them.
2. **Vehicle** - one row, `OwnerId` set to the importing account, registration possibly rewritten (below).
3. **Check definitions**, then **check logs** (`CheckDefinitionId` remapped).
4. **Service records**, then **tasks** (`ServiceRecordId` remapped, may be null).
5. **Fuel entries**, **equipment**, **wash entries** - all three are mirror *sources*.
6. **Expenses** - after every mirror source exists, because `FuelEntryId`, `ServiceRecordId`,
   `EquipmentItemId` and `WashEntryId` all remap. At most one row per vehicle may carry `IsVehiclePurchase`;
   the partial unique index enforces it and the validation pass reports it as a named error rather than as a
   `DbUpdateException`.
7. **Mileage readings** - inserted verbatim, `MileageOrigin` preserved. A reading whose origin is `Fuel` or
   `Service` is *not* re-derived from the fill or the record: it is a row, and the file has it.
8. **Tyre readings**.
9. **Issues**, then **issue watch checks** (both `IssueId` and `CheckDefinitionId` remapped). The same-vehicle
   invariant `IssueService` enforces on the write path is asserted here, because the join reaches across two
   tables and Postgres has no constraint for it.
10. **Budget groups** and their category memberships (by name, against the merged reference list).

**Then, after the rows land:** `AnomalyScanner` runs over each imported vehicle. Flags are not imported. This
is a deliberate loss - a flag the exporting owner had Accepted or Dismissed comes back Open - and it is the
right way round: an anomaly is a statement about the data in *this* database, its `Detail` is JSON embedding
ids and values from another one, and an imported `Corrected` flag would be an assertion nothing here can
check. The import report says how many flags were raised.

## Provenance costs nothing

`EntrySource.Import = 3` **already exists** in `src/CarTracker.Shared/EntrySource.cs` and in every check
constraint that references it, left behind when DEC-008 deleted the importer. Every imported row that carries
a source is stamped with it. **No migration, and no schema change anywhere in this spec.**

The vehicle's `Notes` gains a line recording where it came from - the source registration and the file's
`exportedAt` - which is the only place the original plate survives when the registration has been rewritten.

## Collisions: rename, proposed and editable

`ix_vehicles_registration` is unique on `(OwnerId, upper(replace(registration, ' ', '')))`, so a registration
the account already owns cannot be inserted as-is. The chosen behaviour is to import it under a modified
registration.

- The server proposes `-2`, incrementing to `-3` and beyond until free. `BT53 AKJ` becomes `BT53 AKJ-2`, which
  normalises to `BT53AKJ-2` and is distinct, URL-safe and fits the slug the router already builds.
- `Registration` is `varchar(16)` and so is the computed normalised column, so the base is truncated to make
  room for the suffix rather than overflowing.
- The preview shows the proposal per vehicle and the commit accepts an override, so the person importing
  chooses the plate rather than being handed one.
- **The override is re-checked at commit**, not trusted from the preview. Minutes pass between the two calls
  and a vehicle can be added in them.

> **The cost, stated rather than buried.** A registration is a real-world identifier, and a rewritten one is
> fictional. `GET /api/vehicles/lookup/{reg}` will not resolve it, and an assistant asked about "BT53 AKJ"
> now has two cars to choose between, one of which is not a car anybody owns. The mitigations are that the
> plate is editable before the write, and that the vehicle's notes record what it was cloned from. This is
> the sharpest edge in the spec and the preview exists largely because of it.

> **Importing the same file twice now silently succeeds**, producing `-2` and then `-3` copies of everything.
> Refusing on collision would have made the uniqueness index a free idempotency guard; renaming gives that up.
> The preview compensates by leading with the count: "3 of 3 vehicles already exist and will be imported as
> copies" is the sentence that stops an accidental second import, and it must be the first thing on the panel
> rather than a detail beside each row.

## Reading the file

- **Deserialised into the export's own DTOs** - `ExpenseItem`, `FuelEntryItem`, `ServiceRecordItem`,
  `CheckLogItem`, `IssueRowItem`, `IssueWatchLinkItem`, `BudgetGroupItem`, `DocumentRowItem` and the rest from
  `CarTracker.Shared/Logs/`, with `AccountExportService.Json` as the options. One definition of the format,
  read from both ends, which is the property `CatalogueDriftTests` protects for the tool catalogue.
- **The vehicle profile deserialises into `Vehicle` itself**, because the export writes the entity rather than
  a projection - deliberately, so that a new column cannot be silently dropped. The import inherits that: a
  column added to `Vehicle` travels both ways with no code change here. `Id`, `OwnerId` and the computed
  normalised registration are overwritten on the way in.
- **Unknown properties are ignored, missing required ones are refused.** `System.Text.Json` fills an absent
  member with `default` and says nothing, which would turn a truncated file into a garage full of zeroed
  odometers. Required fields are validated explicitly before anything is written.
- **`schemaVersion` is reported, not enforced.** It carries the app `VERSION` that wrote the file. Refusing a
  mismatch would break every import on every release; the preview states the version and warns when the file
  is newer than the running app, since fields it added will be dropped.
- **25 MB cap, enforced while reading** rather than from a `Content-Length` header - the rule
  `DocumentEndpoints` already follows for the one other upload in the app.

## Transaction and failure

The commit runs inside `Database.CreateExecutionStrategy().ExecuteAsync(...)`, because
`EnrichNpgsqlDbContext` installs a retrying strategy that refuses a user-initiated transaction otherwise. This
is the trap CLAUDE.md records as passing 41 tests and throwing on the first real request, and
`AccountDeletionService` is the shape to copy.

**One transaction for the whole import.** A half-imported garage that looks complete is worse than a refusal:
the vehicle would be there, its fuel log would be there, and its expenses would not, so every money figure
would be wrong in a way nothing flags.

Validation happens **before** the transaction opens, so the common failures - a bad file, a collision, a
mirror pointing at a row that is not in the file - are reported without a write ever being attempted.

## The pending preview

An opaque server-held id, keyed to the owner, holding the parsed payload with a short expiry. `IMemoryCache`,
following `PendingWriteStore` from the chat, including its two rules: **the commit request carries no
payload** - a foreign or expired id is refused identically, and re-sending the file with the commit would
validate the request against itself - and the id is unguessable rather than sequential.

`chat_usage` needed a *table* because Watchtower recreates the container minutes after every release and an
in-memory counter would hand out a fresh daily allowance. A lost preview costs a re-upload, so memory is the
right store here, and the difference between the two cases is worth stating so the next reader does not copy
the wrong precedent.

## Ownership

Everything is written through the request's owner-pinned `DbContext`, so the global query filter and
`ReferenceOwner.Require` apply unchanged. The importing account's id comes from `ICurrentUserAccessor` and
never from the file. The `account` block is shown in the preview as provenance and written nowhere.

An `AssistantToken` principal is refused at the door by the fallback policy, as it is for account deletion and
export, and `api-spec.md` says so.

## The front end

A section in `/account` beside the export, since import is the export's other half and the account screen is
where per-account things live (the 2026-08-15 split).

- A file input, then a preview panel: the source account and export date, the vehicle list with row counts and
  the proposed registration per vehicle, an editable registration field on each colliding one, and the
  headline counts of what will be created, matched and skipped.
- `Field`'s `error` prop and `reportApiError` for the refusals, which is what every other sheet does.
- On success: invalidate **every** query, the rule the chat's confirmed write already follows, because which
  screens went stale depends on what the file contained.
- The panel renders only when the API reports the endpoint is available, the `meta.chatConfigured` polarity.
  There is no configuration behind import, so this is not strictly needed - but a preview that has expired
  must degrade to "upload it again", not to a dead button.

## Testing

- **The round trip is the headline.** Export account A, import into empty account B, export B, and compare the
  two payloads with ids, timestamps and the `account` block normalised away. It is the only test that fails
  when a table is forgotten, because every other test asserts on the tables somebody remembered.
- **A second import into the same account** produces a second complete vehicle with a rewritten registration,
  and the first vehicle's rows are untouched.
- **Isolation**: importing does not read or write any other account's rows, built the way
  `ReferenceListCrossTenantTests` builds its two owners - pinned accessors and `TestOwner.As`, never a
  `BypassOwnership` context, which would make the test a false green.
- **Mirror fidelity**: after an import, the expense count equals the file's expense count. A test that asserts
  "no mirror fired twice" is the regression test for the central decision above.
- **Derived equality**: `IDerivedMetricsService` over the imported vehicle returns the same figures as over
  the source - the strongest statement that the clone is faithful, and it costs one assertion.
- **Refusals write nothing**: a payload truncated mid-file, one whose expense names a `fuelEntryId` absent from
  it, and one with two `isVehiclePurchase` rows, each leaving the database unchanged.
- Data tests against real PostgreSQL through Testcontainers, per the house rule. The in-memory provider would
  not enforce the partial unique index this spec leans on.
