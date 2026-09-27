# CLAUDE.md

Guidance for Claude Code in this repository. This file holds **standing rules only**. History of what shipped
and why lives elsewhere:

| Question | Authority |
|---|---|
| What is built, what is left, build order | `docs/product/roadmap.md` |
| Why something is the way it is (overrides this file on conflict) | `docs/product/decisions.md` (DEC-nnn) |
| Scope and product specification | `README.md` §1, §3-§6 |
| Data model | `docs/specs/2026-07-14-core-data-model/sub-specs/database-schema.md` |
| A feature's design and tasks | `docs/specs/<date>-<name>/` |
| What each slice turned up while shipping | `docs/engineering-log.md` |
| Deployment | `docs/deployment-shared-host.md`, `docs/deployment-synology.md` |

## Writing conventions

- **Never write the em-dash character, U+2014**, anywhere: prose, code, comments, commits, UI copy. Use `-` or
  restructure. `git grep -nP '\x{2014}' -- '*.md'` must stay empty. Source files still carry ~2,700 of them;
  scrubbing those is a separate code change (contract, generated client and test assertions move together), so
  apply the rule to everything **new**.
- En-dashes (U+2013) are intentional in numeric ranges (phases, day windows). Leave them.

## Product in one paragraph

Cambelt (`cambelt.app`) replaces an Excel workbook tracking a 2003 Freelander, BT53 AKJ. Multi-vehicle,
multi-account (Auth0), open sign-up bounded by plans (DEC-022), an MCP server and an in-app chat assistant over
the same domain. Live on a shared Azure host owned by `usualexpat-infra` (DEC-020); the NAS runs the
`standalone` compose profile. **The code is still named `CarTracker` everywhere internal** (namespaces, images,
`cartrackerdb`, the `cartracker.api` Auth0 audience, the `cartracker.settings` localStorage key). Do not rename
internals: the audience change invalidates every token and the storage key change silently resets preferences.
Brand strings are picked by hand, never find-and-replace, because "cambelt" is also a car part in fixtures.

## Commands

```
dotnet run --project src/CarTracker.AppHost   # everything; app on http://localhost:5080
dotnet build
dotnet test          # needs Docker - Testcontainers starts a real PostgreSQL 17
dotnet ef database update --project src/CarTracker.Data   # honours CARTRACKER_CONNECTION
```

Tests run against **real PostgreSQL with migrations applied**, never the in-memory provider (it ignores column
types, check constraints and FK behaviour). There is no `CarTracker.WebApi.Tests` project, so rules worth
testing live in the domain.

## Versioning and release

- **Every feature commit bumps `VERSION`**, inside the feature commit (minor for features, `-Patch` for fixes,
  `-Major` for breaks). `VERSION` feeds image tags and, via `Directory.Build.props`, the assembly version.
  `./scripts/release.ps1 -Minor [-DryRun|-Build]` writes it; it never publishes.
- A push publishes `:edge`; a release is a git tag (`git tag -a v0.x.0 && git push origin v0.x.0`), which
  retags the CI digest as `:0.x.0`, `:latest`, `:stable` (DEC-021).
- Both Dockerfiles must `COPY` `VERSION` and `Directory.Build.props` **before `dotnet restore`**, or the image
  silently builds as `1.0.0`.

## The central constraint

Every derived number is computed server-side on read and never stored (spec §1). One derived-metrics service
(`IDerivedMetricsService`) serves the web API, MCP and chat, so no figure can disagree with itself across
surfaces. Derived, never stored: current mileage (latest `MileageReading` **by date**, not `MAX`), MPG and
L/100 km, fleet stats, spend rollups, cost per mile, days to renewal, check status, budget variance, reminders,
watch status, plan entitlement.

## Standing rules

**Source values, never guess them.** Every past UI bug of this kind came from hand-typing a list the server
owns. Read enums off the generated contract types as `Record<Enum, ...>` (never `Record<string, ...>`, which
lets a new member render blank). Read reference lists from their endpoints. Render `DataAnomaly.Message`, not
`Detail` (JSON). **The plate is never the URL slug**: use `usePlate()`; `coverage.test.ts` fails on
`plate={reg}`.

**Ownership.**
- One global EF query filter on `Vehicle` (plus `Garage`, `WashLocation`, `ExpenseCategory`) scopes by the
  current user. Children are reached only through an owner-checked vehicle, so a foreign vehicle 404s.
- Reference tables are keyed `(OwnerId, Name)` with **no foreign keys** to them (DEC-018). Nothing below the
  application objects to a bad name any more: `ReferenceWriter` is the single door for creating them and must
  stay so. Deletes name the whole primary key.
- `IgnoreQueryFilters()` appears only in `AdminReadService`. Isolation tests must pin an owner with
  `TestOwner.As(ownerId)`; a `BypassOwnership` context makes them false greens.

**Mirrors.** Fuel, service, wash, costed equipment and the purchase price each write a shadow `ExpenseEntry`
(and fuel/service a `MileageReading`) through their factory, in one transaction. Hand-typed Fuel and Purchase
expenses are refused; those categories are rename-locked. Import inserts rows directly rather than replaying
factories, because the export already contains the mirrors. Equipment counts as spend unless `ToOrder`
(`EquipmentRules.CostIsSpend`).

**Anomalies flag, never act.** Monotonicity and similar violations are flagged, not rejected. Every write path
except documents runs `AnomalyScanner`, which also retracts flags whose condition has gone. Watches and flags
never change an issue's status.

**Writes via the assistant.** Chat write tools require confirmation through an opaque server-held id
(`PendingWriteStore`); the client transcript authorises nothing. MCP write tools need the `McpWrite` scope and
are audited. No MCP tool deletes a vehicle or an account.

**Configuration polarity is stated where each key is set**, because it varies: blank `Chat:ApiKey` / `Lookup:`
means the feature is off (503, entry point hidden); blank `Signup:Mode` means **Open**; blank `Legal:` publishes
nothing; for allowances blank means default and `0` means off. Keys that bind to `long`/enums must be nullable
or strings, because compose passes unset keys as `""`.

**Routing.** `AuthGate` is the element of one route branch. A route added outside it is public silently;
`routes.gating.test.tsx` enforces the `PUBLIC_PATHS` list. Account and admin screens are routes reached from
the identity menu, not `ScreenId`s (`hrefFor` resolves every unscoped screen to `/`).

**Client storage.** Every `localStorage` key is declared in `lib/clientStorage.ts`; the test fails on
undeclared literals. A key that is neither `session` nor `preference` reopens the cookie-consent question
(DEC-024).

**Front-end patterns.** Sheets use `Field`'s `error` prop with `reportApiError`/`fieldError`, not `hint`.
Tables go through `<DataTable>` + `useTableView`; prose lists stay lists. Charts are hand-rolled SVG
(`TimeChart`, `Spark`) with derived captions and markers that survive greyscale. Authenticated bytes go through
`apiBlob()`/`apiDownload()`, never `<img src>` to the API. JSON writes must set `Content-Type` at the call
site. A test for "did this save" must assert the request, not the DOM.

**Gateway.** `CarTracker.Gateway` (YARP) is the single origin for the app, `/api`, `/mcp`, `/scalar`,
`/openapi`. There is no CORS; needing it means something bypassed the gateway.

## Known open traps

- `PATCH /vehicles/{reg}` with `isDefault: true` on a second car throws 23505 and answers 500 (no demotion of
  the incumbent). Unreachable today because nothing sets it; fix before adding a "make default" control.
- The chat recorded zero cache tokens across a real afternoon; the daily ceiling is denominated in that number.
  Its test is written and skipped.
- The DVLA/DVSA lookup is dormant and its mapping has never seen real traffic.
- A blank field in a vehicle PATCH means "leave unchanged"; there is no way to clear a value yet.

## Things that cost hours once

- **`ASPNETCORE_ENVIRONMENT` must be Development** or user-secrets do not load. User-secrets also override
  `appsettings.json`. Check both first when configuration seems ignored.
- **Aspire:** an unresolved parameter blocks on a dashboard modal with nothing in stdout (defaults live in the
  AppHost's `appsettings.Development.json`); resource logs go to the dashboard, not stdout. Aspire is 13.4.6,
  the installed templates are 9.1.0 and wrong under CPM, so hand-author csprojs. `bookmark-feeder` is a working
  reference for the same stack.
- **`WithDataVolume()` needs an explicit password parameter**; Postgres reads it only on first init.
- **Use `AddDbContext` + `EnrichNpgsqlDbContext`**, not `AddNpgsqlDbContext` (pooling rejects our
  `TimeProvider` ctor). The enrichment adds a retrying strategy, so every `BeginTransaction` must run inside
  `Database.CreateExecutionStrategy().ExecuteAsync(...)`. Tests do not catch this.
- The WebApi applies migrations on startup in Development only.
- **`Clock.Now()` carries a London offset**; Npgsql refuses non-UTC `timestamptz`. Instants come from
  `TimeProvider`, calendar days from `Clock`.
- **`Utf8JsonWriter` on `HttpResponse.Body` writes synchronously** (`JsonSerializer.Serialize` flushes). Buffer
  and `CopyToAsync` (`BufferedOutput`). A `MemoryStream` test cannot catch it; use `AsyncOnlyStream`.
- **SSE frames must be compact JSON**; `AIJsonUtilities.DefaultOptions` is indented.
- **Pinned pairs nothing enforces:** the Dockerfile SDK tag and `global.json` (roll-forward never goes down, and
  a stale cached floating tag fails restore blaming `global.json`); CI's `node-version` and
  `deploy/Dockerfile.gateway`'s Node (both 24).
- **Deployed compose files are copies.** A key added here reaches a container only after the host's copy is
  updated and the project rebuilt; Watchtower recreates from the running spec and follows the tag the container
  was created from. Diagnose with `docker compose exec webapi env | grep <Section>` (absent: stale YAML; empty:
  `.env` not read), `GET /api/meta`, the `Sign-up posture:` boot line, and
  `docker inspect --format '{{.Config.Image}}'`.
- `docs/images/` is not served; bundled images live in `src/assets/` and are imported.
- `rAF` does not fire in a hidden tab; use `setTimeout` for focus deferral. Callbacks passed to effects
  (`onClose`) belong in a ref, or inline handlers tear the effect down per render.

## What `archive/` is for

Load-bearing inputs, not clutter.

- **`ORIGINAL-TRACKER-IN-EXCEL-Freelander_BT53AKJ_Tracker.xlsx`** - the system being replaced and the source of
  the defect figures. Nothing reads it programmatically (DEC-008). Dates are Excel serials, epoch 1899-12-30
  (46217 = 2026-07-14).
- **`Sample-design-and-road-trip-tracking-green-lane-field-manual.html`** - origin of the visual identity.
- **`dashboard-full-claude-design/`** - the design reference: 17 `<x-dc>` template screens (`sc-if` is `&&`,
  `sc-for` is `.map()`), `theme.css`, `fonts.css`. `dashboard.dc.html` and `fuel-log.dc.html` inline drifted
  copies of the theme; its fuel sheet contradicts the domain (18-45 MPG band, no MPG on partial fills); most
  screens fake their writes.
- **`dashboard-design-idea/dashboard.html`** - superseded concept, still the best statement of the status
  treatment (severity stripe and uppercase mono label first, colour second).

## The five defects (regression fixture)

The workbook's Dashboard stores derived values, and five are wrong. They are hand-transcribed into a C# fixture
(reference date 2026-07-14):

| Dashboard says | Reality | Cause |
|---|---|---|
| MOT expiry 6 Aug 2026 | 8 Jul 2027 | Stale; superseded by the pass logged 8 Jul 2026 at 80,705 mi |
| Total litres 1,112.94 | 556.47 | Double-counts all 13 fills |
| - | Service row 27 Jun 2026 at 83,000 mi, above current 80,712 | Non-monotonic mileage; must be flagged |
| Fuel YTD £725.70 | £888.86 | Lumped expense row instead of per-fill mirrors |
| Worst MPG 24.49, 13-value average | 25.42 over 12 intervals | First fill's interval implies a reading that never existed (DEC-012) |

Not defects: average price per litre is a definition difference (DEC-011, volume-weighted 1.597324 vs plain
mean 1.594923). The live database totals £888.87 because receipts are rounded to the penny; the fixture uses
the workbook's unrounded values. Never-logged is a real fourth check state (the sheet counts 17 of 18).

## Design language

From the field manual; reuse it rather than inventing another.

- **Type:** Oswald (display, uppercase), Inter (body), JetBrains Mono (data and labels), `tabular-nums` where
  digits align. Fonts are inlined because of the strict CSP.
- **Palette:** `--ink #1E241B`, `--paper #E8E2CF`, `--paper-2 #DFD8BF`, `--panel #F1ECDD`, `--green-deep
  #2F3D2C`, `--green #5E7A34`, `--orange #B85C29`, `--rust #A23B2E`, `--blue #3E6187`, `--sand #C9B588`.
- Orange is structure only, never status. Status axis: green OK, `#C79A22` due soon, rust overdue; blue is
  reserved for data-integrity flags. Renewals: red under 30 days, amber under 60.
- No section numbering in app UI.

## Architecture

.NET 10, PostgreSQL 17, React 19 on Vite, Aspire, EF Core, `ModelContextProtocol.AspNetCore` (DEC-014),
official Anthropic SDK for chat (DEC-017), docker-compose. Ten projects under `src/`: `WebApp`, `WebApi`,
`Gateway`, `Data`, `Domain` (the shared brain), `ModelContextProtocol`, `Chat`, `Shared`, `ServiceDefaults`,
`AppHost`, all prefixed `CarTracker.`. Auth: Auth0 JWT is the fallback policy; the static API key fronts only
anonymous meta and docs; MCP `AssistantToken` bearers carry read or read-write scope claims.

## Vehicle facts

BT53 AKJ: 2003 Land Rover Freelander 1, 1.8 SE, K-series petrol, manual, AWD via viscous coupling. Bought
14 Mar 2026 at 76,632 mi. The K-series head gasket (weekly oil-cap and coolant checks are its early warning)
and the VCU (wheelspin can seize it) drive much of the design. Coolant is OAT only, never mixed with IAT.
