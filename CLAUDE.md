# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## State of play

**Phase 1, Phase 2 and most of Phase 3 are complete** (2026-07-16). 260 .NET tests, 321 front-end.
**All 17 screens exist except documents.**

**Partial-fill MPG + dashboard derived extras (2026-07-18).** Two specs landed together.
`docs/specs/2026-07-18-partial-fill-mpg/`: `FuelEntry.FillLevel` is load-bearing again as a hard binary —
Full/unrecorded closes the tank, Half/Quarter defer MPG to the next full fill and accumulate their litres, so a
partial no longer posts two wrong figures. `FuelEconomyCalculator` walks an open segment; on all-full history it
reduces byte-for-byte to before (fixture untouched). `docs/specs/2026-07-16-dashboard-derived-extras/`: a
nullable `FluidSpecs.FuelTankCapacityLitres` (migration `AddFuelTankCapacity`) feeds a derived
`VehicleSummary.FullTankRangeMiles` (avg MPG × tank, null when either is absent — no guessed 59 L); a
constant service-interval map pre-fills the service add sheet's next-due as an overridable suggestion; and a
localStorage MPG↔L/100 km toggle (`lib/fuelUnit.ts`, Settings → Appearance) flips every fuel surface incl. the
chart's plotted series and inverted good/bad. The one new write path: `UpdateVehicleRequest.Fluids`
(`FluidsPatch`) — nothing accepted a `FluidSpecs` field before.

**Edit & remove across the logs (2026-07-17).** Every log's entries are now correctable and removable from the
UI — click a row to open it seeded for edit, a two-step `<ConfirmButton>` in the sheet footer deletes it. Added
the missing endpoints (fuel `PATCH`; mileage/tyres/wash `PATCH`+`DELETE`; equipment `DELETE`) and moved
fuel/service edit+delete into their factories so the reading + mirrored-expense shadow invariants live beside
`CreateAsync`. Three fixes landed with it: the expense mirror-refusal now also blocks service-mirrored rows
(the DTO gained `ServiceRecordId`), expense `PATCH`/`DELETE` re-scan, and an expense's own mileage reading dies
with it on delete. **Anomaly auto-reconcile (2026-07-16 spec) shipped first as its prerequisite**:
`AnomalyScanner` now retracts an Open flag to `Corrected` (with a system note) when a scan finds its condition
gone, so no delete orphans a flag. `docs/specs/2026-07-16-anomaly-lifecycle-reconcile/` and
`docs/specs/2026-07-17-log-entry-edit-remove/`.

- **Data model** — all 15 entities (14 from README §2, plus `DataAnomaly`), explicit configurations, five migrations, the 13-category seed.
- **Domain** — the five calculators, `IDerivedMetricsService`, `VehicleFactory`, `AnomalyDetector`, `AnomalyScanner` (the detector's production caller), `FuelEntryFactory`, `CheckTemplate`. The five workbook defects resolve against a hand-transcribed fixture.
- **API** — ~20 endpoints: garage list, vehicle create/PATCH/summary, fuel, mileage, expenses, check definitions + logs, budget. Every write runs the detectors.
- **Front-end** — tokens, inlined fonts, theme, CSP, icon sprite, status axes, primitives, sheets, the shell (extracted once from 17 copies), a component gallery, typed codegen off the committed OpenAPI contract, TanStack Query, React Router.
- **Screens live** — 16 of 17: garage, add-car, settings, dashboard, fuel, expenses, mileage, checks, service history, data integrity, tasks, issues, tyres, wash, budget, equipment, vehicle-info. **Documents is the only one left** (it needs file upload, which nothing else does).
- **Scaffold** — nine projects, Aspire, YARP gateway on one origin, OpenAPI + Scalar, API-key auth.

`CarTracker.ModelContextProtocol` is **empty** (Phase 4). `<DataTable>` was extracted at the third consumer as
planned — fuel, expenses, mileage — and its reflow is a container query, because a table cares how wide *it* is,
not how wide the window is. Checks, issues, equipment and the integrity queue stayed lists: no columns worth
aligning, and forcing a table on prose is the wrong-abstraction failure the seam exists to avoid.

**Form validation + frictionless data entry (2026-07-19).** `docs/specs/2026-07-19-form-input-ergonomics/`.
Every add/edit sheet (~17) now marks bad fields inline instead of showing a generic red "Bad Request" banner.
The server *already* returned an RFC 9457 per-field `errors` map (documented in the contract); the client threw
it away and rendered only `detail`. Now `api/client.ts` reads the `errors` map onto `ApiError`, `lib/formErrors.ts`
(`reportApiError`/`fieldError`/`formError`) maps it to fields (lowercasing the server's inconsistent
`nameof`-vs-hardcoded keys; anything unmatched — dotted `Insurance.PeriodEnd`, collection-level `Targets`,
framework 400s — folds to a `_` footer banner so nothing is dropped), and the shared `Field` gained an `error`
prop that sets `aria-invalid` (red `--due` border + `--due-wash` ring) and shows a plain message. Each sheet
also runs a small client-side `validate()` for instant feedback, generalising the pattern `AddVehicleSheet`
already proved. **Dates:** `lib/date.ts` (`todayIso`/`addMonths`/`addYears`, `addMonths` lifted out of
`ServiceHistoryPage`); the primary date field defaults to today on *add* (edits keep their stored date); a
`DateQuickFill` ("+6 months"/"+1 year") sits under forward-looking dates (service next-due, task target).
**Lookups:** a hand-rolled accessible `Combobox` (type-new-or-pick-recent, `role="combobox"` + `listbox`, focus
opens, typing filters, free-type stands) on every place field — garage/wash-location from their reference GETs
via `api/reference.ts` (`useReferenceSuggestions`, ranked by `referenceCount`), and station/vendor/tool/tyre-
location/equipment-source from distinct recent values in the vehicle's own history via `lib/recentValues.ts`.
No schema or endpoint change; expense category stays a constrained `<select>`. 395 front-end tests.

**Wash & tyre visualisations (2026-07-19).** `docs/specs/2026-07-16-wash-tyre-visualisations/`. Presentation
over data the screens already compute — no schema, no endpoint, no arithmetic. `CadenceBar` draws where today
sits against the 21–28 day wash window (elapsed fill, highlighted target band, a "today · day N" marker, a
due-axis pill flipping Overdue past day 28 on the same `sinceLast > TARGET_MAX` rule the stat note uses).
`TyreCorners` lays the latest reading out as the car — four corner cards around a body silhouette plus a
full-width spare card that says "never logged · no tread target" (the asymmetric 5-pressures/4-treads model),
with a due-axis warn when a tread nears the 1.6 mm MOT limit. Both CSS, not SVG (Spark is the only hand-rolled
SVG and earns it by plotting a series; boxes and fills are CSS), rendered alongside the unchanged tables.

**Trend charts (2026-07-19).** `docs/specs/2026-07-16-trend-charts/`. The §8 charts the `Spark` sparkline
stood in for, built by generalising Spark rather than adding a library (strict CSP, small dep surface, and the
two hard parts — a *derived* accessible name and greyscale-legible markers — were already solved). `TimeChart`
is a hand-rolled SVG primitive: value axis, time axis, one-or-more series told apart by dash pattern and a
direct end-label (never colour alone), and a required caption the caller derives from the data. Fuel gets
MPG-over-time (plausible measured intervals only, honouring the units toggle) and price-over-time; expenses
gets cumulative spend by category whose final Total point reconciles with the recorded total by construction
(£1,103.67 = `totalSincePurchase`, verified). No stored aggregate, no contract change.

**Trend-chart styling + fuel-page unit toggle (2026-07-19).** The two *single-series* fuel trends took the
dashboard `Spark` look — green line, soft green area fade, and their two extremes marked on the good/bad axis
(`good='higher'|'lower'`: better extreme `--ok` green, worse one `--due` rust, flipping with the metric — max
is good for MPG, min for L/100 km and £/L). `TimeChart` branches on `series.length === 1`; the multi-series
expenses cumulative chart is untouched (sand/dash/end-label — a green fill would mud 4 overlapping series and
put the status axis on a spend chart). Each solo instance gets a `useId()` gradient id (two render per page).
And the MPG↔L/100 km toggle now sits inline in the fuel page's Fleet-stats header (`Seg` with a `seg-sm`
compact variant), the same `useFuelUnit` store as Settings → Appearance, so it flips every fuel surface live.

**Log filter/sort (2026-07-19, partial).** `docs/specs/2026-07-16-log-table-filters/`. README §3.2's
"filterable, sortable" logs, as the fourth `<DataTable>` seam extension: a `useTableView<T>` hook (rows +
predicate groups + sort keys → filtered/sorted rows + a live count; OR-within-group, AND-across) and a shared
`<TableControls>` strip, both beside `DataTable.tsx` — the table stays a pure renderer. **Fuel** (All / Last 30
days / Flagged-only chips, a data-derived station select, sort by date/MPG) and **expenses** (data-derived
category chips, a period select, sort by date/amount) are wired, with a **filtered total** on expenses computed
from the visible rows and rendered distinctly from the server's authoritative YTD rollup — the spec's one real
tension. No contract change; entirely client-side. **Tasks (the kanban board) and equipment (a list) are not
yet wired** — the strip is a different shape over each and is a documented follow-up (spec tasks 2.1/2.2/4.3).

**Task → service promotion (2026-07-19).** `docs/specs/2026-07-16-task-service-promotion/`. README §3.3's
one-click promotion, wired: `TaskPromoter` turns a Done Workshop task into a `ServiceRecord` through
`ServiceRecordFactory` (the same record + mileage-reading + mirrored-expense transaction AddService uses — never
a second three-row path), then stamps `task.ServiceRecordId`. Preconditions are distinct refusals (not Workshop
→ 400, not Done → 409, already promoted → 409). The odometer is supplied on the request (a task carries no
reading); cost defaults to the estimate but is editable (an estimate is not a receipt). `POST
/tasks/{id}/promote`; `TasksPage`'s sheet shows "Convert to service record" only on a Workshop/Done/unpromoted
task and "Converted → service history" once linked.

**Reference-list management (2026-07-19).** `docs/specs/2026-07-16-settings-reference-lists/`. `ReferenceWriter`
only ever created rows; `ReferenceListEditor` adds the edit/remove half. Garages, wash locations and expense
categories are keyed by name and pointed at by FKs that look like free text (`ServiceRecord.Garage`,
`WashEntry.Location`, `ExpenseEntry.Category`, …), and the garage/wash FKs are `SetNull` — so a delete would
*silently blank* referencing rows unless guarded. The editor counts references and **blocks (409 with the
count) or re-homes** before deleting; a **rename cascades** (new-named row → repoint FKs → drop old, one
transaction inside the retrying execution strategy, because changing a PK can't be an in-place update). System
categories are delete-locked and **Fuel is rename-locked** (the mirror resolves it by the exact constant).
`ReferenceEndpoints` grew GET/POST/PATCH/DELETE for garages + wash-locations and PATCH/DELETE for categories;
`ChecksEndpoints` gained `GET /definitions` (the status summary carries no guidance/isActive/order). Settings
now has a `ReferenceListsPanel` (rename + guarded delete with a re-home picker, Fuel shown Locked) and the
`CheckDefinitionsPanel` leads with **retire (IsActive toggle)** over delete-which-cascades-logs.

**Reminders engine (2026-07-19).** README §4's "phase 1.5" shipped as a UI-badge-first cut
(`docs/specs/2026-07-16-reminders-engine/`). A pure `ReminderEvaluator` reads the derived `VehicleSummary`
(renewals by urgency, checks/wash/tyre off `CheckStatusSummary`, service by date or mileage) — it re-derives
nothing, so the badge and the dashboard's attention panel are one figure. A hosted `RemindersBackgroundService`
wakes on `Reminders:Interval` (24h default), resolves a scope per tick, and fans `ReminderDispatcher` out to
every enabled `INotificationChannel`; the in-app badge is the only adapter, email/push/MCP are named
registration points DEC-006 leaves open. `GET /api/vehicles/{reg}/reminders?includeQuiet` lists fired items
with reasons; a `<ReminderBadge>` in the shell (`TopNav`) shows the firing count on the due axis. No schema,
no stored state — the badge is derived on read.

Left to do: **documents** (upload, the one thing no other screen needs), **promote-a-task-to-a-service-record**,
and the rest of Settings' reference-list management. Then Phase 4, the MCP server.

### Four bugs, one cause — read this before adding a screen

Every one of these came from hardcoding a guess instead of reading the source, and each is now sourced so the
build breaks instead of the page lying:

- **Expense categories** were hand-typed from the workbook's wording ("Repairs", "Road tax", "Cleaning",
  "Other"). The seed says `Repair`, `Tax`, `Wash`, `Misc`, `Tools/Equipment` — and the endpoint validates
  against that table, so 8 of 12 options 400'd on save. Now `GET /api/reference/expense-categories`.
- **`MileageOrigin`** was guessed as Manual/Fuel/Service/Expense/Mot. It is Manual/Fuel/Tyre/Wash/Service/
  **Purchase** — so BT53's founding reading rendered a raw enum name. Now `Record<Origin, string>` off the wire
  type.
- **`DataAnomaly.Detail` is JSON**, not prose: `{"mileage":83000,"currentMileage":80900}`. The screen rendered
  it raw while the test — which mocked prose — stayed green. `Message` is the prose.
- **`Garage`, `WashLocation` are keyed FK tables.** `ServiceRecord.Garage`, `MaintenanceTask.AssignedGarage`,
  `Vehicle.DefaultGarage` and `WashEntry.Location` all look like free text and are foreign keys. Their comments
  say "upserted by the importer" — and DEC-008 deleted the importer, so nothing upserted them: a 500 the first
  time anyone typed a new name. `ReferenceWriter` creates on first use, per CLAUDE.md's "created as used".

And the same shape again in the UI: **the plate is never the URL slug.** `plate={reg}` renders `BT53AKJ`; the
route param is normalised for matching and only the database holds the real registration. Fixed once on
settings in M1c, then written again on eleven more screens. `usePlate()` is the single source and
`coverage.test.ts` now fails the build on `plate={reg}`.

**BT53's history is being entered by hand, as each screen lands** — dogfooding the write paths before an agent
touches them. In today: its two policies, one check definition, and **all 13 fuel fills** (transcribed from the
xlsx Fuel Log, entered through the add-fill sheet and the endpoint behind it). Each fill mirrored into expenses
automatically, so fuel spend, the 14-reading mileage log and the odometer are all live and all derived. Still
to come: expenses beyond the fuel mirror, the remaining 17 check definitions, service history, tyres, washes.

The empty states that remain are **real, not bugs** — the design cannot show any of them, having 13 fills and
18 checks frozen in, which is exactly why they keep finding things.

```
dotnet run --project src/CarTracker.AppHost   # everything; app on http://localhost:5080
dotnet build
dotnet test          # needs Docker — Testcontainers starts a real PostgreSQL 17
dotnet ef database update --project src/CarTracker.Data   # honours CARTRACKER_CONNECTION
```

Tests run against **real PostgreSQL via Testcontainers, applying migrations** — not the in-memory provider,
which ignores column types, check constraints, and FK behaviour (i.e. most of what the schema asserts). Don't
swap it for speed.

### Things that cost hours once, and will again

- **`ASPNETCORE_ENVIRONMENT` must be Development or user-secrets do not load.** This produced three separate
  fake bugs in one session: an API returning 401 to a correct key, and an AppHost hanging forever. If
  configuration seems ignored, check the environment first.
- **User-secrets override `appsettings.json`.** A stale secret silently shadows an edited appsettings value.
- **An unresolved Aspire parameter blocks on a dashboard modal**, with *nothing* in the AppHost's stdout. If
  the log stops after "Login to the dashboard" and never says "Distributed application started", open the
  dashboard — it is asking you a question. Parameter defaults live in the AppHost's `appsettings.Development.json`.
- **Aspire resource logs go to the dashboard, not stdout.** The AppHost's own log is ~24 lines and tells you
  almost nothing. Reading stdout and concluding "wedged" is a mistake worth not repeating.
- **`WithDataVolume()` + a generated password** fails auth from run two onwards, because Postgres only reads
  the password on first init. Always pass an explicit password parameter.
- Aspire is **13.4.6**; the installed `dotnet new aspire-*` templates are **9.1.0** and emit `net8.0` plus
  hardcoded package versions that break under CPM. Hand-author AppHost csprojs.
- `D:\repos\personal\bookmark-feeder` is a **working reference** for this exact stack (Aspire 13 + YARP + Vite).
  When something here does not work and it does there, believe that repo.
- **`AddNpgsqlDbContext` is unusable here** — it pools, and a pooled context may only take
  `DbContextOptions<T>`; ours also takes a `TimeProvider`. Use `AddDbContext` + `EnrichNpgsqlDbContext`, in
  that order.
- **`EnrichNpgsqlDbContext` adds a retrying execution strategy, which refuses user-initiated transactions.**
  Any `BeginTransaction` must run inside `Database.CreateExecutionStrategy().ExecuteAsync(...)`. The tests do
  not catch this: the test context has no retry strategy, so `VehicleFactory` passed 41 tests and threw on the
  first real request.
- The WebApi **applies migrations on startup in Development only**. Aspire's database starts empty, and
  without it the first request is `relation "vehicles" does not exist`.

`README.md` is not a readme so much as the full specification (§1–§8), and it is the authority on scope. §7
gives the intended build order. Live specs are in `docs/specs/`; `docs/product/decisions.md` overrides
conflicting guidance here and is the first place to look when something seems contradictory.

## What `archive/` is for

These are load-bearing inputs, not historical clutter.

- **`ORIGINAL-TRACKER-IN-EXCEL-Freelander_BT53AKJ_Tracker.xlsx`** — the live system this project replaces, and
  the source of truth for the figures below. 13 sheets: Dashboard, Vehicle Info, Expenses Log, Fuel Log,
  Service History, DIY To-Do, Workshop To-Do, Regular Checks, Wash Log, Tyre Log, Budget, Issues Watchlist,
  Equipment. **Nothing reads it programmatically** (DEC-008 dropped the importer): its history is entered via
  MCP write tools by an agent, and its five bad figures are transcribed into a test fixture by hand. Check
  transcriptions against this file.
- **`Sample-design-and-road-trip-tracking-green-lane-field-manual.html`** — the origin of the visual identity.
  See Design language below.
- **`dashboard-full-claude-design/`** — **the design reference for the whole front-end.** 17 screens plus a
  shared `theme.css` (tokens + ~60 component classes) and `fonts.css` (135 KB base64).

  **These are not static HTML.** Each screen is a `<x-dc>` template with `{{ }}` bindings, `<sc-if>`/`<sc-for>`,
  and a `class Component extends DCLogic` carrying `state`/`setState`/`componentDidMount`. `support.js` is a
  runtime template-to-React compiler. The port is *unwrapping a bespoke JSX dialect into real JSX* — `sc-if` →
  `&&`, `sc-for` → `.map()`. `support.js` and `image-slot.js` are strippable harness.

  Things to know before trusting it: **`dashboard.dc.html` and `fuel-log.dc.html` do not link `theme.css`** —
  they inline forked copies that have already drifted. Its fuel sheet **contradicts the domain** (hardcodes an
  18–45 MPG band against our 10–70, and withholds MPG on partial fills — a rule the fuel-basis spec removed).
  13 of 17 screens are theatre: toasts describing writes that never happen. Everything is hardcoded, and there
  is no routing at all — links are flat filenames, and the registration never appears in a URL.

- **`dashboard-design-idea/dashboard.html`** — the **superseded** single-screen concept, kept for provenance.
  Built on the real figures at a reference date of 2026-07-14, demonstrating four of the five flags as a live
  "Import check" panel. Still the best statement of the status treatment: severity stripe + uppercase mono
  label first, colour second, so state survives greyscale. It also carries the comment the newer `theme.css`
  dropped — `--accent: /* structure only — never status */`.

## The central constraint

Spec §1: *every derived number must be computed server-side, never stored stale*. §4 requires one
derived-metrics service that both the web API and the MCP server call, so a metric can never disagree with
itself across surfaces. Multi-vehicle is active scope (DEC-007): the garage is the home screen, every entity
is vehicle-scoped, and vehicles are never seeded — they arrive via the add-car flow or MCP. Only BT53 AKJ
exists today.

Derived, never stored: current mileage (most recent `MileageReading` **by date** — not `MAX(mileage)`; see the
83,000 mi row below), per-fill MPG and L/100km, fleet MPG stats, spend rollups, cost-per-mile, days-to-renewal,
check status from last log + interval, budget variance.

## The five defects: the project's reason to exist, and its test fixture

The xlsx **Dashboard sheet holds stored derived values, and five of them are provably wrong.** This is the
evidence for the whole derived-never-stored premise, and the figures are **regression tests for the
derived-metrics service**, transcribed by hand into a C# fixture (DEC-008 — there is no importer; nothing reads
the file programmatically). All were verified against the underlying logs (reference date 2026-07-14):

| Dashboard says | Reality | Cause |
|---|---|---|
| MOT expiry 6 Aug 2026 (23 days) | 8 Jul 2027 (359 days) | Stale. Superseded by the MOT pass logged 8 Jul 2026 at 80,705 mi. Would show a red countdown for a renewal already done. |
| Total litres pumped 1,112.94 | 556.47 | Exactly 2.0000× — the summary double-counts all 13 fills. Anything downstream (range-per-tank) is out by half. |
| — | Service History row dated 27 Jun 2026 logs **83,000 mi**, above the current 80,712 | Mileage is not monotonic. Likely 80,300 mistyped. Spec §5.3 requires flagging this, not silently accepting it. |
| Fuel YTD £725.70 | Fuel Log totals £888.86 | £163.16 gap: Expenses Log carries one lumped "fuel to date" row instead of per-fill entries. Spec §3.2's auto-mirroring of fills into expenses is what closes it. |
| Worst MPG 24.49, and a 13-value Average MPG | Worst 25.42 over 12 measurable intervals | **The fifth, added 2026-07-15 by DEC-012.** Fuel Log row 4 (the *first* fill) carries "miles since last = 334" against 77,537 mi, implying a previous reading of 77,203 that exists nowhere — the purchase was 76,632. Two headline figures rest on an interval that never happened. |

**Not a defect — a definition difference (DEC-011):** average price per litre. The sheet takes a plain mean of
the price column (1.594923); this service weights by volume (1.597324). The sheet answers a different question
correctly, which is why it sits outside the count.

**Not a defect — a transcription note (found 2026-07-15, entering the 13 fills by hand):** the workbook's
Total column is `litres × price` **unrounded**, so row 6 reads £98.518 — not an amount anyone can pay. Its
£888.86 is therefore the sum of thirteen unpayable amounts, and £163.16 and 1.597324 above both derive from it.
Entered as real receipts, rounded to the penny as they would be paid, the same 13 fills total **£888.87** and
weight to **1.597337**. A penny, and it is the *live database* that differs from the figures above, not the
domain: check against 888.86 when reading the xlsx, and expect 888.87 from the running app. The C# fixture uses
the workbook's own values and is unaffected.

Also note **current mileage (manual) 80,705 is behind latest logged 80,712** — the sheet's "miles since
purchase" uses the manual figure. `MileageReading` (spec §2) exists precisely to decouple this; derive from the
latest reading.

Other facts about the workbook worth knowing when reading it by hand:

- The Regular Checks sheet has 18 rows but the Dashboard counts 17 — "Spare tyre pressure" has never been
  logged and falls out of the OK/due-soon/overdue buckets. **Never-logged is a real fourth state**, and the
  schema enforces it: `check_definitions` carries no status column, so the domain must handle the empty case.
- Expenses Log has ~30 trailing blank rows carrying a running-total formula. There is no running-total column
  in the schema; the replacement is `SUM()`.
- **Dates are Excel serials, epoch 1899-12-30** (46217 = 2026-07-14) — every date column is a bare integer.
  Nothing parses them any more, but you need this to read the file.
- Reference lists (expense categories, wash locations, garages) sit in side columns of their sheets. Only the
  13 expense categories are seeded; garages and wash locations are created as used.

## Design language

`archive/…green-lane-field-manual.html` establishes the identity. Reuse it rather than inventing a second one.

- **Type** — Oswald (display, uppercase, condensed), Inter (body), JetBrains Mono (all data/labels). Use
  `font-variant-numeric: tabular-nums` anywhere digits align.
- **Palette** — `--ink #1E241B`, `--paper #E8E2CF`, `--paper-2 #DFD8BF`, `--panel #F1ECDD`,
  `--green-deep #2F3D2C`, `--green #5E7A34`, `--orange #B85C29`, `--rust #A23B2E`, `--blue #3E6187`,
  `--sand #C9B588`.
- Orange reads as the structural accent (rules, eyebrows, section marks). When building status UI, keep the
  semantic axis separate from it, or the two fight: the dashboard concept uses `--green #5E7A34` OK,
  `#C79A22` due soon (the manual's yellow waymark), `--rust #A23B2E` overdue, and reserves `--blue #3E6187`
  for data-integrity flags, which are a different axis from due-status. §3.1 thresholds: red under 30 days,
  amber under 60.
- The field manual loads Google Fonts and Leaflet from CDNs; the dashboard concept inlines its fonts instead.
  Under a strict CSP the CDN version silently falls back to system faces, which is why the fonts got inlined.
- The manual numbers its sections 01–05 because a document has a reading order. Don't carry that into app UI —
  a dashboard is scanned, not read, and numbering it is decoration posing as information.

## Intended architecture (none of this exists yet)

Per README: .NET 10, PostgreSQL, React (Vite), Aspire, EF Core, Microsoft Agent Framework, docker-compose.
Projects planned under `src/`: `CarTracker.WebApp` (Vite React), `.WebApi`, `.Data` (EF Core model +
migrations), `.ModelContextProtocol`, `.Shared`, `.Domain` (domain logic and derived metrics — the shared
brain), `.AppHost` (Aspire).

The MCP server (§5) is the differentiator, hosted in-process in the same ASP.NET Core app over HTTP/SSE. It
reads the same domain service as the web UI. Two token scopes: read-only and read-write; every write logs
`source = "mcp"`. **Its package family is an open question** — `tech-stack.md` says "Microsoft Agent
Framework", the ecosystem package is `ModelContextProtocol.AspNetCore`; `CarTracker.ModelContextProtocol`
deliberately has no MCP dependency until Phase 4 decides.

`CarTracker.Gateway` (DEC-009) is the single public origin: `/` → the app, `/api` → the WebApi, `/scalar` and
`/openapi` → the WebApi, in dev and prod alike. **CORS is absent by design** — if you ever need it, something
has bypassed the gateway and that is the bug. The API key is `ApiKey:Value`, sent as `X-Api-Key`; only
`/api/meta` is anonymous.

## Vehicle facts worth knowing

BT53 AKJ — 2003 Land Rover Freelander 1, 1.8 SE, Rover K-series petrol, manual 5-speed, AWD via viscous
coupling (VCU). Bought 14 Mar 2026 at 76,632 mi. Two known frailties drive much of the spec's design: the
K-series head gasket (the weekly oil-filler-cap and coolant-colour checks are its early-warning system) and the
VCU (prolonged wheelspin can seize it and destroy the IRD/diff). Coolant must be OAT (red/pink) — never mixed
with IAT.
