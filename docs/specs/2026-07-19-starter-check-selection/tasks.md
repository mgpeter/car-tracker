# Spec Tasks

## Tasks

- [x] 1. Domain — selection-aware check resolution
  - [x] 1.1 Write tests for `VehicleFactory`/`ResolveChecksAsync`: a `null` selection yields all fifteen (the
        existing behaviour, unchanged); a subset yields exactly those definitions in template order; an empty
        list yields none; a selection is ignored for `CheckSource.None` and `CheckSource.CopyFromVehicle`.
  - [x] 1.2 Add an optional `IReadOnlyList<string>? selectedCheckNames = null` parameter to `CreateAsync` and
        thread it through to `ResolveChecksAsync`.
  - [x] 1.3 Filter the `GenericStarterSet` branch by the selected names — via a `CheckTemplate.For(0, names)`
        overload doing an ordinal `Contains` filter over the ordered `Generic` list, preserving template order
        and renumbering `DisplayOrder` contiguously; `null` means all fifteen.
  - [x] 1.4 Verify all domain tests pass (WritePathTests: 32 passed, incl. 4 new + the unchanged all-15 case).

- [x] 2. API — expose the template and accept the selection
  - [x] 2.1 Test strategy: the repo has no HTTP/WebApplicationFactory harness — endpoints are thin and behaviour
        is covered by the domain tests (against real PostgreSQL) plus the front-end tests that mock the wire. The
        selection behaviour is pinned by Task 1's `WritePathTests`; the endpoint is a passthrough/projection.
        Followed that convention rather than standing up a new HTTP test project.
  - [x] 2.2 Added `GET /api/reference/starter-checks` projecting `CheckTemplate.Generic` (name, cadenceLabel,
        intervalDays, guidance) in template order, behind the API key, no DB.
  - [x] 2.3 Added `SelectedCheckNames` (`IReadOnlyList<string>?`) to `CreateVehicleRequest` and passed it to
        `VehicleFactory.CreateAsync`.
  - [x] 2.4 WebApi builds clean; whole solution builds clean.

- [x] 3. Contract and typed client
  - [x] 3.1 Regenerated `api-contract/v1.json` (build-time emission) — additive only: +60 lines, 0 deletions
        (new `GET /api/reference/starter-checks` path, `StarterCheckItem` schema, `selectedCheckNames` property).
  - [x] 3.2 Regenerated the typed front-end client (`npm run gen:api`).
  - [x] 3.3 Front-end typechecks clean against the regenerated schema.

- [x] 4. Front-end — the inline toggle list
  - [x] 4.1 Wrote 4 `AddVehicleSheet` tests (in `GaragePage.test.tsx`): picking the generic set reveals the
        checks all-on with a live count; deselecting one posts only the kept names; an untouched submit omits
        `selectedCheckNames` entirely (byte-for-byte today); choosing "None" hides the list and sends no
        selection. A URL-aware fetch mock serves the template and captures the create body.
  - [x] 4.2 Added `useStarterChecks(enabled)` beside the reference hooks (`api/reference.ts`), cached
        indefinitely, fetched only while the sheet is open.
  - [x] 4.3 Rendered the reveal-on-source toggle list — checkboxes defaulted all-on, a live "N of M" count,
        each check's cadence shown read-only; selection tracked as a `Set<string>` of *deselected* names so
        all-on needs no async initialisation.
  - [x] 4.4 Submit shape: `selectedCheckNames` omitted when every check is kept, `[]` when none, the kept names
        otherwise — only for `GenericStarterSet`.
  - [x] 4.5 All 22 `GaragePage` tests pass (4 new + 18 existing).

- [x] 5. Prove it end to end and document
  - [x] 5.1 Full suites green — .NET 286 (200 Domain + 86 Data, Testcontainers), front-end 404 passed / 4
        skipped; oxlint clean; solution + tsc build clean; contract regenerated additive-only.
  - [x] 5.2 Updated CLAUDE.md's state-of-play with the add-car starter-check selection.
