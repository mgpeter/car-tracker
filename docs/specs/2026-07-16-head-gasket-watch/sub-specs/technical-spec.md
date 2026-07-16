# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-head-gasket-watch/spec.md

## Technical Requirements

### The link

- A many-to-many between `Issue` and `CheckDefinition`, both already vehicle-scoped. An issue may watch several
  checks (the head-gasket watch is two); a check could in principle guard more than one issue. A join entity
  `IssueWatchCheck { IssueId, CheckDefinitionId }` with a composite key, both FKs `ON DELETE CASCADE` (deleting
  either end removes the link, not the other row).
- Guard: both ends must belong to the **same vehicle**. Enforce in the write path (the join has no vehicle
  column of its own); a link across vehicles is a bug, not a feature.

### Derived contingency — through the shared brain

- The watch's *status* is not stored; it is the live status of its checks, which `CheckStatusCalculator`
  already computes. The issue screen and the dashboard must read that same computation — a second "is this
  check overdue" path is exactly the kind of divergence the project exists to prevent.
- Add to the issue read model (`IssueItem` / `IssueLog` in `IssueEndpoints.cs`) the watched checks *and their
  derived status*, so the screen renders "contingent on N checks, M overdue" without re-deriving. The cleanest
  source is `CheckStatusCalculator`'s per-check result, joined to the watch links.
- For the dashboard, the named watch belongs on `VehicleSummary` — a small addition (e.g. `Watches:
  [{ IssueTitle, LapsedCheckCount, TotalCheckCount }]`) computed in `DerivedMetrics.Compute` from the issues,
  their watch links, and the check statuses already in scope. A headline, like the integrity summary: the
  attention panel needs the name and the counts, not the full check list.

### Screens

- **Issues screen** (`IssuesPage.tsx`) — a watched issue shows its checks and their status; a Resolved issue
  whose watch has a lapsed check gets a warning affordance (blue is the integrity axis and wrong here; this is
  the *due* axis — reuse the check's own tone). The edit sheet gains a multi-select of the vehicle's check
  definitions.
- **Dashboard attention panel** (`AttentionPanel.tsx`) — a lapsed watch becomes a named alert ranked **above**
  the generic "N checks overdue" alert, because a named early-warning system lapsing is more specific than the
  count. Its action deep-links to the checks screen (a batch-log of the watched checks is a nice-to-have, not
  required — the checks screen already batches).
- Every new component axe-swept or exempted; `usePlate()` already handles the plate.

### Interactions to preserve

- **No auto-reopen.** The issue's `Status` is untouched by check status; the watch only *surfaces* the
  contingency. This mirrors the anomaly lifecycle's "flag, never act for the owner".
- A `NeverLogged` watched check counts as lapsed for the warning (a never-done early-warning check is not
  reassurance), consistent with how `NeverLogged` is treated as a real due state elsewhere.
- Deleting a check definition that a watch points at removes the link (cascade) and the issue simply watches
  fewer checks; deleting the issue removes its links.

### Verification

- Domain tests: an issue watching two checks, both OK → no lapse; one overdue → lapse count 1; a `NeverLogged`
  watched check → counts as lapsed. Against the check-status calculator, no new arithmetic.
- Write-path test (Testcontainers): the same-vehicle guard rejects a cross-vehicle link; cascade on delete.
- Live on BT53: create the head-gasket issue, link the two weekly checks (once they exist as definitions),
  mark it Resolved, let a check go overdue, and confirm the issues screen shows the contingency and the
  dashboard names the watch — the exact scenario the design's dashboard depicts.

## Database Schema

See `sub-specs/database-schema.md` — one join table, no change to `Issue` or `CheckDefinition`.

## External Dependencies (Conditional)

None. `CheckStatusCalculator`, the issue endpoints and the check definitions all exist; this is a link plus
surfacing it through the summary the two screens already read.
