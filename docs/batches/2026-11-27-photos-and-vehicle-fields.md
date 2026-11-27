# Batch 2026-11-27-photos-and-vehicle-fields

- **Status:** done
- **Started:** 2026-11-27
- **Prompt:** "/ue-specs:batch" (proposal accepted; drafts all first, then execute)
- **Options:** tests=auto · high-risk=assume · git=commit-per-task · drafts=execute · review=end · run=sequential
- **Branch:** batch/2026-11-27-photos-and-vehicle-fields (from e25f73b; specs committed as 5c08a05)
- **Test command:** `dotnet test` (repo root, needs Docker); `cd src/CarTracker.WebApp && NODE_OPTIONS=--no-webstorage npx vitest run`; `npx tsc -b` (typecheck)
- **Baseline:** .NET 811/817 passing, 6 skipped, 0 failing; web 701/705 passing, 4 skipped, 0 failing; tsc clean
- **Repo rules for runners:** house-style commit subjects (imperative, no spec prefix, no AI trailer);
  bump `VERSION` once per spec (minor) in that spec's first feature commit, via `./scripts/release.ps1 -Minor`
  or by editing `VERSION`; no em-dash (U+2014) in anything new

## Queue

| # | Spec | Mode | State | Tasks | Assumptions | Tests added | Commits |
|---|------|------|-------|-------|-------------|-------------|---------|
| 1 | 2026-11-27-clear-vehicle-fields | draft (tasks.md) | done | 0/3 | A1 m, A2 l, A3 l, A4 m, A5 l, A6 l | (planned: int, unit, component) | 58f4e62 |
| 2 | 2026-11-27-photo-gallery-document-categories | draft (tasks.md) | done | 0/6 | A1-A5 h, A6-A10 m, A11-A18 l | (planned: unit, int, component+axe) | 37f1252 |
| 3 | 2026-11-27-assistant-photo-filing | draft (tasks.md) | done | 0/5 | A1-A2 h, A3-A6 m, A7-A13 l | (planned: unit, int, component+axe) | ee268c9 |
| 4 | new: fluids-tyre-settings-form | draft | skipped | | | | |
| 5 | 2026-11-27-clear-vehicle-fields | execute | done | 3/3 | +A7 l, A8 m | 10 int, 2 unit, 9 component | fd15bdb..c69e46a |
| 6 | 2026-11-27-photo-gallery-document-categories | execute | done | 6/6 | +A19-A21 l, A22 m, A23-A24 l | 38 unit, 50 int (.NET); 18 unit, 63 component (web) | f889d67..f6ed25d |
| 7 | 2026-11-27-assistant-photo-filing | execute | partial | 4/5 | +A14-A17 l | 73 unit, 24 int, 53 component | 7c9a898..26f1fcf |
| 8 | fluids-tyre-settings-form | execute | skipped | | | | |

## Log

- Branch created; three Nov-27 spec folders committed (5c08a05)
- global.json rollForward latestPatch -> latestFeature, so SDK 10.0.404 satisfies the 10.0.301 pin (024448d, human's choice)
- Local Node is v26 (CI pins 24): its built-in localStorage shadows jsdom's and fails 332 web tests; NODE_OPTIONS=--no-webstorage restores 701/705
- Baseline taken: .NET 811/817 (6 skipped), web 701/705 (4 skipped), tsc clean
- #1 dispatched
- #1 done, verified: 58f4e62 exists, 3 parent tasks, A1-A6 in assumptions.md
- #4 and #8 skipped: fluids and tyres editors already exist in VehicleEditSheet (shipped f6f800f, 2026-08-15); the roadmap bullet is stale and clear-vehicle-fields task 3.6 removes it (A1)
- #2 dispatched
- #2 done, verified: 37f1252 exists, 6 parent tasks, A1-A18 in assumptions.md (5 high: Photo rows rewritten to ConditionPhoto with lossy Down, contract rename with no alias, GPS stripped from previews only, privacy sentence and LegalVersion bump, cover-photo FK guard on account deletion)
- #3 dispatched
- #3 done, verified: ee268c9 exists, 5 parent tasks, A1-A13 in assumptions.md (2 high: A1, A2 = guarded returnTo after Auth0 sign-in for gated routes)
- Drafts complete; #5 dispatched
- #5 done, verified: 3/3 tasks (19/19 sub), commits fd15bdb 2b80ae3 c69e46a, VERSION 0.30.0, A7-A8 logged; suite .NET 821/827 (6 skipped), web 711/715 (4 skipped), tsc clean, no new em-dashes. By hand later: one real PATCH with a JWT (A8)
- #6 dispatched
- #6 done, verified: 6/6 tasks (46/46 sub), commits f889d67 78cade2 606e9bc 1fb0092 19105a9 f6ed25d, VERSION 0.31.0, migration AddPhotoGalleryAndDocumentCategories, A19-A24 logged; suite .NET 909/915 (6 skipped), web 792/796 (4 skipped), tsc clean, no new em-dashes. Hand checks 2.7 and 6.7 ticked but NOT run (A22): HEIC on a real iPhone, endpoint headers through the gateway. Outside the spec: useFocusTrap answers keys for the newest trap only, Sheet stops submit propagation (stacked-sheet bug)
- #7 dispatched
- #7 partial, verified: 4/5 tasks (30/31 sub), commits 7c9a898 a358eb4 31c585a 2cca144 26f1fcf, VERSION 0.32.0, A14-A17 logged; suite .NET 975/981 (6 skipped), web 882/886 (4 skipped), tsc clean, no new em-dashes. 5.6 blocked: by-hand check needs a live Auth0 session, Chat:ApiKey and an MCP client. Shared host compose copy needs App__PublicOrigin / APP_PUBLIC_ORIGIN
- Queue complete; review started
- Review done; batch closed. Final suite: .NET 975/981 (6 skipped), web 882/886 (4 skipped), tsc clean
- PR #13 opened; spec status lines updated (4afead3)
- Localhost end-to-end run (AppHost, real Auth0 session, Playwright): clear PATCH, renditions with no GPS and
  their cache headers through the gateway, viewer, cover photo, signed-out `/file` link through sign-in to a
  pre-filled MOT upload all pass. Three problems found and fixed: the Engine oil row and its capacity vanished
  after a clear (0.32.1); the refused `/file` page had no app shell (0.32.2); dev uploads in
  `src/CarTracker.WebApi/documents-data/` were not gitignored. Web suite 885/889 (4 skipped), tsc clean

## Review

- High risk, kept (accepted): photo-gallery A1 (existing Photo rows become ConditionPhoto), A3 (GPS stripped from
  browser previews only, viewer falls back to the original), A4 (privacy sentence always shown, LegalVersion moves),
  A5 (cover-photo guard on vehicle and account deletion); photo-filing A2 (returnTo for every gated route)
- High risk, overridden:
  - photo-gallery A2: accept "Photo" as an alias for ConditionPhoto -> follow-up task 7
  - photo-filing A1: keep offer_to_file a read but audit it over MCP -> follow-up task 6
- Low/medium: all 42 accepted
- Totals: 47 accepted, 2 overridden, 0 deferred
- Owed by hand before release (after the localhost run): photo-gallery 2.7 (the 400 and 404 answers) and 6.7
  (iPhone HEIC, Compare layout); photo-filing 5.6 (chat cards with a live `Chat:ApiKey`, a real MCP client)
- Deploy note: the shared host's compose copy needs App__PublicOrigin and APP_PUBLIC_ORIGIN=https://cambelt.app

