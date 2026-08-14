# Database Schema

This is the database schema implementation for the spec detailed in @docs/specs/2026-08-11-pre-public-release-gates/spec.md

Two migrations: `AddPerOwnerReferenceLists` and `AddPendingIdentityDeletions`.

## Migration 1 — `AddPerOwnerReferenceLists`

### The three reference tables gain an owner and a composite key

```sql
ALTER TABLE garages          ADD COLUMN owner_id integer NULL REFERENCES users (id) ON DELETE CASCADE;
ALTER TABLE wash_locations   ADD COLUMN owner_id integer NULL REFERENCES users (id) ON DELETE CASCADE;
ALTER TABLE expense_categories ADD COLUMN owner_id integer NULL REFERENCES users (id) ON DELETE CASCADE;

-- after the backfill below, owner_id is set NOT NULL and the primary keys are replaced
ALTER TABLE garages            DROP CONSTRAINT pk_garages,            ADD PRIMARY KEY (owner_id, name);
ALTER TABLE wash_locations     DROP CONSTRAINT pk_wash_locations,     ADD PRIMARY KEY (owner_id, name);
ALTER TABLE expense_categories DROP CONSTRAINT pk_expense_categories, ADD PRIMARY KEY (owner_id, name);
```

`ON DELETE CASCADE` to `users` is deliberate and differs from `Vehicle.OwnerId`/`AssistantToken.OwnerId`,
which are `Restrict`. Those are `Restrict` because a vehicle is data whose deletion should be an explicit act;
a reference row is a list entry that cannot outlive its list. It also means account deletion does not have to
remember them — though `AccountDeletionService` deletes them explicitly anyway, because relying on a cascade
to do something you intended is how the document bytes got forgotten.

Configured on `GarageConfiguration`, `WashLocationConfiguration`, `ExpenseCategoryConfiguration` with
`HasKey(x => new { x.OwnerId, x.Name })`.

### Six foreign-key constraints dropped

| Table | Column | Points at | Current behaviour | Declared in |
|---|---|---|---|---|
| `service_records` | `garage` | `garages` | `SetNull` | `ServiceRecordConfiguration.cs:31` |
| `maintenance_tasks` | `assigned_garage` | `garages` | `SetNull` | `MaintenanceTaskConfiguration.cs:35` |
| `vehicles` | `default_garage` | `garages` | `SetNull` | `VehicleConfiguration.cs:120-123` |
| `wash_entries` | `location` | `wash_locations` | `SetNull` | `WashEntryConfiguration.cs:26` |
| `expense_entries` | `category` | `expense_categories` | `Restrict` | `ExpenseEntryConfiguration.cs:30-33` |
| `budget_group_categories` | `category` | `expense_categories` | `Cascade` | `BudgetGroupCategoryConfiguration.cs:17-20` |

**The six columns themselves do not change.** They stay `varchar(80)` / `varchar(24)`, carrying the same
values. Only the constraint goes. This is the entire reason for choosing this shape over surrogate ids.

The roadmap's tally said four columns. It missed `expense_categories` entirely (two more FKs), which is the
same oversight that left `ExpenseCategory` out of the gate.

### The seed stops being seed data

`ExpenseCategoryConfiguration.HasData(SystemCategories)` is removed — seeded rows have no owner and there is
no sensible owner to invent for them. The `SystemCategories` array stays exactly as it is and becomes the
source `CurrentUserMiddleware` provisions from, per user, at account creation.

This means the 13 categories move from being a migration artefact to being per-account data, and a new
account's categories are created in the same `SaveChangesAsync` as its `User` row, so a user cannot exist
without them.

### Backfill

**Every step is a precondition of the next, and the order above is not it.** As built
(`20260813230044_AddPerOwnerReferenceLists`, hand-written — the scaffolded `Up()` was thrown away):

0. **Refuse to run if more than one row exists in `users`**, with a message saying why.
1. Drop the six child foreign keys, or the copies and deletes below break them mid-flight.
2. Drop the three single-column primary keys, or a per-user copy collides on the name.
3. Add `owner_id` **nullable** — the existing rows have no owner yet, and EF's generated
   `NOT NULL DEFAULT 0` names a user that does not exist.
4. For each row in `users`, insert a copy of every `garages`, `wash_locations` and `expense_categories` row
   with `owner_id` set to that user.
5. Delete the ownerless originals (`WHERE owner_id IS NULL`).
6. `SET NOT NULL`.
7. Add the three composite primary keys, then the three `owner_id` foreign keys to `users`.

EF's generated `DeleteData` for the 13 seeded categories must go: it is keyed on the old primary key, so it
would eat the per-user copies step 4 has just made. `Down()` throws `NotSupportedException` — collapsing
per-account lists back into one global list would have to pick a winner per name.

**Child rows need no change at all.** They reference by name, and the constraint that would have objected is
gone by step 1. This is the property that makes the migration small: no data in any log table is touched.

**Why step 0.** The copy hands every list to every account, which is right for the one existing deployment and
a data leak on any other — a garage row carries a contact, an address and free-text notes. Nothing recorded
which account typed a global row, so there is no way to attribute one; the migration refuses rather than
guesses. This replaces "verify against a restored dump", which is not something anyone can execute in CI, with
a precondition the database enforces. On a deployment with no users — a fresh checkout, CI — steps 4 and 5 are
a no-op and the tables end up empty, which is correct: reference lists are created as used.

`PerOwnerReferenceListBackfillTests` migrates to the migration before this one, seeds through the old schema,
and migrates the rest of the way: once with one account (every row owned, every child column unmoved) and once
with two (aborted, nothing touched, the migration unapplied).

## Migration 2 — `AddPendingIdentityDeletions`

```sql
CREATE TABLE pending_identity_deletions (
  id           integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
  external_id  varchar(128) NOT NULL,
  requested_at timestamptz  NOT NULL,
  attempts     integer      NOT NULL DEFAULT 0,
  last_error   text         NULL,

  CONSTRAINT ck_pending_identity_deletions_last_error CHECK (last_error <> '')
);

CREATE UNIQUE INDEX ix_pending_identity_deletions_external_id
  ON pending_identity_deletions (external_id);
```

No FK to `users` — the row's whole purpose is to outlive the user it refers to. `external_id` matches
`User.ExternalId`'s `varchar(128)` and is unique, so a retry cannot enqueue a second attempt for the same
identity.

`PendingIdentityDeletion` is not `IAuditable`: like `User` and the reference tables, it is an operational row
rather than one of README §6's mutable domain entities. `RequestedAt` is stamped from `TimeProvider`.

## Rationale

### Why the FK constraints can go

The instinct is that dropping six foreign keys weakens the schema. Read what they actually do:

- **`SetNull` on the four garage/wash FKs is a hazard, not a safeguard.** CLAUDE.md records it plainly: a
  delete "would *silently blank* referencing rows unless guarded", which is precisely why
  `ReferenceListEditor` was written to block-with-a-count or re-home instead. The FK's runtime behaviour is
  the outcome the application layer exists to prevent. Removing it removes a trap that is already being
  worked around.
- **`Restrict` on `expense_entries.category` duplicates a check the editor already performs.**
  `DeleteCategoryAsync` counts references and refuses before the database would.
- **`Cascade` on `budget_group_categories.category` is the sharpest of the six** — it silently deletes budget
  group memberships when a category goes. The editor re-homes them explicitly
  (`ReferenceListEditor.cs:328`), so the cascade only fires on a path the editor does not take.
- **Rename cascade never used the FK.** There is no `ON UPDATE CASCADE` anywhere; `ReferenceListEditor`
  hand-writes insert-new → repoint → drop-old in a transaction, because changing a primary key cannot be an
  in-place update.

So every behaviour that survives is application code, and every behaviour that dies is one the application
code was written to override.

### Why not surrogate ids

`roadmap.md:206` records the shape as "surrogate id + `OwnerId`, repoint the four FK columns, backfill". That
was decided before anyone counted what those columns feed:

- `ServiceRecord.Garage` and `WashEntry.Location` are rendered directly in `<DataTable>` columns.
- Both are in `useTableView`'s `search.fields` — free-text search added 2026-08-09 matches against them as
  strings.
- MCP tools (`add_service`, `log_wash`, `update_vehicle_profile`) accept a garage or location **by name**.
- `Vehicle.DefaultGarage` appears in `VehicleSummary` and the vehicle-info screen.

Every one of those needs a join to render a name from an id, the search fields change shape, and the contract
diff stops being additive. The composite-key alternative — keeping a real FK on `(OwnerId, Name)` — requires
denormalising `owner_id` onto all six child tables so the FK has something to point at, which puts the owner
in two places per row and creates a new class of inconsistency.

`(OwnerId, Name)` with no constraint costs one dropped guarantee that was never load-bearing, and changes no
DTO, no search field, no MCP argument and no rendered column.

**This reverses a recorded decision and therefore needs a DEC** in `docs/product/decisions.md`, with
`roadmap.md:203-207` updated to match.
