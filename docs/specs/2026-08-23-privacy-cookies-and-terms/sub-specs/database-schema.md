# Database Schema

This is the database schema implementation for the spec detailed in
@docs/specs/2026-08-23-privacy-cookies-and-terms/spec.md

One migration, one nullable column on one table, **no backfill**, no data change and no new table. The
retention half of this spec deletes rows from tables that already exist and needs no schema at all.

## Migration 1 - `AddTermsAcceptance`

```sql
ALTER TABLE users ADD COLUMN terms_version varchar(32) NULL;
```

`Down()` drops it. Reversible and safe to reverse: one nullable column that nothing references, so dropping it
loses the record of which document version was in force at each sign-up and breaks no foreign key, index or
constraint.

EF generates this correctly from the entity change, so hand-writing it would be the mistake
`AddPerOwnerReferenceLists` was right to make and this one is not. Generate it, then read it - the check is
that it contains exactly one `AddColumn` and no `DeleteData`, `UpdateData` or `Sql`.

**The release is a no-op for every existing account**, which is the property `AddAdminObservability` achieved
and `AddAccountPlans` could not: nothing resolves differently, nothing changes tier, and no account loses a
capability the day it lands.

### `users.terms_version`

`string?` on the entity, `varchar(32) NULL` in the column.

```csharp
// User.cs
public string? TermsVersion { get; set; }

// UserConfiguration.cs
builder.Property(u => u.TermsVersion).HasColumnType("varchar(32)");
```

**A stored observation, not a stored derived value.** It records which version of the committed documents was
in force at the moment this account was provisioned. Nothing recomputes it, and there is no underlying record
it could disagree with, because the provisioning *is* the record - the same sentence `LastSeenAt` carries, for
the same reason, in a file otherwise entirely about deriving on read.

**Nullable, and the null is load-bearing in the other direction from `PlanOverride`'s.** Null means one of two
things and both are true statements: the account was created before these documents existed, or it was created
on a deployment that publishes none. **It is never backfilled.** Writing the current version into those rows
would assert that somebody accepted a document that did not exist when they signed up, and being able to tell
those rows apart from the ones that did is the entire reason for the column.

**Not the date, and not a boolean.** A boolean `AcceptedTerms` cannot say *which* terms, so it goes stale
silently the first time the wording changes - and the wording changing is the case this column is for. The
date is already on the row: `User.CreatedAt` is stamped at provisioning and the stamp happens in the same
place, so a second timestamp would be a second source of truth for one event.

**`varchar(32)`, holding the version string, not a foreign key to a versions table.** The versions are
committed prose in the front-end bundle; a table would be a database-side index of something the database
cannot read, and no query ever joins on it. The value is a date-shaped constant (`2026-08-23`), so 32
characters is comfortable room for whatever scheme replaces it.

### Written on the creation path only

`AccountProvisioner` stamps it where it stamps `CreatedAt`, and nowhere else.

This is `TouchLastSeenAsync`'s lesson repeated deliberately: `BackfillEmailAsync` returns early at `:225` once
the address is present and verified - the common case for every established account - so anything folded into
it is not written for exactly the accounts worth looking at. A Data test asserts that a returning account's
`TermsVersion` is unchanged after a sign-in, including when the constant has moved on since.

## What retention deletes, and why none of it is a schema change

`RetentionBackgroundService` issues one `ExecuteDeleteAsync` per table:

| Table | Date column | Kept for |
|---|---|---|
| `chat_usage` | its day column | `Retention:LedgerDays`, default 400 |
| `vehicle_lookup_usage` | its day column | the same window |
| `assistant_write_audits` | its audit timestamp | the same window |

No column is added, no index is added, and no constraint changes. **None of these three tables carries a query
filter** - only `Vehicle`, `Garage`, `WashLocation` and `ExpenseCategory` do - so a deployment-wide delete from
a background scope is correct here and needs neither `IgnoreQueryFilters()` nor `BypassOwnership`.

**An index is deliberately not added yet.** A delete by date on three small tables, once a day, on a
single-digit-account deployment, does not need one; adding an index against a table whose size nobody has
measured is optimising a query that has never been slow. The line to watch is `assistant_write_audits`, which
is the only one of the three that grows per write rather than per account per day.

`pending_identity_deletions` is **not** on the list. It is already transient by design - a row exists only
while an Auth0 deletion is being retried hourly - and pruning it by age would abandon an erasure that has not
completed, which is the opposite of what a retention policy is for.
