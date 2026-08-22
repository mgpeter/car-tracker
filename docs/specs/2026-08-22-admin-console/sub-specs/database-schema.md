# Database Schema

This is the database schema implementation for the spec detailed in
@docs/specs/2026-08-22-admin-console/spec.md

One migration, two nullable columns on one table, no backfill and no data change. Nothing else in the schema
moves: the admin surface reads tables that already exist, and the only reason it needs the database at all is
that two facts it wants have nowhere to live.

## Migration 1 - `AddAdminObservability`

```sql
ALTER TABLE users ADD COLUMN plan_override integer NULL;
ALTER TABLE users ADD COLUMN last_seen_at timestamptz NULL;
```

`Down()` drops both. Unlike `AddPerOwnerReferenceLists` this migration is reversible and safe to reverse: it
adds two nullable columns that nothing else references, so dropping them loses the overrides and the
observations and breaks no foreign key, no index and no constraint.

EF will generate this correctly from the entity changes, so unlike `AddPerOwnerReferenceLists` there is no
reason to hand-write it. Generate it, then read it - the check is that it contains exactly two `AddColumn`
calls and no `DeleteData`, `UpdateData` or `Sql`.

### `users.plan_override`

`AccountPlan?` on the entity, `integer NULL` in the column.

```csharp
// User.cs
public AccountPlan? PlanOverride { get; set; }

// UserConfiguration.cs
builder.Property(u => u.PlanOverride).HasColumnType("integer");
```

**Nullable, and the null is the whole design.** Null means "no administrator has decided anything about this
account", which is different from `Free` and must stay different: an account with no override falls through to
the comp list and can be promoted by a configuration change, while an account overridden to `Free` is pinned
below whatever the comp list says. A boolean `IsPro` could not express the second, and a non-nullable
`AccountPlan` defaulting to `Free` would silently pin every existing account the day the column landed.

**Stored as the enum's integer, not its name.** `AccountPlan` is `Free = 0, Pro = 1` and the neighbouring
plan machinery is all in-memory, so there is no existing string-storage convention for this enum to match.
Integer keeps the column narrow and the mapping is EF's default.

**No check constraint.** The neighbouring `ck_*_source` constraints exist because those columns are written by
several paths including a data importer. This one is written by exactly one endpoint, from a parsed enum, and
a constraint enumerating two values would need editing on the day a third tier is added - which is the day
somebody is already editing several things and would find it by test rather than by constraint violation.

**No index.** It is read as part of a single-row primary-key lookup on the entitlement path, and scanned in
full on the admin list, which is at most 500 rows.

### `users.last_seen_at`

`DateTimeOffset?` on the entity, `timestamptz NULL` in the column, matching `CreatedAt` at
`UserConfiguration.cs:22`.

```csharp
// User.cs
public DateTimeOffset? LastSeenAt { get; set; }

// UserConfiguration.cs
builder.Property(u => u.LastSeenAt).HasColumnType("timestamptz");
```

**Nullable because null is honest.** Every row that exists when this migration runs has never been observed,
and there is no defensible value to invent for them. `CreatedAt` would claim an observation that did not
happen; the epoch would sort them into a block at the bottom of a "least recently seen" ordering as though
they were dormant rather than unmeasured. The admin list renders null as "never seen since this shipped",
which is true and is a state that empties itself over the first few days of any deployment taking the release.

**`timestamptz`, not a `date`.** "Did they come back" is answered by a date, but "are they in the app right
now" is answered only by a time, and it is the question an operator asks while a tester is on the phone. The
column costs the same either way.

**No index.** Sorting 500 rows in memory does not need one, and adding an index to a column written on the
request path is a cost on every write for a read that happens when an administrator opens a screen.

### Backfill

**None, deliberately, for both columns.** This is worth stating because the two preceding migrations both
backfilled and one of them refused to run without doing so:

- `AddPerOwnerReferenceLists` had to copy shared rows per user and asserted `users` count <= 1 first, because
  the copy is only unambiguous while there is one account.
- `AddAccountPlans` set `email_verified = TRUE WHERE email <> external_id`, and CLAUDE.md records that
  **without it the release is not a no-op for anybody** - every existing account would have landed on the free
  tier and lost the assistant.

Neither hazard exists here. Both new columns are read as "absent" by every consumer, absent is the correct
state for every pre-existing row, and an account's resolved plan on the request after this migration is
byte-for-byte the plan it resolved on the request before. **This release is a no-op for every existing
account**, which is the property `AddAccountPlans` had to backfill to achieve and this one gets for free.

## Rationale

### Why a stored override at all, when DEC-022 refused a plan column

DEC-022 refused a `User.Plan` column on two grounds and only one of them applies here.

The first is the founding premise: a stored *derived* value goes stale. That is not what this is. The resolved
plan is still computed on every request by `PlanResolver.Resolve` from three inputs - the override, the comp
list and a verified address - and is stored nowhere. `plan_override` is an **input**, the same kind of thing
`Plans:CompEmails` already is, differing only in living in a table rather than in a container's environment.
DEC-002 is untouched.

The second is the `Vehicle.PurchasePrice` trap, which DEC-022 names directly: a column nothing writes. That
trap is real and this spec was nearly built into it - the first pass had read-only endpoints and this column,
which is precisely a stored field with no writer, waiting to be discovered as dead in six months. The column
and the two endpoints that write it ship together, or neither ships.

### Why the override is read before the comp list

An override is a decision somebody made about one account. A comp list is a rule about a class of addresses.
Where they disagree, the specific decision is the newer information and the one a human took deliberately.
Reading the list first would make an override that agrees with it invisible and an override that contradicts
it inert, which are two different kinds of confusing.

It also fixes the ordering for the reason: `PlanReason.AdminGranted` is only reachable if the override is
consulted before anything that could return early, and the account holder seeing "granted by an administrator"
rather than "not on the comp list" is the difference between a screen that explains itself and one that lies
politely.

### Why `last_seen_at` is stored rather than derived

Last activity could be derived from rows that already exist - `chat_usage.day`, `assistant_tokens.last_used_at`
and the `IAuditable` timestamps on anything the account created - with no migration at all, and that is the
shape this project reaches for first.

It was rejected because it is blind to the visit that matters most. Somebody who signs in, reads their
dashboard, looks at their fuel log and signs out writes nothing to any of those tables, and that is most of a
first session and nearly all of a second one. A derived figure would report that person as never having
returned, which is the exact opposite of the fact the column exists to record.

Storing an observation is not storing a derived value. Nothing recomputes when somebody signs in; there is no
underlying record this could disagree with, because the sign-in *is* the record. The distinction belongs in a
comment on the property, because the file it lands in is otherwise entirely about the derive-on-read premise
and a reader finding a new stored column there is owed the reason.

### Why the write is coalesced to fifteen minutes rather than written every request

Stamping on every authenticated request turns a read-mostly request into a write, on the hot path, for a
figure whose consumer is a screen one person opens occasionally. Fifteen minutes is short enough that "seen
today" and "seen in the last hour" are both accurate, and long enough that a session browsing between screens
writes once rather than forty times.

The comparison is against the **stored** value, not a cache, so the coalescing survives a container recreate -
the mistake `chat_usage` was a table rather than a counter to avoid, and the reasoning is the same one written
down there.

### Why not a `daily_usage` table generalising `chat_usage` and `vehicle_lookup_usage`

Not proposed and not wanted, recorded because the admin surface reads both and the symmetry is tempting from
that vantage point. DEC-022 already answered it: generalising would rewrite a working migration and its tests
to save one entity. The admin service joins them in memory, which costs nothing at this scale and leaves both
ledgers owned by the code that writes them.
