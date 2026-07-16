# Database Schema

This is the database schema implementation for the spec detailed in @docs/specs/2026-07-16-head-gasket-watch/spec.md

## New table: `issue_watch_checks`

```
CREATE TABLE issue_watch_checks (
  issue_id            integer NOT NULL REFERENCES issues (id) ON DELETE CASCADE,
  check_definition_id integer NOT NULL REFERENCES check_definitions (id) ON DELETE CASCADE,
  PRIMARY KEY (issue_id, check_definition_id)
);

CREATE INDEX ix_issue_watch_checks_check_definition_id
  ON issue_watch_checks (check_definition_id);
```

Configured on a new `IssueWatchCheckConfiguration : IEntityTypeConfiguration<IssueWatchCheck>`, both FKs
`DeleteBehavior.Cascade`, composite key.

## Rationale

- **A join table, not a column on either side.** An issue watches a *set* of checks and a check may guard more
  than one issue, so neither `Issue` nor `CheckDefinition` gains a field — the relationship is its own row.
- **Cascade both ways.** The link is a statement about a pair; if either the issue or the check definition is
  deleted, the statement is void. Cascade removes the link and leaves the surviving row untouched (deleting a
  check does not delete the issue, and vice versa).
- **The same-vehicle invariant is not a DB constraint.** Both `issues` and `check_definitions` carry a
  `vehicle_id`, but a cross-table CHECK on two FKs is awkward in Postgres and the write path is the honest place
  to enforce it — the join is only ever created through the issue edit endpoint, which knows the vehicle. A
  cross-vehicle link cannot arise from the UI; the guard is defence against a future caller.
- No change to `issues` or `check_definitions`. The five-defect fixture is untouched — this adds no arithmetic.

## Migration

One additive migration (`AddIssueWatchChecks`) creating the table and index. No data backfill: existing issues
watch nothing until linked, which is the correct default.
