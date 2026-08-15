# Spec Tasks

## Tasks

- [x] 1. The watch link
  - [x] 1.1 Tests written for the join and the same-vehicle guard - `IssueWatchTests` (6, Testcontainers):
        set/replace/clear diffs the links, a cross-vehicle id is refused whole, deleting a check definition
        removes the link and leaves the issue, deleting the issue removes its links and leaves the checks.
  - [x] 1.2 `IssueWatchCheck` entity + `IssueWatchCheckConfiguration` - composite `(IssueId, CheckDefinitionId)`
        key, both FKs `DeleteBehavior.Cascade`, plus the reverse index on `CheckDefinitionId`. No audit block:
        the row is a statement about a pair, not an event with a source.
  - [x] 1.3 Migration `AddIssueWatchChecks` - table + index, byte-for-byte the sub-spec's SQL. No backfill.
  - [x] 1.4 All 6 pass.

- [x] 2. Derive the contingency through the shared brain
  - [x] 2.1 Domain tests written - `WatchCalculatorTests` (10): both current → no lapse; one overdue → 1;
        `NeverLogged` → lapsed; `Attention` → lapsed *even though in date*; `DueSoon` → not lapsed; a retired
        definition falls out; ranking is worst-first; the issue's own status comes back unchanged.
  - [x] 2.2 `WatchCalculator` reads `CheckStatusCalculator`'s per-check `CheckState` - it adds **no arithmetic**
        and never asks for itself whether a check is overdue. `IssueItem` gains `Watch` (the checks + their
        live status + an `IsLapsed` flag carried from the server, so the rule is not re-evaluated per surface).
  - [x] 2.3 `VehicleSummary` gains `Watches` (issue id/title/status + lapsed/total counts), computed in
        `DerivedMetrics.Compute` from the one `CheckStatusSummary` already being built for `Checks` - the same
        instance, passed to both, so the dashboard's named watch and the checks screen agree by construction.
        `VehicleMetricsData`/`VehicleMetricsLoader` grew `Issues` + `IssueWatchChecks` to feed it.
  - [x] 2.4 Contract and TS types regenerated - **additive only**, 110 insertions / 0 deletions.
  - [x] 2.5 All pass - 221 Domain, 140 Data.

- [x] 3. Surface it on the screens
  - [x] 3.1 Tests written - `AttentionPanel.test.tsx` (+6): the watch is named, ranks above the generic
        overdue alert, stays silent when current, words Resolved and Monitoring differently, and reports a
        partial lapse as a fraction. `phase3.test.tsx` (+4): the contingency line renders, the lapse is flagged
        while the status pill still reads Resolved, an unwatched issue renders nothing, and the watched row is
        axe-clean.
  - [x] 3.2 Issues screen - an `IssueWatch` contingency line ("Resolved, contingent on 2 checks · 1 lapsed")
        naming each check, on the **due** axis (blue is the integrity axis and would say the wrong thing); a
        `WatchPicker` on the edit sheet, id-keyed and empty-by-default.
        > Deliberately **not** `<CheckSelectList>`: that is name-keyed and defaults all-on, so sharing it would
        > mean passing the complement of the selection as its "deselected" set and mapping names back to ids.
        > Two toggle lists with opposite defaults are not one abstraction yet. It borrows `.checksel` styling so
        > they still read as siblings.
  - [x] 3.3 Dashboard `AttentionPanel` - a named lapsed watch ranked above the generic overdue-check alert and
        below an expired renewal (a car that cannot legally move still outranks it), deep-linking to checks.
  - [x] 3.4 Axe-swept on both surfaces; no coverage-guard exemption needed (both new components are local to
        `IssuesPage` and swept with the page).
  - [x] 3.5 All pass - 441 front-end.

- [x] 4. Prove it end to end
  - [x] 4.1 Covered by `A_lapsed_watch_surfaces_on_the_issue_log_and_the_summary_without_reopening_the_issue`,
        which builds the design's exact scenario against a real database: the head-gasket issue created
        Resolved, the two weekly K-series checks linked, both last done 18 June against a 14 July reference.
  - [x] 4.2 That test asserts both surfaces from one write: the issue log shows two lapsed watched checks and
        `Status: Resolved`, and the summary's `Watches` names the issue with 2 of 2 lapsed. The status is
        untouched - flagged, never reopened.
  - [x] 4.3 Full suite green (221 Domain, 140 Data, 441 front-end), both builds clean, codegen gate additive
        only. Roadmap and CLAUDE.md updated.

## Fixed along the way

- **`IssueService.AddAsync` never stamped `ResolvedDate`.** `ck_issues_resolved_date_iff_resolved` requires the
  date iff the status is Resolved, so posting an issue *already* Resolved - which is exactly how the head-gasket
  item arrives, resolved off the May compression test - failed on the constraint with a bare
  `DbUpdateException`. The PATCH path had always stamped it on a status change; the add path never did, because
  until this spec nothing had posted a Resolved issue. Found by the first write-path test.
- **`AddIssueRequest` accepts the watch too**, not just PATCH, so linking checks is not an operation you can
  only perform on an issue that already exists - a rule about our plumbing rather than about watches.
