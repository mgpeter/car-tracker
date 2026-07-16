# Spec Tasks

## Tasks

- [ ] 1. The watch link
  - [ ] 1.1 Write tests for the join config and the same-vehicle guard
  - [ ] 1.2 `IssueWatchCheck` entity + `IssueWatchCheckConfiguration` (composite key, cascade both FKs)
  - [ ] 1.3 Migration `AddIssueWatchChecks`
  - [ ] 1.4 Verify tests pass

- [ ] 2. Derive the contingency through the shared brain
  - [ ] 2.1 Write domain tests: two OK checks → no lapse; one overdue → lapse 1; a NeverLogged watched check → lapsed
  - [ ] 2.2 `IssueLog`/`IssueItem` carry the watched checks and their derived status (from `CheckStatusCalculator`, not a second path)
  - [ ] 2.3 `VehicleSummary` grows a `Watches` headline (issue title, lapsed/total counts), computed in `DerivedMetrics.Compute`
  - [ ] 2.4 Regenerate the contract and TS types; staleness gate green
  - [ ] 2.5 Verify tests pass

- [ ] 3. Surface it on the screens
  - [ ] 3.1 Write tests: a resolved-but-watched issue shows contingency and warns on a lapsed check; the dashboard names the watch above the generic overdue alert
  - [ ] 3.2 Issues screen — contingency display + a check-definition multi-select on the edit sheet
  - [ ] 3.3 Dashboard `AttentionPanel` — a named lapsed watch, ranked above generic overdue checks, deep-linking to checks
  - [ ] 3.4 Axe sweep + coverage-guard exemptions
  - [ ] 3.5 Verify tests pass

- [ ] 4. Prove it end to end on BT53
  - [ ] 4.1 Create the head-gasket issue, link the two weekly K-series checks, mark Resolved
  - [ ] 4.2 Let a watched check go overdue; confirm the issue shows the contingency without its status changing, and the dashboard names the lapsed watch
  - [ ] 4.3 Full suite, both builds, codegen gate; update roadmap/CLAUDE.md
