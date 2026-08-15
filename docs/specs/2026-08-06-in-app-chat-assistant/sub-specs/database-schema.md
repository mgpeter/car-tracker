# Database Schema

This is the database schema implementation for the spec detailed in @docs/specs/2026-08-06-in-app-chat-assistant/spec.md

## Summary

**One change: a fifth `EntrySource`.** There is no chat table, no message table, no photo table - the
conversation is client-held and the photo is discarded (see the spec's Out of Scope). The only persistent
trace a chat leaves is the row it writes, and README §6 requires that row to say which surface wrote it.

## Change: `EntrySource.Chat`

`src/CarTracker.Shared/EntrySource.cs` gains a fifth member:

```csharp
public enum EntrySource
{
    // Deliberately no zero member - see the existing comment.
    Web = 1,
    Mcp = 2,
    Import = 3,
    Seed = 4,
    Chat = 5,
}
```

`AuditConfiguration.ConfigureAudit<T>` persists this as a lowercase string in a `varchar(8)` column and
constrains it:

```csharp
builder.ToTable(t => t.HasCheckConstraint(
    $"ck_{tableName}_source",
    "source IN ('web', 'mcp', 'import', 'seed', 'chat')"));
```

`'chat'` is four characters, so **`varchar(8)` is unchanged** - the column does not need widening, only the
constraint needs rewriting.

## Migration: `AddChatEntrySource`

Because `ConfigureAudit<T>` is applied by **every** `IAuditable` entity's configuration, EF will emit a
drop-and-recreate of `ck_<table>_source` for each of them. That is a dozen-plus constraint pairs in one
migration and is expected - it is the cost of the check constraint being the thing that makes the enum real
in the database rather than only in C#.

```sql
-- one pair per auditable table, e.g.
ALTER TABLE fuel_entries DROP CONSTRAINT ck_fuel_entries_source;
ALTER TABLE fuel_entries ADD CONSTRAINT ck_fuel_entries_source
    CHECK (source IN ('web', 'mcp', 'import', 'seed', 'chat'));
```

**Verify the generated migration lists every auditable table** before applying it. A table whose
configuration forgot `ConfigureAudit` would silently be absent from the diff, and the first chat write against
it would fail the old constraint at runtime - which is a better failure than a missing constraint, but a worse
one to discover in production than in review.

- Down migration restores the four-value constraint. It will fail if any `'chat'` rows exist, which is correct
  - silently rewriting real attribution to make a rollback succeed would be worse than the rollback failing.
- No data migration. Existing rows keep their existing source.

## Rationale

**Why not reuse `Web`?** The owner does press Save in the web app, so `Web` is defensible - and it is wrong.
The figures in a chat-drafted row were read off a photograph by a model, not typed by a person; when a litre
count later looks odd, "which surface produced this?" is exactly the question the audit block exists to
answer, and collapsing two provenances into one value destroys the only evidence. The same argument produced
the `EntrySource` enum in the first place.

**Why not `Mcp`?** The tools are shared, but the surface is not: an MCP write is unattended and carries a
scoped bearer token; a chat write is confirmed by a signed-in human. `AssistantWriteAudit` covers the former
and does not cover the latter (technical spec), so folding them together would leave chat writes looking like
audited MCP writes that have no audit row.

**Why no conversation table?** Nothing in the user stories needs a transcript to survive a page reload, and
persisting one would immediately raise retention, ownership and PII questions (photographs of documents pass
through these messages) that a v1 has no need to answer. The Messages API is stateless; matching it is the
smaller design.

**Why the pending write is not a table either.** The confirm gate needs server-side state - a client-supplied
transcript cannot authorise a write (`api-spec.md`) - but that state lives in `IMemoryCache` for ten minutes
and holds a tool name, its arguments, a vehicle and an owner id. It is not a transcript and it is not durable:
if the process restarts, the draft expires and the owner asks again, which is the correct outcome for a
proposal nobody has confirmed. A table would make it durable, which would make it a retention question about
data the owner never agreed to save.
