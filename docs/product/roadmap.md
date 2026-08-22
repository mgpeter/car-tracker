# Product Roadmap

> This roadmap is the authority on build order. It began as README §7's seven steps, grouped into phases;
> that section now lives here rather than in two places. Do not reorder without saying why.
>
> **Current as of 2026-08-21, at `VERSION` 0.20.1.** Update this line when you update the file - an authority
> with no dateline cannot be checked against anything, and every other date here is an inline event date on a
> single bullet, which tells a reader when *that* shipped and nothing about whether the rest is still true.
>
> **Test counts on the phase-completion lines are snapshots at that date, not running totals** - the same
> convention CLAUDE.md states at its head. The current suite is **631 front-end** and **312 Domain, 285 Data,
> 61 Chat** (measured 2026-08-20); the "236 .NET tests, 255 front-end" on the Phase 2 line is what Phase 2 finished with, and is
> roughly half the present figure.

## Phase 1: Foundation

**Goal:** A schema that cannot store a stale derived value, with the shared brain that computes them proven by tests.

**Success Criteria:** The derived-metrics service reproduces every Dashboard figure that the old sheet got *right*, and the known-bad figures resolve to their verified values (MOT 8 Jul 2027, 556.47 L, fuel YTD £888.86, mileage 80,712, and the fabricated first-fill interval - five since DEC-012) - against a hand-authored fixture (DEC-008). Non-monotonic mileage is reported alongside the derived value, not swallowed.

### Features

- [x] EF Core data model - all 14 entities per `docs/specs/2026-07-14-core-data-model/`, vehicle id on everything from the start `L`
- [x] Migrations + seed data - global reference data only (13 expense categories); vehicles are never seeded, they arrive via the add-car flow or MCP (DEC-007) `S`
- [x] `data_anomalies` - write-path validation flags with a lifecycle, per spec §5.3 (DEC-008 rehomed this from the importer) `S`
- [x] Derived-metrics service - mileage, MPG, L/100km, spend rollups, cost-per-mile, days-to-renewal, check status, budget variance `L`
- [x] Unit tests on derived metrics - hand-authored workbook fixture, including the defects as regression cases `M`

**Phase 1 complete, 2026-07-15.** 206 tests. The defects resolve against the hand-transcribed workbook
fixture, and `AnomalyDetector` raises exactly one anomaly on the real history (the 83,000 mi row).

### Dependencies

- `archive/ORIGINAL-TRACKER-IN-EXCEL-Freelander_BT53AKJ_Tracker.xlsx` is the source of truth the fixture is transcribed from and checked against
- Postgres running via Aspire / docker-compose

## Phase 2: Daily Loop

**Goal:** Make the phone-in-the-driveway case faster than the spreadsheet it replaces, and put the live Dashboard in front of it.

**Success Criteria:** A fill-up can be logged from a phone in under 30 seconds and its MPG appears immediately on the Dashboard. Every Dashboard figure is computed on read. The spreadsheet stops being opened.

### Features

- [x] Solution scaffold - 9 projects, Aspire AppHost, YARP gateway on one origin, OpenAPI + Scalar, API-key auth, Vite React app with the key in localStorage (DEC-009) `M`
- [x] Vehicle API - `POST /api/vehicles` (via `VehicleFactory`, so the opening reading is guaranteed) and `GET /api/vehicles/{reg}/summary` returning every derived figure. Landed 2026-07-15 alongside Phase 1, because until it existed nothing the domain computes was observable outside the tests `S`
- [x] Design system foundation - Tailwind theme tokens (`@theme inline`), `.woff2` fonts extracted per DEC-010, status treatment (stripe + mono label first, colour second) `M`
- [x] Garage homepage - one card per vehicle with status badge and attention summary, vehicle switcher (DEC-007) `M`
- [x] Add-car flow - vehicle form plus check-source choice: empty / generic starter set / copy from existing `M`
- [x] Dashboard - every computed value from spec §3.1, per vehicle `L`
- [x] Fuel log + quick-add - on-the-fly MPG, outlier warning, auto-mirror to expenses `M`
- [x] Expense log + quick-add `M`
- [x] Mileage readings log + quick-add `S`
- [x] Regular checks - computed status, "mark done today", batch weekly walk-around `M`

**Phase 2 complete, 2026-07-16.** 236 .NET tests, 255 front-end. Seven screens live on BT53's real history:
its two policies, one check definition and all 13 fuel fills were entered by hand through these screens, which
is how the write paths got used in anger before an agent touches them.

The five defects now reproduce against live data rather than only the fixture - 556.47 litres against the
sheet's 1,112.94, worst MPG 25.42 against its 24.49, 12 measurable intervals against its 13.

Three amendments this phase made to its own line items, each recorded where it bites:

- **"red <30 days / amber <60" was a colour-only statement of a rule**, and the dashboard's legend rendered
  those two words in `--accent`, so "red" printed orange. The thresholds are the information; the labels are
  Not set / OK / Due soon / Due / Expired (`lib/renewal.ts`). `Overdue` is a *check's* state and never a
  renewal's, and `RenewalUrgency.Red` covers both "due in 23 days" and "expired 12 days ago" - so urgency
  alone cannot label one.
- **Quick-add landed with the checks screen, not the fuel screen.** Three of its four buttons had no sheet
  behind them until then, and a band with one live button is worse than no band.
- **`<DataTable>` was extracted at the third consumer**, not designed up front, and its reflow is a container
  query rather than the design's viewport breakpoints. Checks stayed a list: no columns worth aligning.

### Dependencies

- Phase 1 derived-metrics service
- `archive/dashboard-full-claude-design/` - 17 screens plus a shared `theme.css`/`fonts.css`, the reference for the whole port. `archive/dashboard-design-idea/dashboard.html` is the superseded single-screen concept.
- `archive/Sample-design-and-road-trip-tracking-green-lane-field-manual.html` for the visual identity

## Phase 3: Full Coverage & Reminders

**Goal:** Retire the spreadsheet entirely - every remaining sheet has a home - and stop requiring the app to be opened to learn something needs attention.

**Success Criteria:** No sheet in the workbook lacks an equivalent view. A renewal or overdue check surfaces without being looked for.

### Features

- [x] Tasks (DIY + Workshop) - grouped by status, bundle-for-garage with summed cost, promote-to-service-record `L` - screens shipped in Phase 3; **promote-to-service-record shipped** 2026-07-19 (2026-07-16-task-service-promotion): a done Workshop task converts through ServiceRecordFactory (record + reading + mirrored expense), stamping the task's ServiceRecordId; guarded on Workshop/Done/not-already-promoted
- [x] Service history, tyre readings, wash log `M` - screens shipped in Phase 3; **wash cadence bar + tyre corner diagram shipped** 2026-07-19 (2026-07-16-wash-tyre-visualisations): a CSS cadence bar showing today against the 21–28 day window with a due-axis status pill, and a CSS car-body layout of four corner cards + a full-width spare, with a tread warn near the 1.6 mm MOT limit. Presentation only, no schema
- [x] Budget - editable targets, derived YTD, variance highlighting, period toggle `M` - shipped in Phase 3 (`BudgetPage.tsx`, `BudgetEndpoints.cs`); the period toggle covers calendar year / rolling 12 months / since purchase
- [x] Issues watchlist + equipment inventory `M` - shipped in Phase 3 (`IssuesPage.tsx` with the Monitoring/Resolved filter, `EquipmentPage.tsx` grouped by category); the issues screen later gained the head-gasket watch (2026-08-07)
- [~] Vehicle info / settings - fluid specs, tyre pressures, reference list management `M` - **reference-list management shipped** 2026-07-19 (2026-07-16-settings-reference-lists): garages/wash-locations/categories editable with FK-aware rename-cascade and guarded delete (block-or-rehome), system/Fuel locks, and a check-definition editor (retire via IsActive, guidance/order). **Fluids and tyre pressures are writable through the API and MCP** (`set_fluids`, `set_tyre_specs`, both via `VehicleUpdateService` with `FluidsPatch`/`TyresPatch`); what is still missing is only the **web form** - `VehicleInfoPage` links "Edit in settings →" and no settings panel answers it
- [x] Form validation + frictionless data entry `M` - shipped 2026-07-19 (2026-07-19-form-input-ergonomics): inline per-field validation across all ~17 add/edit sheets (the server's existing RFC 9457 `errors` map rendered against fields with a red outline + plain message, replacing the generic "Bad Request" banner), record dates defaulting to today with "+6 months"/"+1 year" quick-fill on forward-looking dates, and a hand-rolled recent-value `Combobox` on every place field (garages/wash-locations from reference GETs, station/vendor/etc. from record history). No schema or endpoint change
- [x] Documents - upload, tag, link to record, viewer/download `M` - shipped 2026-08-07 (2026-07-16-documents):
  the seventeenth and last workbook screen. Content-addressed storage on the mounted volume (DEC-005), papers on
  `<DataTable>` and photo sets as a grid, chips from `DocumentType` + the three link FKs, and an authenticated
  blob seam because a bearer-authenticated app cannot serve bytes through a plain `<img src>`. No schema change:
  `Document` and its configuration already existed
- [x] Data integrity - the anomaly queue: Open → Corrected / Accepted / Dismissed with a resolution note. Phase 1 produces the flags and `data-integrity.dc.html` designs the screen; it had no roadmap home until 2026-07-15 `M` - shipped in Phase 3 (`DataIntegrityPage.tsx`, `AnomalyEndpoints.cs`), with the auto-reconcile lifecycle following 2026-07-16
- [x] Reminders background job - spec §4, pluggable channel `M` - shipped 2026-07-19 (2026-07-16-reminders-engine): pure evaluator over the derived summary, hosted `BackgroundService`, `INotificationChannel` seam with the in-app badge adapter, `GET .../reminders`, shell badge. Email/push/MCP left as named registration points - **the channel choice itself is settled** (DEC-006, accepted on the strength of the shipped `InAppBadgeChannel`; see Dependencies below). Unbuilt adapters behind a seam is a different statement from an open decision

**Phase 3 complete.** Every screen has a home and the reminders engine runs. The one line still carrying a
`[~]` is vehicle info, and only its web form is missing - the capability itself ships through the API.

### Dependencies

- Phase 2 design system and CRUD patterns
- Notification channel decision - **settled: the in-app badge** (DEC-006, accepted on the strength of the
  shipped `InAppBadgeChannel`). Email and push remain unbuilt registration points behind `INotificationChannel`,
  which is a different statement from the choice being open

## Phase 4: MCP Server

**Goal:** The differentiator. Make the assistant a first-class client of the same domain, able to answer live and log on your behalf.

**Success Criteria:** "What needs my attention?" returns the same answer the Dashboard shows, because both called the same service. A spoken fill-up appears in the browser immediately, audited as `source = "mcp"`.

### Features

- [x] MCP host - in-process, **Streamable HTTP** transport `M` - the original "HTTP/SSE" was superseded by DEC-014; `McpServerRegistration.cs`, `MapMcp("/mcp")`
- [x] Read tools - spec §5.2, `get_due_items` first; `list_vehicles` plus optional vehicle param with default-vehicle fallback (DEC-007) `L` - 19 tools across `SummaryReadTools` (6), `LogReadTools` (12), `VehicleReadTools` (1)
- [x] Write tools - spec §5.3, mileage validation, auto-mirroring, `source = "mcp"` audit, same optional vehicle param `L` - 30 tools; the catalogue later grew edit/delete beyond the original add-only boundary (see DEC-014's amendment)
- [ ] Enter the spreadsheet history via agent - the workbook in `archive/` is the reference; supervised (DEC-008) `M` - **the one Phase 4 item genuinely still open.** History is being entered by hand as screens land; still to come: expenses beyond the fuel mirror, the remaining check definitions, service history, tyres, washes
- [x] Token scopes - read-only and read-write, bearer auth `M` - `AssistantToken` + migration `AddAssistantTokens`, `McpRead`/`McpWrite` policies, minted in Account → Assistant access
- [x] Tool description pass - explicit and example-rich; structured JSON plus a short human summary `S` - every tool carries a worked `[Description]`, and `McpResult<T>` is exactly that pairing

**Phase 4 complete, 2026-07-20**, apart from entering the workbook history. `docs/mcp-connect.md` is the
connection recipe.

### Dependencies

- Phase 1 derived-metrics service (read tools call it directly)
- Phase 3 write paths exist and are validated
- HTTPS termination - the token must never cross plaintext. **Still outstanding:** the shipped stack serves
  plain HTTP on the NAS, so this dependency is satisfied only on a deployment that fronts the gateway with TLS,
  which since DEC-020 means the shared host rather than anything in this repository

## Phase 4.5: Accounts and Ownership

**Added retrospectively (2026-08-07).** This shipped 2026-07-24 as the largest architectural change in the
project and had no line on this roadmap at all - which is precisely the drift a build-order authority exists
to prevent. Recorded here so the sequence reads true.

**Goal:** Real accounts, and vehicles that belong to someone.

- [x] Auth0 login - SPA client on tenant `usualexpat.uk.auth0.com`, API audience `cartracker.api`, `AuthGate` above the router, bearer injected at the single `client.ts` fetch seam `L`
- [x] Ownership - a `User` keyed by the Auth0 `sub`, nullable `Vehicle.OwnerId`, per-owner unique indexes, migration `AddUsersAndOwnership` `M`. ~~The first user to sign in claims pre-existing unowned vehicles~~ - **retired 2026-08-14 (DEC-018)**, and left struck through rather than deleted because it shipped and ran for three weeks. Adoption is now an explicit `Ownership:ClaimUnownedVehiclesFor` external id matched exactly, **defaulting to nobody**: right for the single-user migration it was written for, a trap the moment a stranger can be first through the door. See the gate at the foot of this file
- [x] The invitation door - sign-up is behind `Signup:AllowedEmails` / `Signup:AllowedDomains` over addresses the tenant has **verified**, checked before an unseen `sub` is provisioned, and **an empty allowlist means closed** `M`. Shipped 2026-08-14 in the same commit as the reference lists. The address is not in the access token, so it is read from the Auth0 **Management API** at provisioning - which makes one credential gate two things: with `Auth0:Management:` unset, sign-up is closed *and* account deletion refuses. Recorded here because Phase 4.5 is where a reader looks to find out who can get an account, and it was the one part of that story this section did not carry
- [x] Enforcement as **one global EF query filter** on `Vehicle`, not an ownerId threaded through ~35 call sites - a new endpoint cannot forget to filter, because a vehicle you do not own never resolves `M`
- [x] Per-user reference tables - shipped 2026-08-14 (DEC-018), and **not in the shape this line recorded**. `Garage`, `WashLocation` *and* `ExpenseCategory` - which this line never named, and which had the identical defect - are keyed **`(OwnerId, Name)`** with their **six** foreign keys dropped, rather than the surrogate id + repointed columns described here. The columns stay `varchar` carrying names, so no DTO, search field, MCP argument or rendered column changes. Migration `AddPerOwnerReferenceLists` (hand-ordered SQL, one-way, asserting `users` count ≤ 1 before it backfills) `M`

## Phase 5: Ship & Harden

**Goal:** Make it survivable - running unattended, backed up, and recoverable.

**Success Criteria:** A restore from backup reproduces the DB and documents. One-click export back to Excel/CSV keeps parity with the old workflow as a safety net.

### Features

- [~] Backup - `pg_dump` on a timer plus documents folder copy to a second location `M` - **the database half ships** (`db-backup` sidecar, 6-hourly, 7/4/6 rotation, restore recipe documented). The documents half is now *possible*: until 2026-08-07 the compose stack mounted no documents volume at all, so uploads were written inside the container and destroyed on every auto-update. The volume exists now; the **off-host copy is still manual** (a Hyper Backup target), not automated
- [~] Export to Excel/CSV `M` - **the export ships, in JSON rather than a spreadsheet** (2026-08-14): `GET /api/account/export` streams every row the account owns - all 15 per-vehicle tables, the three reference lists, the assistant tokens without their secrets, and the write-audit trail - as one attachment, driven from Account → *Your account*. It carries **no calculated figure by rule** (see DEC-018): a derived value written into an archive is the workbook's five defects reproduced in the one artefact read later, when nothing can recompute it. That is UK GDPR Art. 15 and Art. 20 satisfied. **What is still open is this line's original claim** - "parity with the old workflow as a safety net" meant a spreadsheet you could open, and JSON is not that. A CSV-per-table or `.xlsx` rendering is unbuilt and needs a package this repository does not carry
- [x] Import an export back in `M` - **shipped 2026-08-19** (`docs/specs/2026-08-19-account-data-import/`): `POST /api/account/import/preview` reads an export file, reports exactly what it would do and writes nothing; `POST /api/account/import/{importId}/commit` writes it, in one transaction, against an opaque server-held id. Driven from Account → *Your account*, beside the download. That closes the half of Art. 20 an export alone leaves open - a file readable by a person and by nothing else - and it is what makes moving hosts, or taking on a car whose history already exists, something other than re-typing four years of logs. **The rows are inserted, not replayed**: running the file through the factories would fire the four expense mirrors a second time against rows the file already carries. **A registration you already own is imported under a modified one** (`BT53 AKJ-2`), proposed by the server and editable in the preview. Document rows, assistant tokens and the write-audit trail are deliberately not imported, and anomaly flags are re-derived once the rows land. No schema change and no migration
- [x] Docker packaging - compose with gateway + API + Postgres, env config `M` - shipped and then some: two Dockerfiles, CI publish to Docker Hub, `VERSION`-driven release scripts, Watchtower auto-update, healthchecks, host bind mounts, and `docs/deployment-synology.md`
- [~] Harden auth - the static API key exists from the scaffold (DEC-009); this is rotation, HTTPS-only, and deciding whether cookie/proxy auth is still wanted `S` - **overtaken by Phase 4.5**: the "is cookie/proxy auth wanted" question was answered by shipping Auth0, and the API key now grants no vehicle access. What remains from this line: API-key rotation, and HTTPS-only
- [ ] HTTPS + deployment hardening `S` - **deployment hardening done** (bind mounts, healthchecks, `restart: unless-stopped`, Watchtower scoped by label so Postgres is never auto-updated). **HTTPS is not**: the stack serves plain HTTP on `${GATEWAY_PORT}`, and README §6 calls HTTPS mandatory because the MCP endpoint carries a bearer token. **The route changed on 2026-08-18 and the destination did not** (DEC-020): rather than DSM's reverse proxy, the app becomes one tenant of a shared host that terminates TLS for several projects, and the host lives in its own repository. What is left *here* is the tenant shape - external `edge`/`data-cambelt` networks, no published ports, the self-contained stack behind a `standalone` profile - and still no code change to the app itself

### Dependencies

- Phase 4 (MCP endpoint shapes the reverse-proxy and TLS requirements)

## Deferred

The nice-to-haves, formerly README §8 and now maintained only here. Not scheduled; revisit once the daily
loop is proven.

- ~~Fuel price / MPG / spend trend charts~~ - shipped 2026-07-19 (2026-07-16-trend-charts): a hand-rolled `TimeChart` SVG primitive (axes + derived accessible name + greyscale-safe multi-series) plots MPG and price over time on the fuel screen and cumulative spend by category on expenses, the last point reconciling with the recorded total. No chart library (strict CSP, small dep surface); no contract change
- ~~Head-gasket watch - checks as an issue's early-warning~~ - shipped 2026-08-07 (2026-07-16-head-gasket-watch):
  an `issue_watch_checks` join lets an issue name the regular checks that are its early warning, so the issues
  screen shows "Resolved, contingent on 2 checks · 1 lapsed" and the dashboard's attention panel names the watch
  above the generic overdue count. `WatchCalculator` reads `CheckStatusCalculator`'s existing per-check state and
  adds no arithmetic; a lapsed watch is flagged and never reopens the issue
- ~~DVLA/MOT lookup to auto-refresh expiry from the reg~~ - shipped 2026-08-07 (2026-07-16-dvla-lookup, DEC-015):
  `GET /api/vehicles/lookup/{reg}` calls DVLA VES + DVSA MOT History server-side and pre-fills the add-car sheet;
  the MOT date lands on `MotExpirySeed` (never a stored countdown) and the tax date on `VedExpiry`. **Dormant
  until API keys are provisioned** - with none it answers 503 and manual entry is untouched, which is CI's and
  every fresh checkout's state
- ~~Receipt photo capture pre-filling an expense~~ - **absorbed into the in-app assistant, below** (2026-08-07). Its own spec (`2026-07-16-receipt-photo-capture`, deleted) had the owner reading the photo and typing the figures, and named "the MCP assistant reading the attached photo" as one of two routes to real extraction. That route won; manual transcription was not wanted. Its two load-bearing rules moved into the assistant spec: a wrong auto-filled amount silently entered is worse than a typed field, and Fuel stays mirror-only. **Barcode scanning** - a product/VIN code, a different capability that was always awkwardly paired with this one - remains unspecced and unscheduled
- ~~Estimated tank range on the Dashboard (not just via MCP)~~ - shipped as **full-tank** range (2026-07-18-dashboard-derived-extras); "remaining" is out (tank level is untracked by design)
- Fleet spend rollups on the garage (cross-car totals - explicitly excluded by DEC-007, revisit if wanted)
- ~~Service-interval templates suggesting "next due" automatically~~ - shipped (2026-07-18-dashboard-derived-extras)
- ~~Fuel-economy units toggle (MPG ↔ L/100 km)~~ - shipped (2026-07-18-dashboard-derived-extras)

- ~~Free-text search on the log tables~~ - shipped 2026-08-09 (2026-08-08-log-table-search): the deferred third
  of `2026-07-16-log-table-filters`, which was titled "Filter, Sort & Search" and shipped two of the three.
  Search lives **inside `useTableView`** rather than beside it, because `count`/`total`/`filtered` derive from
  the filter state and a search narrowing rows anywhere else would desync the "N of M" the strip renders. A
  query matches every text field a row carries **including ones no column shows** - service `notes` holds the
  MOT advisories. Six screens wired; **service history gained the filter strip it never had**, with a
  `serviceDate` sort (id tie-break) reproducing its old hardcoded `reverse()` and the filter-miss empty state
  it lacked. Client-side over rows already fetched: no schema, no endpoint, no contract diff, no debounce

- ~~Public landing page for signed-out visitors~~ - shipped 2026-08-09 (2026-08-09-public-landing-page): the
  login wall's splash replaced with a welcome that says what the app is, shows two real screens and offers
  sign-up alongside log-in. Renders in place of `AuthGate`'s signed-out branch, **above the router**, so the
  boundary that stops any screen rendering before Auth0 confirms a session is untouched - at the cost, stated
  rather than hidden, that the page has no URL of its own. Reverses `design-brief.md:347`, which forbade
  exactly this and was written before Auth0

## Before sign-up can be opened to the public

**Three gates, none of them addressed by the landing page.** The page itself is safe to ship - it is a better
signed-out experience for a single owner too. **Opening registration to strangers is not**, until these clear.

**Two of the three closed 2026-08-14** (`docs/specs/2026-08-11-pre-public-release-gates/`, DEC-018). **HTTPS
did not**, so registration stays shut.

- [x] **Per-user reference tables** `M` - closed, and **this line understated it**. It said "one user can
  rename or re-home another's data", which reads as untidiness. It was a **cross-tenant write**, armed by the
  second account rather than the hundredth, and the test written before any fix showed the worst of it:
  renaming a shared garage rewrote another owner's service records and workshop tasks into a name they never
  chose, **and blanked their `vehicles.default_garage` to NULL** - because `context.Vehicles` *was* filtered,
  so their row was correctly left out of the repointing, and then the `SetNull` foreign key erased the field
  when the old row was dropped. **Partial scoping was worse than none**, which is why the composite key and
  the FK drops were a prerequisite of scoping the cascade rather than a tidy-up after it. Also: this gate
  never named `ExpenseCategory`, which had the same defect twice over. Shipped as `(OwnerId, Name)` with all
  six FKs dropped - see Phase 4.5 above and DEC-018 for why the recorded surrogate-id shape was rejected
- [ ] **HTTPS** `S` - **still open, and now the only gate.** README §6 calls it mandatory because the MCP
  endpoint carries a bearer token, and the shipped stack serves plain HTTP. Already tracked in Phase 5; listed
  again here because a public sign-up over cleartext is a different order of problem from a private one. It is
  met by fronting the gateway with TLS and re-registering the `https://` origin in Auth0 - no code change,
  which is why nothing in this repository will tell you it has not been done.
  **Since 2026-08-18 that is structural rather than incidental** (DEC-020): the host moved to its own
  repository, so this gate is now closed somewhere else and observed nowhere here. `2026-08-11-cambelt-azure-deployment`
  keeps the app's half - a compose file that is a good tenant, and the two things a host can break that the
  app cannot check for itself: an unbuffered `/mcp`, and documents travelling with the dumps
- [x] **DEC-016's first-user-claims-all-unowned-vehicles** `S` - closed by **retiring the behaviour**, not by
  checking for unowned vehicles. Adoption is now an explicit `Ownership:ClaimUnownedVehiclesFor` external id
  and happens only when the provisioning `sub` matches it exactly; **the default is null - no adoption,
  ever.** A second guard landed with it: sign-up is behind an allowlist (`Signup:AllowedEmails` /
  `Signup:AllowedDomains`) checked before an unseen `sub` is provisioned, and **an empty allowlist means
  closed** - the fail-safe direction, and the opposite of the natural reading. ~~That allowlist is what keeps
  strangers out.~~ **Superseded 2026-08-22 (DEC-022)**: sign-up is open by default and the allowlist now
  applies only under `Signup:Mode=InviteOnly`. What keeps a stranger from costing anything is the plan below,
  not the absence of an account. Left struck through rather than deleted, because it shipped and ran for
  eight days and the polarity it recorded is the one people will remember

- [x] **Open sign-up, and the three allowances that made it safe** `M` - shipped 2026-08-22, DEC-022.
  `Signup:Mode` defaults to `Open`; `IAccountEntitlements` resolves a `Free`/`Pro` plan per request from
  `Plans:CompEmails`/`CompDomains` against a **verified** address, and bounds the three surfaces that cost
  money or somebody else's quota: the assistant (off on Free), documents held per account (100 / 2,000) and
  DVLA lookups a day (3 / 50). Nothing about the plan is stored - no column, no webhook to go stale - so a
  subscription becomes one extra step inside the resolver. New: `User.EmailVerified`, `vehicle_lookup_usage`,
  migration `AddAccountPlans`. **The two exposures the landing-page gates never named are closed here** - a
  free account could previously have filled the documents volume 25 MB at a time and spent the DVLA quota
  every other account shares

### What the law wants, written down once

Opening sign-up makes this a controller of other people's data. Three UK GDPR articles have concrete endpoints
now, and a fourth obligation has neither. Recorded here so the next reader does not rediscover them from first
principles:

- **Art. 15 (access)** and **Art. 20 (portability)** - `GET /api/account/export`. One file, every stored row,
  no derived figure, no token secret. Document *files* are excluded and the export says so in its own `notes`.
  Since 2026-08-19 the file reads back in through `POST /api/account/import/preview` and its commit, which is
  the half of portability an export on its own does not satisfy: a right to *take* your data somewhere is
  worth what the somewhere can do with it.
- **Art. 17 (erasure)** - `DELETE /api/account`, gated on typing your own email. Data first inside one
  transaction, then the document folders, then the Auth0 identity; a failed identity call queues a
  `pending_identity_deletions` row for an hourly retry rather than leaving the rows behind. With
  `Auth0:Management:` unconfigured it **503s and deletes nothing**, because a half-erasure that leaves a
  login is worse than a refusal that says which credential is missing.
- **Art. 5(1)(c)/(e)** are the ones with no endpoint and no plan. Nothing expires, nothing is minimised, and
  a retention policy is a decision nobody has made. Not a blocker for one owner; it becomes one the day this
  holds a stranger's data.

## Shipped since the phases above

- **In-app chat assistant** (2026-08-14, `0.14.0`) - `docs/specs/2026-08-06-in-app-chat-assistant/`, DEC-019.
  **The build shipped; the spec is back in progress** - see the outstanding paragraph at the foot of this entry.
  The MCP tools pointed at the web UI: a docked panel above 900 px, a `/:reg/assistant` route below it, streamed
  over SSE. **Reads run; writes stop and ask** - every write tool is an `ApprovalRequiredAIFunction`, so the
  loop suspends and the only thing that can run one is a `/confirm` naming a server-held id. The draft card is
  an add sheet pre-filled from the tool's own JSON Schema, and what the owner corrects is what runs. One shared
  catalogue across `/mcp` and the chat (`CarTrackerToolCatalogue`, held together by a drift test), one provider
  seam (`Microsoft.Extensions.AI.IChatClient`), a frozen and cached system prompt, and a daily token ceiling per
  account and across the deployment kept in a table rather than in memory. **Off without `Chat:ApiKey`** - the
  endpoints 503 and no entry point is rendered.
  **Outstanding, and it is mostly measurement rather than build:** the model is defaulted to `claude-sonnet-5`
  and has not yet been measured against `claude-opus-5` on BT53's paperwork; effort is defaulted to `medium`
  and not swept; and no photo-to-record conversation's cost has been read off `usage`. Task 8 of the spec holds
  those, and each needs photographs of the car's own documents rather than more code.
  **One item is not measurement and is the sharpest of them (task 10.2):** an afternoon of real transcription -
  38 turns - recorded **zero cache tokens**, 0 write and 0 read against 993,999 input. That is either a cache
  that is off or counters dropped in the streamed aggregation, and the two are indistinguishable from inside
  the app, because Anthropic reports a read by lowering `input_tokens`. It matters twice over: ~19k of prefix
  per turn is the difference between pennies and pounds, and the daily spending ceiling is denominated in
  exactly this number. Next step is the provider's own usage view;
  `The_streaming_path_reports_the_cache_too` is written, skipped with that reason on it, and goes green the day
  it is fixed.

## Specced but unscheduled

Written up in full, with tasks, and waiting on a decision rather than on other work. Neither had an entry here
before 2026-08-07, which is how "what is left to build?" became a question you had to read the code to answer.

- **Green-lane trips** - `docs/specs/2026-07-16-green-lane-trips/`. An outing log that prompts the wash reset
  and coolant recheck the field manual prescribes. **Gated on a DEC first**: it is net-new scope outside
  README §1–§8, drawn from the design's origin rather than the workbook. The map and the live TRO feed are
  explicitly out of its v1, so nothing about it needs an API key
- **Fluids / tyre-pressure settings form** - the missing web half of the Phase 3 `[~]` above. No spec; the
  API and MCP tools already exist, so it is a form over `FluidsPatch`/`TyresPatch`
