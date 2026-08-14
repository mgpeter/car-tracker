# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## State of play

**Phases 1–4 are complete, plus the unplanned Phase 4.5 (accounts and ownership).** Current suite:
**273 Domain, 216 Data, 539 front-end.** **All 17 screens now exist** — documents, the last, shipped
2026-08-07. What is left: entering the workbook history, **HTTPS** — now the *only* thing standing between
this and public sign-up — an off-host copy of the documents volume, and two specced-but-unscheduled features
(in-app chat assistant, green-lane trips). The account-data export ships, in JSON; a spreadsheet rendering of
it does not. `docs/product/roadmap.md` is the authority and is current as of 2026-08-14.

> **Test counts below are snapshots at the date of the entry they sit in, not running totals.** They record
> what the suite was when that work landed. The current figure is the one above.

**Multi-user + Auth0 — core slice (2026-07-24).** The app was single-user (one shared `X-Api-Key` in
localStorage, every vehicle unowned, the garage listing all cars). It now has real accounts via **Auth0**
(tenant `usualexpat.uk.auth0.com`, SPA client, API audience **`cartracker.api`**). Schema: a new `User` (keyed
by the Auth0 `sub` in `ExternalId`), a nullable `Vehicle.OwnerId` FK, `AssistantToken.OwnerId`, and the two
globally-unique vehicle indexes (registration, default) **reworked per-owner** so two users can each own a
"BT53 AKJ" and each have a default (migration `AddUsersAndOwnership`; no reference-table change yet — see
below). Enforcement is **one global EF query filter on `Vehicle`** (`CarTrackerDbContext` +
`ICurrentUserAccessor`), *not* threading an ownerId to ~35 call sites: every child is reached only through an
already-owner-checked vehicle id, so a cross-user vehicle simply never resolves and the endpoint 404s — a new
endpoint cannot forget to filter. Backend auth: a `.AddJwtBearer("Auth0")` scheme beside the existing ApiKey +
AssistantToken; the **fallback policy now requires the Auth0 scheme** (the web login is the way in; ApiKey stays
registered but grants no vehicle access — it fronts only the anonymous meta/docs). `CurrentUserMiddleware` (after
`UseAuthorization`, where both the Auth0 and assistant-token principals are established) resolves the principal
to a local user — **JIT-provisioning** an Auth0 `sub` on first sight, and the **first user to ever sign in claims
all pre-existing unowned vehicles** (BT53) — and pins it on the accessor. MCP tokens carry their owner
(`AssistantClaims.UserId`); `add_vehicle` and the assistant token-management endpoints are user-scoped. Frontend:
`@auth0/auth0-react`, `Auth0Provider` in `main.tsx` with **`useRefreshTokens`** (rotation — no silent-auth
iframe, so the strict CSP needs only `connect-src` widened to the tenant, no `frame-src`), an `AuthGate` login
wall above the router, a bearer injected at the single `client.ts` fetch seam via `setAccessTokenProvider` +
`<AuthBridge>`, and a `UserMenu` (email + sign-out) in `TopNav`. Config in `lib/authConfig.ts` (`VITE_AUTH0_*`,
defaulting to this tenant; `.env.example` committed). Tests global-mock `@auth0/auth0-react` as signed-in in
`src/test/setup.ts`. **107 Data (+6 ownership), 206 Domain, 431 front-end.** Additive/empty contract diff. Plan
at `~/.claude/plans/snazzy-kindling-axolotl.md`. **User must still, in the Auth0 dashboard:** register the
gateway origins (`http://localhost:5080` dev + prod) in Allowed Callback/Logout URLs + Web Origins, and enable
refresh-token rotation. ~~**Deferred (its own next migration):** per-user **reference tables** — `Garage`/
`WashLocation` are still global; the chosen full isolation (surrogate id + `OwnerId`, repoint the four FK
columns, backfill) is the largest slice and lands next.~~ **Done 2026-08-14, and in a different shape** — see
the pre-public-release entry at the foot of this section. Not surrogate ids: composite `(OwnerId, Name)` keys
with the foreign keys dropped, and there were **six** of them across **three** tables, not four across two.
Two other clauses above are also stale: the first user no longer claims unowned vehicles (it is an explicit
`Ownership:ClaimUnownedVehiclesFor` subject, defaulting to nobody), and `User.Email` is now a real address
read from the Auth0 Management API rather than the `sub`.

**MCP server — Phase 4 (2026-07-20).** `docs/specs/2026-07-16-mcp-server/`, DEC-014. The domain is exposed as
in-process MCP tools over **Streamable HTTP** at `/mcp` through the gateway, on `ModelContextProtocol.AspNetCore`
(the package question is settled — *not* Microsoft Agent Framework, which DEC-017 later retired for the chat
half too, in favour of the official Anthropic SDK). Tools
live in `CarTracker.ModelContextProtocol` and call the same `IDerivedMetricsService` the web UI does, so the
assistant and the dashboard cannot disagree. **Read tools cover every screen** — the derived summaries
(`get_due_items` first, `get_vehicle_summary`, `get_fuel_status`, `get_spend_summary`, `get_check_status`,
`get_budget`, `get_data_integrity`) plus a raw `list_*` per log and `get_reference`/`get_open_tasks`/`get_issues`.
**Write tools** began as add/log + safe-updates only (`log_fuel_fillup`, `add_service`, `log_expense`,
`update_mileage`, `mark_check_done`, `log_wash`, `log_tyre_reading`, `add_task`, `complete_task`, `add_issue`,
`add_issue_observation`, `add_equipment`, `add_vehicle`, and the **vehicle-settings** tools `set_insurance` /
`set_road_tax` / `update_vehicle_profile` / `set_fluids` / `set_tyre_specs`) — **and later grew the edit/delete
half** (`update_*`/`delete_*` for fuel, service, mileage, tyre, wash and equipment), reversing DEC-014's
original "no edit or delete of existing rows via the assistant". That reversal is now recorded as an amendment
on DEC-014; the catalogue is 30 write tools (counted from the source, and it disagreed with this line — see
the 49-tool figure below, which is the one that was right). Each stamps `EntrySource.Mcp`,
running the same factory/service the web write uses, and returning any anomaly flags (monotonicity is flagged,
never rejected). The settings tools (added 2026-07-20, after dogfooding found the assistant could log an MOT but
not insurance/road-tax renewals) go through a shared `VehicleUpdateService` the web `PATCH /vehicles/{reg}` also
calls; they deliberately expose **no** MOT-expiry/status/default setter (MOT stays derived from the logged pass;
lifecycle stays web-only). The half of the write/read paths whose invariants sat inline in the
endpoints were **extracted into a shared application layer** in `CarTracker.Domain` (`ExpenseService`,
`LogQueryService`, `LogWriteService`, `TaskService`, `IssueService`, `CheckService`, `OdometerShadow`,
`VehicleResolver`, `WriteResult`), with the row DTOs lifted to `CarTracker.Shared/Logs/` — the endpoints refactored
to call them, so a list or a write is one path whichever surface hits it (the same seam a future in-app chat
reuses). **Auth: scoped bearer tokens** (`AssistantToken`, migration `AddAssistantTokens`), read-only vs
read-write, minted in Settings → *Assistant access* with the secret shown once; built on ASP.NET Core policies
(`McpRead`/`McpWrite`) that check scope *claims*, so a future Auth0/JWT scheme drops in without touching the tools.
`/mcp` requires `McpRead`; write tools carry `[Authorize(Policy="McpWrite")]` via `AddAuthorizationFilters()`, so a
read-only token is physically refused by every write tool. Every write is recorded in an `AssistantWriteAudit`
trail (a call-tool filter, keyed to the token); reads are counted on the token. Connection recipe in
`docs/mcp-connect.md`. Everything left of the original roadmap at that point — documents, head-gasket-watch,
dvla-lookup — shipped 2026-08-07; only green-lane-trips remains, and it is gated on a DEC.

**Check verdicts are a real status now (2026-07-21).** Bug: "checks can't log anything but OK — Attention/Failed
don't save." They *did* save — `CheckLog.Result` was **write-only**, surfaced nowhere. The checks screen showed
only the date-derived `CheckStatus`, which reads green "OK" the instant anything is logged, so a Failed verdict
looked lost. Fix carries the latest log's verdict onto the read model (`CheckState.Result`) and adds a **fifth
`CheckStatus.Attention`** — a check whose *latest* log recorded Attention/Failed escalates into it whatever its
date (verdict precedence; the date math is still returned so the row shows how overdue it also is), and clears
the moment a later log records OK. `CheckStatusCalculator` now keeps the whole latest log per definition
(ordered by date then id, so a same-day correction wins) instead of just `Max(PerformedOn)`. Because it is a
genuine status, it flows everywhere status does: the fifth `<StatTile>` (`.tiles-5`, Attention reuses the rust
`due` tone — no new colour, the label and the row's "flagged Failed" text carry it), `checksStatus`/
`overallStatus` tell-tales, the dashboard checks/attention panels, and `ReminderEvaluator` (a flagged check
fires like an overdue one). Also fixed the sheet's latent casing bug — its `<option>`s sent `"Ok"` where the
enum name is `"OK"`, working only by case-insensitive JSON parsing. Additive contract (`CheckState.result`,
`CheckStatus` gains `Attention`, `CheckStatusSummary.attentionCount`); no migration — the column and its
`ck_check_logs_result` constraint already accepted all three. Plan at `~/.claude/plans/snazzy-kindling-axolotl.md`.

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

- **Data model** — all 15 entities (14 from `docs/specs/2026-07-14-core-data-model/sub-specs/database-schema.md`, plus `DataAnomaly`), explicit configurations, five migrations, the 13-category seed.
- **Domain** — the five calculators, `IDerivedMetricsService`, `VehicleFactory`, `AnomalyDetector`, `AnomalyScanner` (the detector's production caller), `FuelEntryFactory`, `CheckTemplate`. The five workbook defects resolve against a hand-transcribed fixture.
- **API** — ~20 endpoints: garage list, vehicle create/PATCH/summary, fuel, mileage, expenses, check definitions + logs, budget. Every write runs the detectors.
- **Front-end** — tokens, inlined fonts, theme, CSP, icon sprite, status axes, primitives, sheets, the shell (extracted once from 17 copies), a component gallery, typed codegen off the committed OpenAPI contract, TanStack Query, React Router.
- **Screens live** — all 17: garage, add-car, settings, dashboard, fuel, expenses, mileage, checks, service history, data integrity, tasks, issues, tyres, wash, budget, equipment, vehicle-info, documents. Documents was the last, because it needed file upload and nothing else did.
- **Scaffold** — nine projects, Aspire, YARP gateway on one origin, OpenAPI + Scalar; auth is Auth0 (below), with the API key fronting only the anonymous meta/docs endpoints.

`CarTracker.ModelContextProtocol` holds **49 tools** (19 read, 30 write) — see the Phase 4 entry above.
`<DataTable>` was extracted at the third consumer as planned — fuel, expenses, mileage — and its reflow is a
container query, because a table cares how wide *it* is, not how wide the window is. It now has eight
consumers (those three plus service, tyres, wash, tasks and documents). Checks, issues, equipment and the
integrity queue stayed lists: no columns worth aligning, and forcing a table on prose is the wrong-abstraction
failure the seam exists to avoid.

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

**Log filter/sort (2026-07-19, complete).** `docs/specs/2026-07-16-log-table-filters/`. README §3.2's
"filterable, sortable" logs, as the fourth `<DataTable>` seam extension: a `useTableView<T>` hook (rows +
predicate groups + sort keys → filtered/sorted rows + a live count; OR-within-group, AND-across) and a shared
`<TableControls>` strip, both beside `DataTable.tsx` — the table stays a pure renderer. **All four logs wired**
(mileage joined later, so `useTableView` has five consumers; service, tyres and wash still have no controls).
**Fuel** (All / Last 30 days / Flagged-only chips, a data-derived station select, sort by date/MPG) and
**expenses** (data-derived category chips, a period select, sort by date/amount) shipped first, with a
**filtered total** on expenses computed from the visible rows and rendered distinctly from the server's
authoritative YTD rollup — the spec's one real tension. Then **tasks** (kind chips + priority select, default
priority-then-target sort; the board renders `view.rows` grouped into its status columns, the bundle stats stay
on the full set like the expenses rollup) and **equipment** (status chips + category select, no sort — the list
stays grouped by category, `view.rows` regrouped so a filtered-away heading doesn't render). Both configure the
same shared hook + strip with only declared predicates — no per-screen filter code. No contract change; entirely
client-side.

**Starter-check selection on add-car (2026-07-19).** `docs/specs/2026-07-19-starter-check-selection/`. When the
add-vehicle sheet's "Regular checks" is set to the generic starter set, its fifteen checks now expand inline as
an all-on toggle list with a live "N of 15" count and each cadence shown read-only, so the founding set can be
pruned to the car (no air-con, electric-assist steering) before create rather than after. The template is not
hardcoded in the client: `GET /api/reference/starter-checks` projects the same `CheckTemplate.Generic` the
factory applies, so the picker can't drift from what create does. `CreateVehicleRequest` gained
`SelectedCheckNames`; `CheckTemplate.For(0, names)` filters the generic set by an ordinal `Contains` (template
order preserved, `DisplayOrder` renumbered contiguously) and `VehicleFactory.CreateAsync` threads it through.
The client tracks *deselections*, so leaving the list alone omits the field entirely → the server applies all
fifteen byte-for-byte as before; deselect-all sends `[]` → no checks, exactly like "None". Additive contract
diff; no schema change (it chooses which `CheckDefinition`s to create, not their shape). Watch the positional
`CreateAsync` call: the new param sits before `cancellationToken`, so pass the token by name.

**Add a set of checks — copy-from-vehicle surfaced, and bulk-add in settings (2026-07-19).** The unified
follow-on to starter-check selection (plan `~/.claude/plans/snazzy-kindling-axolotl.md`). Three converging gaps
closed: (1) `CheckSource.CopyFromVehicle` — fully built in the domain but reachable from no UI — now appears in
the add-vehicle sheet's "Regular checks" (only when the garage is non-empty), with a source-vehicle picker and
the *same* toggle list over that car's **active** checks; (2) Settings → Check definitions gained an **"Add
checks…"** sheet that adds the generic set *or* a copy of another car's checks onto an **existing** vehicle;
(3) the `.checksel` block became a reusable `<CheckSelectList>` (`components/`), which now takes a `locked` set —
checks the vehicle already has render disabled as "already added", out of the count. Domain: `ResolveChecksAsync`
extracted into a shared **`CheckSetResolver`** (create-time + post-hoc use one resolver, so "generic"/"copy from
X" can't fork), and **copy now honours `selectedNames`** the same way the generic path does (null still copies
all active — create-time callers unchanged). New **`CheckSetAdder`** adds a resolved set to an existing vehicle,
skipping names it already has (**active *and* retired** — the unique `(VehicleId, Name)` index ignores IsActive)
and **appending** `DisplayOrder = max+1` (not the generic path's 1-based renumber). API: `POST
/api/vehicles/{reg}/checks/definitions/add-set` → `{ added, skipped }`; `useVehicleChecks` previews a source
car's definitions. Additive contract; no schema change. Note: `useGarage()` can be a non-array under a loose
test mock — both new sheets guard with `Array.isArray`.

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

Left to do: green-lane-trips, and the Phase 5 hardening (backup, export, HTTPS). Phase 4's MCP server **shipped**
(2026-07-20, above); head-gasket-watch, **documents** — the seventeenth and last screen — and dvla-lookup all
shipped 2026-08-07. **All 17 screens now exist.** The DVLA lookup is built but dormant until API keys are
provisioned.

**Running costs: the purchase price reached no figure (2026-08-07).** The arithmetic was right; the largest
cost never got to it. `Vehicle.PurchasePrice` was stored, shown on Vehicle Info and read by **zero**
calculations — `SpendCalculator` takes the purchase cost from expense rows in the `Purchase` category and
**nothing ever wrote one**. So on every vehicle the app creates, `TotalSincePurchase` equalled
`TotalSincePurchaseExcludingPurchase` and the two cost-per-mile figures were the same number: four fields
silently collapsed to two, and the dashboard's "including the £1,700 car itself" clause — conditional on those
totals differing — simply never rendered. Nothing looked broken. Fixed as the **fourth expense mirror**
(`VehiclePurchaseMirror`, called by `VehicleFactory.CreateAsync` and `VehicleUpdateService` — one path, so
create and edit cannot fork); `SpendCalculator` is **unchanged**, its existing Purchase logic just starts
firing. The marker is `ExpenseEntry.IsVehiclePurchase` with a **partial unique index** on `(VehicleId) WHERE
is_vehicle_purchase` — a flag not the category name, because categories can be renamed and that would orphan
the mirror (so `Purchase` is now rename-locked beside `Fuel`, and `ReferenceOpStatus.FuelRenameLocked` became
`MirrorRenameLocked`). Hand-typing a Purchase expense is refused like Fuel. `PurchasePrice` also became
**patchable** (`UpdateVehicleRequest`, `update_vehicle_profile`) — create-only was fine while it was cosmetic
and is not now that a typo moves every cost figure. Three labels were describing other numbers: **"Monthly
average · ex-purchase" was false** on both surfaces (the figure included the car, and no ex-purchase twin
existed — added `MonthlyAverageExcludingPurchase`); the **garage card's "Running cost" rendered the
purchase-inclusive `costPerMile`** while the dashboard's tile was already ex-purchase, so two screens gave two
answers under the same words; and **"since purchase" meant two different numbers inside one panel**. Settled
vocabulary, applied everywhere: **"running cost" always excludes the car, "total outlay" always includes it.**
Also closed: **wash costs never mirrored** (`WashEntry.Cost` rendered on the wash screen and counted nowhere,
while the Budget page promised "money the app knows about is never hidden"), **equipment with a cost and no
purchase date** is now refused rather than accepted-and-dropped (plus a fifth `AnomalyKind.
EquipmentCostWithoutDate` for rows that predate the rule — BT53's £24.99 scissor jack is one), and
cost-per-mile now **says when the odometer is stale** (its numerator runs to today, its denominator only to the
last reading). Migrations `AddVehiclePurchaseMirror` (two columns, two indexes, three backfills — it **adopts**
an existing hand-typed Purchase row rather than inserting a second, because doubling the largest line is the
£163.16 failure in another currency) and `AddEquipmentCostAnomalyKind`. Additive contract diff throughout.
**211 Domain, 134 Data, 431 front-end.** Plan at `~/.claude/plans/soft-baking-thompson.md`.

**Head-gasket watch — checks as an issue's early-warning (2026-08-07).**
`docs/specs/2026-07-16-head-gasket-watch/`. The design says "Head-gasket watch · lapsed" on the dashboard,
"resolved **conditionally** — the two weekly checks are what keep it that way" on issues, and an `HG watch`
badge on the checks screen. The app could say none of it: a comment in `VehicleCard.tsx` has read *"nothing
models WHICH checks are the head-gasket watch"* since the garage screen was ported. Now an issue names them —
a join table `issue_watch_checks` (composite key, both FKs cascade, migration `AddIssueWatchChecks`), no column
on either side, because an issue watches a *set* and a check may guard more than one issue. **The same-vehicle
invariant is a write-path guard, not a DB constraint** (it reaches across two tables; Postgres needs a trigger
for that), and it refuses the whole call rather than filtering — a caller passing a wrong id is told.
**Nothing about the watch is stored** — not its status, not a lapsed flag: `WatchCalculator` reads the
`CheckState`s `CheckStatusCalculator` already produced and groups them by issue, so it adds **no arithmetic**
and the dashboard's named watch cannot disagree with the checks screen. `DerivedMetrics.Compute` now builds the
check summary **once** and passes that same instance to both `Checks` and `Watches`. What counts as lapsed is
one definition (`WatchCalculator.IsLapsed`): Overdue, **NeverLogged** (a never-done early-warning check is not
reassurance — the workbook's 17-of-18 bug in another costume) and **Attention** (the verdict alarm actually
going off), but not DueSoon; it is carried to the client as a per-check `IsLapsed` so the rule is not
re-evaluated per surface. `IssueItem` gains `Watch`, `VehicleSummary` gains `Watches`, and the attention panel
ranks a named lapsed watch **above** the generic "N checks overdue" (different claims — one is a chore list,
the other is why it matters) and **below** an expired renewal. **The status is never touched**: a lapsed watch
on a Resolved issue still says Resolved and says the thing keeping it resolved has stopped — flag, never act
for the owner, the same rule the anomaly lifecycle follows. Additive contract. **221 Domain, 140 Data, 441
front-end.**

Two things fixed in passing: **`IssueService.AddAsync` never stamped `ResolvedDate`**, so posting an issue
already Resolved — exactly how the head-gasket item arrives — died on
`ck_issues_resolved_date_iff_resolved` with a bare `DbUpdateException` (the PATCH path always stamped it; the
add path never did, because nothing had yet posted one). And `AddIssueRequest` accepts the watch too, so
linking checks is not an operation you can only perform on an issue that already exists.

**Documents — the seventeenth screen (2026-08-07).** `docs/specs/2026-07-16-documents/`. The last workbook
screen, and the only one that needed file upload, which is why it went last. **No schema change** — `Document`
and `DocumentConfiguration` were built in Phase 1 and the spec verified that before it was written. Bytes live
on a mounted volume with the path on the row (DEC-005); `Documents:RootPath` is resolved to an absolute path in
`Program.cs` so the domain takes no hosting dependency for one string. **Storage is content-addressed**: the
file is named for the SHA-256 of its own bytes under `{root}/{vehicleId}/`, hashed *while* streaming through a
`CryptoStream` in one pass — so two `scan.pdf`s cannot collide, a client filename never becomes a path
component, and a byte-identical re-upload is refused **by name** ("already filed as 'MOT certificate — pass'").
The 25 MB cap is enforced while reading, not from a Content-Length header. `DocumentEndpoints` is the only
group taking `multipart/form-data` and the **only write path that never calls `AnomalyScanner`** — a document
moves no figure and trips no detector, which is correct rather than an omission. Links are `SetNull`, never
cascade: delete the service record and the certificate survives with its link severed, the opposite of the
expense mirrors, because a mirror is a shadow and a document is evidence that outlives its subject. Delete
removes the row then the bytes, **skipping the file if another row shares it** — the cost of content-addressing.
Screen: Papers on `<DataTable>` (fifth consumer), photo sets as a **grid**, chips from `DocumentType` + the
link only (no tags table was invented to match the mock's `identity`/`statutory` chips; the `→ policy` chip
stays unbuilt because there is no `PolicyId`). **221 Domain, 155 Data, 449 front-end.** Additive contract.

> **The one thing the port could not have been written without discovering:** a bearer-authenticated app cannot
> serve bytes through `<img src>` or `<a href>`. A plain navigation carries cookies, not our `Authorization`
> header, so an image pointed at the file endpoint gets a 401 and a broken-image icon. `apiBlob()` sits beside
> `apiRequest()` in `api/client.ts` — the bytes come through the same authenticated fetch seam and become an
> object URL, revoked on unmount so the photo grid does not pin every image it has ever shown.

**DVLA/MOT lookup — a plate instead of a form (2026-08-07).** `docs/specs/2026-07-16-dvla-lookup/`, **DEC-015**.
`GET /api/vehicles/lookup/{reg}` calls DVLA VES (identity, engine, tax) and DVSA MOT History (current expiry)
**server-side** and pre-fills the add-car sheet. Server-side is not a preference: the DVLA key must not reach a
browser, and the strict CSP forbids a browser→`api.gov.uk` fetch outright, so a client-side lookup could not
work even if the key were publishable. **The load-bearing decision is where the MOT date lands** — on
`Vehicle.MotExpirySeed`, *not* a fabricated MOT `ServiceRecord`. A ServiceRecord asserts a test *happened*
(garage, cost, mileage, date of work — none of which the DVLA gives us); materialising one would put a record
nobody performed into service history and make the seed indistinguishable from a real pass, which is the
opposite of "a real record supersedes the seed". `MotExpirySeed` is already documented as "read only while no
MOT record exists", so the first logged pass wins **by construction**. VES tax date → `VedExpiry`, which *is* a
legitimately stored input because nothing logs a road-tax payment. `CreateVehicleRequest` gained
`EngineSizeCc`/`MotExpirySeed`/`VedExpiry`; there is still deliberately **no settable MOT expiry**.

> **It is built and dormant.** Both upstreams need credentials nobody has provisioned — VES an API key, DVSA a
> key plus OAuth client credentials — so with none set the endpoint answers **503 NotConfigured** (distinct from
> 502, which would invite a retry that cannot succeed) and the sheet says so while manual entry stays exactly as
> usable. That is CI's state and every fresh checkout's. Switch it on under `Lookup:` — `VesApiKey`, then
> `MotApiKey`/`MotTokenUrl`/`MotClientId`/`MotClientSecret` — **where those come from and where they go
> (user-secrets in dev, `Lookup__*` via `deploy/.env` in containers) is the README Quickstart**, which is now
> the one place that answers it. **The mapping is written against the documented response shapes, not real
> traffic**, so first live use may find field-name drift; the DVSA token flow has never round-tripped.

The pure vocabulary (`LookupMapping`, `VehicleLookupOptions`, `IVehicleLookupService`) sits in
`CarTracker.Domain/Lookup/` where it is testable; the HTTP lives in `CarTracker.WebApi/Lookup/`. An unknown fuel
type maps to **null, never a guess** — a guess would be invisible and would wrong every MPG figure derived from
that car. Also fixed: **the add-car fuel select offered "Plug-in hybrid", which is not a `FuelType`** (the enum
is Petrol/Diesel/Hybrid/Electric/**LPG**), so choosing it sent a value the server rejects — a hand-written
option list that had drifted from the contract it feeds. **244 Domain, 155 Data, 453 front-end.**

**Free-text search on the log tables (2026-08-09).** `docs/specs/2026-08-08-log-table-search/`. The deferred
third of `2026-07-16-log-table-filters`, which was titled "Filter, Sort & **Search**" and shipped two. Search
lives **inside `useTableView`**, not beside it, and that is the whole design: selection state is
`Record<string, string[]>` of *option ids* filtered by `sel.includes(o.id) && o.test(row)`, so unbounded text
has nothing to select — but the deciding reason is that `count`, `total` and `filtered` all derive from that
state, and a search narrowing rows anywhere else would leave `TableControls` announcing an "N of M" the table
no longer matches. Config gains `search?: { label, fields }`, the view gains `searchText`/`setSearchText`, and
`filtered` widens to `anySelected || query !== ''`. Omit `search` and nothing changes — the query is forced to
`''` when it is undeclared, so a screen that never opted in cannot be filtered by stale state. Matching reuses
`Combobox`'s idiom (`trim().toLowerCase()` + `.includes`), one substring per field, **not** term-splitting.
A query matches **every text field the row carries, including ones no column renders** — service `notes` holds
the MOT advisories, and finding "headlamp lens" two years later is the point; the accepted cost is a row that
matches for a reason not on screen. Six screens wired. **Service history gained the filter strip it never
had**: its `serviceDate` sort carries an **id tie-break** because the hardcoded `[...records].reverse()` it
replaces put the later-inserted of two same-day records first, and it gained the filter-miss empty panel it
lacked (it had only "no records yet", which would tell someone with four years of history they had none).
No debounce, deliberately: nothing paginates, every log is one un-paged GET, and filtering tens of rows in a
`useMemo` per keystroke is free — `useDeferredValue` is the answer if that ever changes. Entirely client-side:
**no schema, no endpoint, no contract diff**. `.tctl-search` carries `min-width: 0` for the reason `8b938af`
records. **486 front-end.**

**Public landing page (2026-08-09).** `docs/specs/2026-08-09-public-landing-page/`. The app is going public,
and a signed-out visitor used to get a centred `<h1>`, one sentence and two buttons — an invitation to create
an account in a product it never described. `LandingPage` replaces that branch of `AuthGate`: a `--head-bg`
hero on the `.g-hero` recipe, the spreadsheet story, two real screenshots, and both CTAs repeated at the foot,
over a `<Footer>` linking to usualexpat.com and the GitHub repo.

> **Its first cut (0.10.0) was written for the wrong reader, and the fix is worth knowing about.** Assembling
> the copy from the README and mission seemed obviously right — the prose is good — but it is written for
> engineers, so the page shipped saying "MCP", "self-hosted", "derived" and "a class of bug the schema
> forecloses". Rewritten in 0.11.0 for car owners, keeping the spreadsheet story because it is concrete
> ("it said the MOT was due in three weeks; it had already been done") and dropping the arithmetic.
> `LandingPage.test.tsx` carries a **jargon guard** — the rendered text must match none of `MCP`,
> `self-hosted`, `derived`, `schema`, `domain service`, `regression test` — because the house voice creeps
> back otherwise. It went red on all five terms present in the first cut, which is how it earned its place.
> The page also now says plainly that connecting an assistant takes a key and a config file: unqualified,
> "ask an AI assistant about your car" promises a non-technical owner something they cannot reach until the
> in-app chat ships. And `footer a` had no rule at all, so the first link in a footer would have rendered in
> browser-default blue on the dark band. **It renders above the router**, because `AuthGate` wraps `RouterProvider` and that is
the property stopping any screen flashing another user's data before a redirect settles — so **the page has no
URL**, and a future `/about` means moving the gate inside the router, which is a change to the security
boundary. `LandingPage` is presentational (two callbacks + an optional error), so `AuthGate` stays the only
file that knows what `screen_hint: 'signup'` is for, and the page tests need no session mock. Reverses
`design-brief.md:347`, which forbade exactly this and predates Auth0; the stale "Single-user, self-hosted"
line on the garage **footer** went with it.

> **But "self-hosted" did not leave the garage screen, and this sentence used to imply it had.** The *hero
> eyebrow* still reads `Car Tracker · self-hosted` (`GaragePage.tsx:41`) — a different string from the footer
> line that was removed. It is the one place in the app still describing the product the way the landing page's
> jargon guard forbids, on a deployment now open to invited strangers. Left as-is because it is a product-copy
> decision rather than a defect; recorded because the Azure spec caught this file overstating the cleanup, and
> a stale certainty in this document is exactly the failure the "four bugs, one cause" section warns about.

> **Three things this cost that are worth knowing.** (1) `docs/images/` is **not served** — `.dockerignore`
> excludes `docs`, and an unresolved `/images/x.png` hits `MapFallbackToFile` and returns **`index.html` with
> a 200**, a broken image reporting success. The shots live in `src/assets/screens/` as WebP and are
> *imported*, so Vite fingerprints them (`fonts.css:8-12`'s rule, after a stale-cache incident). 51.3 + 29.5
> KB, the app's first bundled rasters. (2) The garage screenshot needed a second crop: its footer legibly read
> "Single-user, self-hosted", which would have contradicted the page it sits on. (3) **`.btn` is `--fg` on
> `--bg`**, which on the dark hero band is dark-green on dark-green in *light* theme — so `.lp-hero .btn` pins
> to `--head-fg`/`--head-bg`. `axe` cannot catch this: `color-contrast` is disabled in `test/axe.ts` because
> jsdom has no layout engine. `.lp-hero` is registered in `tokens.test.ts`'s ALLOWED band list, which is what
> makes painting `--head-*` there legitimate rather than a guard failure.

**The landing page does not make the app ready for public sign-up.** `docs/product/roadmap.md` now carries
three gates: `Garage`/`WashLocation` still have **no `OwnerId`**, so one account can rename another's
reference data; HTTPS is unmet while the MCP endpoint carries a bearer token; and DEC-016's
first-user-claims-all-unowned-vehicles is a trap on a deployment where a stranger signs in first.
**Two of those three closed 2026-08-14 — see the entry below. HTTPS did not, so sign-up stays shut.**

**A flag that leads you to the row that caused it (2026-08-13).** The integrity queue could say precisely what
was wrong and offered no way to act on it: the only action on an open flag was **RESOLVE**, which changes the
flag's *status* and never touches the data. Open flags now lead with **Fix this →**, an `AppLink` to the screen
that owns the offending row carrying **`?flag=<anomalyId>`** — the app's **first search param**. The receiving
half is `lib/useFlagFix.ts`: it resolves the id against the cached `useAnomalies` list, **compares the flag's
`entityType` to the caller's and returns null on a mismatch** (a stale link opens nothing rather than the wrong
row), strips the param with `replace: true` while keeping the flag in state for the visit (so Back and refresh
cannot reopen a sheet you closed), and `useOpenFixedRow` opens the row's own existing edit sheet once, ref-
guarded. A `<FixBanner>` carries the **detector's own sentence** — never a re-worded one — with a link back to
the queue. **The closing half was already built**: `AnomalyScanner.Reconcile` retracts an Open flag inside the
same write, so there is no "done" button and none would be honest. `hrefFor(screen, reg, query?)` gained the
query slot; `<DataTable>` gained `scrollTo`. Only mileage, fuel and equipment are wired, because the four
detectors name only three entity types.

> **Two things this turned up.** (1) **Nothing but service history invalidated `['vehicle', reg, 'anomalies']`**
> — so supplying the missing purchase date would have left the flag sitting on the queue it was meant to close.
> Fuel, mileage and equipment now invalidate it (plus summary and garage) with their other keys. (2) The
> **mirrored-reading case has no direct fix and must not pretend otherwise.** `MileagePage` allows editing only
> `origin === 'Manual'`, and BT53's 83,000 mi flag is `Service`-origin; `MileageReading` carries no link back to
> the record that wrote it, and matching by date+mileage would be a guess. So the row is highlighted, no sheet
> opens, and a `CORRECTED_AT: Record<Origin, …>` map names the screen where the fix lives. One honest hop.

Three defects on that screen shipped with it. **The fourth detector had no copy**: `EquipmentCostWithoutDate`
landed 2026-08-07 and was never added to `KIND`, so its rows rendered the raw message as their title, printed
it again in the comparison block, and left the explanation an empty `<p>` — the map's own comment claimed
`Record<Kind, …>` "so a fourth detector fails the build here" while the declaration read `Record<string, …>`,
which is exactly why nobody noticed. It now reads the generated enum, and so does the new `FIX` screen map.
**"Three detectors" is four**, in the header and the empty state. And **"worst first" was false**:
`LogQueryService` ordered by `Severity`, a *string* column, so descending sorted it `Warning` → `Info` →
`Error` and put Errors last; it now ranks by the enum's meaning through an explicit CASE, correcting the web
queue and MCP's `get_data_integrity` together. **No schema, no endpoint, no contract diff** — the ordering fix
changes the sequence, not the payload. **244 Domain, 156 Data, 519 front-end.**

**Kit you have not bought is not spend (2026-08-13).** Dogfooding the above found the equipment rules wrong in
both directions at once, because **nothing in the domain read `EquipmentStatus`**. `CostNeedsDate` took only
`(cost, date)`, so **"Tow rope, £40, to order" was refused outright** — the one status whose whole purpose is
pricing something before you buy it was the one you could not price. And `MirrorFor` was equally status-blind,
so a cost *plus* a date wrote a real `Tools/Equipment` `ExpenseEntry` whatever the status — while the add sheet
pre-filled **today's date on every new item**, which is how a £40 estimate quietly reached spend,
cost-per-mile and the Equipment & Tools budget. The front end had the rule right the whole time and nothing
else agreed with it: `EquipmentPage`'s Kit-value tile has always read `items.filter(i => i.status === 'Owned')`,
noted on screen as "owned items with a cost".

One predicate now draws the line — `EquipmentRules.CostIsSpend(status)`, `status != ToOrder` — read by all four
places that were guessing: the write refusal, the mirror, the mirror's reconcile on edit, and
`DetectEquipmentCostWithoutDate`. **Owned and On order count** (on order is paid for and on its way); **To
order is a plan**: no date wanted, no mirror, no flag. Written as "not ToOrder" rather than as a list of the
two that count, so a fifth status defaults to *counting* — absent money is invisible, present money is
arguable. The patch guard fires on a **status change** as well as a cost or date, because moving a costed item
out of To order is the moment the estimate becomes money and so the moment to ask when; and `shouldMirror`
shares the predicate, so moving one back takes its expense off the budget with it. `EquipmentStatus` had **no
XML docs at all** — its meaning lived only in a comment on the equipment screen, which was tolerable until the
domain started branching on it. Migration `DropMirrorsForUnboughtEquipment` is **data-only**: it deletes the
mirrored expenses already written against To-order rows, which nothing else would ever revisit. Deleting them
is not data loss — a mirror is a shadow, and the item, its estimate and its status all stay.

**Two layout defects shipped with the Fix-this banner, and one predates it.** The `ABOVE CURRENT` pill is
~115px (10px mono, 0.12em tracking, `white-space: nowrap`) in a fixed **90px** track, and a grid cell paints
over its neighbour rather than clipping — so it sat on top of the Source column. `ExpensesPage.tsx:249` had
already fixed this exact bug once by measuring the widest pill its column can render; the odometer track is now
`124px` and `.dt-c:has(.pill)` lets a pill fall to its own line, which also fixes the fuel log's
`IMPLAUSIBLE`/`BEST`/`WORST` in a 122px track (**the same bug, unreported** — `.mpgcell` needed `flex-wrap` too,
being a flex item one level in). And **`FixBanner` invented a fourth callout**: `.fixban*` plus `.fixnote` put
two blue boxes of different widths on one screen, the second with its action inline in a paragraph, saying
*"correct the row below"* directly above *"the row below is read-only"*. `.attn.attn-info` is the house shape
for precisely this — `1fr auto`, prose left and actions right — and `MileagePage` was **already rendering one
thirty lines above**. The banner is now that, with optional `note`/`action` props so the mirrored case rides
one box instead of two, and it sits below the section head so it attaches to the table it is about.
`ANOMALY_KIND`/`FIX_SCREEN` moved to `lib/anomalyCopy.ts` so the queue and the banner cannot describe one flag
two ways. Also caught: `IntegrityPanel` still enumerated **three** detectors. **249 Domain, 158 Data, 521
front-end.**

**Money that has left the account counts, whatever its date says (2026-08-13).** Every money figure on BT53 was
understated by exactly £1,183.00 and the app said nothing. `SpendCalculator` and `BudgetCalculator` filtered
expenses with `EntryDate <= referenceDate` (= `Clock.Today()`), so a tyre bill **paid in advance** and dated
four days out was absent from `TotalYtd`, `TotalSincePurchase`, `ServiceAndRepairsYtd`, `MonthlyAverage`,
`CostPerMile` and every budget group — while the expenses table and the cumulative chart both showed it. The
exclusion was *deliberate and pinned*: `SpendCalculatorTests.Future_dated_expenses_are_excluded`, "the
reference date itself counts; tomorrow does not". What made reversing it right was not the rule but the three
things around it. **The numerator was clamped and the denominator was not** — `MileageCalculator.Calculate`
does not even *accept* a reference date, so the 82,900 mi reading written by that same service counted and its
money did not; cost per mile read £0.58 where the honest figure is £0.77. **Nothing said a row had been
dropped** — no flag, no field, under a rollup panel whose own rule text reads "computed from the rows".
And **the invariant that should have caught it had no test**: `ExpensesPage.test.tsx` asserted the chart equals
the rows it was handed, a tautology, on a fixture setting `totalSincePurchase: 3192.86` against a chart of
£688.60.

Now: `ytd` is a **calendar-year match** and `sincePurchase` has **no upper bound**; `PeriodBounds` ends are
boundaries rather than clock readings (CalendarYear → 31 Dec, SincePurchase → open, **Rolling12Months stays at
today** — "the last 12 months" is backward-looking by definition, and it is the one view where a future row
legitimately does not appear). A fifth detector, **`AnomalyKind.FutureDatedEntry`**, questions the date instead
of the app obeying it in silence — because counting these means a mistyped year now *inflates* a total rather
than shrinking one. It **expires by itself** when the day arrives, through the existing `Reconcile`. Two
structural notes: `AnomalyDetector` is static and had no clock, so `today` threads through
`Detect`/`Reconcile`/`FindAll` and `AnomalyScanner` gained a `Clock` beside its `TimeProvider` (a *day* in
Europe/London is a different question from an audit *instant* in UTC); and it flags **the row that owns the
date, not the mirror** — a future service stamps three rows and only the `ServiceRecord` is editable, so the
walk is over expenses and resolves each through the mirror FK it already carries.

**`FIX_SCREEN` is now keyed on the entity type, not the kind** (`lib/anomalyCopy.ts`). One finding can land on
a service record, a fill, an item, a wash or a hand-typed expense, so a kind→screen map could not route it —
and the fix screen was always a property of the row. All four earlier kinds mapped identically, so nothing was
lost. `ServiceHistoryPage` and `ExpensesPage` gained the `useFlagFix` wiring (expenses passes the same
`!isMirrored` rule its table uses). **Known gap:** `WashPage` gets the link but no auto-open.

Three adjacent honesty gaps went with it. **The staleness note was one-sided** — `daysBetween` is signed, so an
odometer dated *ahead* gave −4, `−4 > 14` was false, and `SpendPanel` stayed silent exactly when the
denominator was least trustworthy. **The budget hid the £1,700 car**: excluding a purchase from running costs
is right, but it appeared *nowhere*, under a footer promising "money the app knows about is never hidden" —
`BudgetSummary.ExcludedPurchase` now states it. And the chart test compares against the server rollup on a
fixture that is a possible world. **First contract diff in three rounds** (additive: the fifth `AnomalyKind`,
`excludedPurchase`), migration `AddFutureDatedAnomalyKind`. **257 Domain, 159 Data, 524 front-end.**

**Two accounts, and the second one could rewrite the first's records (2026-08-14).**
`docs/specs/2026-08-11-pre-public-release-gates/`, **DEC-018**. The roadmap called this "one user can rename or
re-home another's data", which reads as untidiness. It is a **cross-tenant write**, and it is armed by the
second account rather than the hundredth. `Garage`, `WashLocation` and `ExpenseCategory` were keyed by `Name`
alone, so the second owner to type "K & P Motors" silently *adopted* the first one's row — address and contact
included — and `ReferenceListEditor` matches on that name, not through a vehicle, so the one filter Phase 4.5
relies on never came into it. Owner A renaming their garage issued an `UPDATE` across owner B's service records
and workshop tasks.

> **The red test found something worse than the bug it was written for, and it decided the shape of the fix.**
> B's three references did not fail the same way: two came back rewritten into a name B never chose, and the
> third — `vehicles.default_garage` — came back **NULL**. `context.Vehicles` *is* filtered, so B's vehicle was
> correctly left out of the repointing, and then the old `garages` row was dropped and the `SetNull` foreign
> key blanked the field anyway. **Partial scoping was worse than none**: scoping the editor's statements
> without changing the key would have produced that third line on all four garage/wash columns. So the
> composite key and the FK drops are a *prerequisite* of scoping the cascade, not a tidy-up after it.

The three tables are now keyed **`(OwnerId, Name)`**, cascading from `users`, with three query filters beside
the `Vehicle` one — one mechanism extended, not a second style introduced — and **all six foreign keys
dropped**. The columns do not change: they stay `varchar` carrying names, which is the entire reason for
choosing this shape over the surrogate id the roadmap had recorded. `ServiceRecord.Garage` and
`WashEntry.Location` render straight into `<DataTable>` columns and sit in `useTableView`'s `search.fields`;
`add_service`, `log_wash` and `update_vehicle_profile` take a garage **by name**. An id would have changed
every one of them for a guarantee the application layer already overrides — the `SetNull` is the outcome the
editor exists to prevent, the `Restrict` duplicates a check it already performs (and obstructs it: a correctly
scoped `UpdateCategoryAsync` ends in an `ExecuteDelete` that throws while the constraint lives), and the
`Cascade` on budget memberships silently does what the editor re-homes explicitly. **The gate never named
`ExpenseCategory`**, which had the identical defect twice over, and `GET /api/reference/expense-categories`
was reporting every account's usage as your own.

**Migration `AddPerOwnerReferenceLists` is hand-written and one-way.** EF's generated `Up()` was thrown away
(its `DeleteData` is keyed on the old PK and eats the per-user copies) for ordered SQL: drop the 6 FKs → drop
the 3 single-column PKs → add `owner_id` **nullable** → copy per user → `DELETE WHERE owner_id IS NULL` →
`SET NOT NULL` → add the 3 composite PKs. `Down()` throws. It **asserts `users` count ≤ 1 and aborts
otherwise** — a per-user copy of a shared row is only unambiguous while there is one user, and the spec's
original instruction was to "verify against a restored dump", which is not something a migration can do. The
precondition is enforced instead of trusted, and `PerOwnerReferenceListBackfillTests` proves both halves
against a real database by migrating to the *previous* migration, seeding through the old schema, and
migrating up: one account keeps every row and every child name; two accounts abort with the garage untouched
and `__EFMigrationsHistory` unmoved.

The **13 expense categories stopped being seed data** — a seeded row has no owner and there is none to invent
— so `ExpenseCategoryConfiguration.HasData` goes and `AccountProvisioner` creates them per account.
`SystemCategories` is a static array of **live entity instances**, so `AddRange(SystemCategories)` would attach
process-wide singletons to a `DbContext`; `SystemCategoriesFor(ownerId)` projects fresh ones. Provisioning is
**two saves**, because `user.Id` is store-generated and the owner FK is navigation-less, and the lost-the-race
catch now does `ChangeTracker.Clear()` rather than detaching the user alone and stranding 13 Added rows.

> **Where the guard sits, and why not everywhere.** `ReferenceOwner.Require` refuses an insert with no account
> in two distinct sentences — *no request context* (background, design-time, a directly constructed test
> context) means the caller is wrong; *a request that resolved no account* means the pipeline is wrong. It
> guards the four **create** inserts only: reads and edits still run under a bypass context, because refusing
> there would make every existing Data test unrunnable to prevent a hazard those tests do not exhibit. The
> real bypass hazard — `Garages.Where(g => g.Name == name).ExecuteDeleteAsync` deleting **every** account's row,
> since `BypassOwnership` is a runtime parameter and the filter then contributes nothing — is closed by naming
> the **whole primary key** on all six reference-table deletes. The three *rename* inserts take the owner from
> the row being renamed: a rename changes one key component, not both. Fifteen child statements are scoped with
> `context.Vehicles.Any(v => v.Id == x.VehicleId)`, which inherits the vehicle filter inside the generated SQL —
> the correlated subquery held and no materialised `Contains` fallback was needed. Fifteen, not the eleven
> planned: five *counts* needed it too.

> **A `BypassOwnership` context makes an isolation test a false green** — every correlated `Any()` matches. The
> tests build their contexts with an accessor pinned by `TestOwner.As(ownerId)`, and their vehicles through
> `VehicleFactory.CreateAsync(vehicle, ownerId, …)`, and the warning is written into `As`'s doc comment.

**DEC-016's first-user-claims-all-unowned-vehicles is retired, not guarded.** Adoption is now an explicit
`Ownership:ClaimUnownedVehiclesFor` subject matched ordinally, **defaulting to nobody**. Beside it, sign-up is
invitation-only (`Signup:AllowedEmails` / `Signup:AllowedDomains`) and **an empty allowlist means closed** —
the fail-safe direction and the opposite of the natural reading, so it is stated in `.env.example`, the README
and the API spec. A refused person leaves no `User` row and nothing to clean up, **but does leave an Auth0
identity in the tenant**; disabling public sign-up in the dashboard is the belt to those braces and nothing
here can assert it has been done. The policy lives in `AccountProvisioner` (domain) rather than
`CurrentUserMiddleware`, because there is **no `CarTracker.WebApi.Tests` project** and "a refused address
creates no row" is worth asserting against a real database.

> **The list is over *verified* addresses, and that half is not decoration.** `SignupPolicy.Admits` takes the
> tenant's `email_verified` beside the address and refuses without it: on a database connection a stranger
> self-registers with whatever they type, so a domain allowlist alone admits anyone writing
> `anything@example.com` while the deployment reads as invitation-only. It arrives in the same Management API
> answer, so it costs no extra call — and a connection that never verifies admits nobody. Beside it,
> `SignupRefusalCache` remembers a refusal for a minute, because a refusal writes no row and so an uninvited
> visitor would otherwise re-ask the rate-limited tenant on every request; a throttled tenant answers nothing,
> which refuses the *invited* newcomer signing in during it. The cache holds refusals only, never admissions.

> **The allowlist needs an address and the access token carries none.** `CurrentUserMiddleware` has documented
> that since July and fell back to `?? sub`, so every `User.Email` held an `auth0|…` string — unmatchable by an
> allowlist, and untypeable as a deletion confirmation. The server now calls the Auth0 **Management API**
> (`GET /api/v2/users/{sub}`) at provisioning and **backfills** the address on rows where `Email == ExternalId`,
> an equality no real address can satisfy. That is **one credential gating two things**: with `Auth0:Management:`
> unset, sign-up is closed *and* account deletion refuses.

**Art. 15/17/20 got endpoints.** `GET /api/account/export` streams every stored row the account owns — 15
per-vehicle tables, the three reference lists, tokens without their secrets, the write-audit trail — through a
`Utf8JsonWriter`, flushing between vehicles. It carries **no derived figure by rule**: an archive exists to be
read when nothing can recompute, and a stored derived value in one is the workbook's five defects in a new
costume. That cost two reads the log layer did not have (`ListIssuesAsync`, `DocumentService.ListRowsAsync` —
the screen wrappers carry live check status and rendered link labels) and **reverses `FuelEndpoints.cs:43-44`
for this one caller**, where no raw fuel read had ever been allowed to exist. The endpoint declares **no
response schema**, because a streamed payload has no static shape and a declared one would be a second
definition free to drift. `DELETE /api/account` takes your own email as a body, deletes data first inside one
transaction (vehicles by `RemoveRange`, not `ExecuteDelete` — `Vehicle` shares its table with four owned
blocks), then the document folders, then the identity; a failed identity call queues
`pending_identity_deletions` for an hourly retry. With the credential unset it **503s and deletes nothing**,
checked before the transaction opens — the `Lookup:` precedent, and a half-erasure that leaves a login is
worse than a refusal naming the missing grant. An assistant token gets **401** at the door rather than 403,
which is accepted: widening the scheme purely so it could be told no is a bad trade, and `api-spec.md` says so.

Client half: a *Your account* section in Settings, with export as a `blob:` object URL saved under the
**server's** `Content-Disposition` filename (a `blob:` href ignores the header, so `apiDownload` returns it —
deriving the name client-side would disagree by a day for anyone downloading late in the evening west of UTC),
and a deletion sheet that states the counts in prose and arms only on an exact address match. `AuthGate` now
makes the app's first API call above the router, and **the invitation refusal is the only place this app reads
an RFC 9457 `type`** — a not-invited 403 is otherwise indistinguishable from any other. It **fails open**: a
500 or a dropped connection renders the app, because a gate that locks people out whenever it cannot reach the
server turns an outage into a lockout. Also found: **`.btn` had no `:disabled` rule at all**, nothing having
ever disabled one, so an inert destructive button would have painted exactly like a live one.

Additive contract diff throughout (three paths, `AccountSummary`, `DeleteAccountRequest`, and a
`meta.identityDeletionConfigured` defaulting to false). Migrations `AddPerOwnerReferenceLists` and
`AddPendingIdentityDeletions`. **272 Domain, 204 Data, 537 front-end.**

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
- **`Garage`, `WashLocation` are keyed reference tables.** `ServiceRecord.Garage`, `MaintenanceTask.AssignedGarage`,
  `Vehicle.DefaultGarage` and `WashEntry.Location` all look like free text and are not: a row must exist in the
  list first. Their comments said "upserted by the importer" — and DEC-008 deleted the importer, so nothing
  upserted them: a 500 the first time anyone typed a new name. `ReferenceWriter` creates on first use, per
  CLAUDE.md's "created as used". **Since 2026-08-14 they are no longer *foreign keys*** (DEC-018 dropped all
  six, across those four columns plus `ExpenseEntry.Category` and `BudgetGroupCategory.Category`), which makes
  this the sharpest item on the list rather than a solved one: nothing below the application will object any
  more. `ReferenceWriter` is the single door and must stay so.

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

### Every feature commit bumps `VERSION`

The root `VERSION` file is the single source of truth for image tags, and **a feature that ships without a
bump ships under the previous version's number**. Every feature since 0.4.0 has taken a **minor**, with the
bump folded into the feature commit itself rather than trailing behind it — check `git log -- VERSION` and the
pattern is plain.

```powershell
./scripts/release.ps1 -Minor -DryRun   # prints "0.9.0 -> 0.10.0" and exits
./scripts/release.ps1 -Minor -NoPush   # writes VERSION, builds images locally, leaves publishing to CI
```

Then `git add VERSION` **into the feature commit**, not a follow-up one. `-NoPush` is the documented path
(`docs/deployment-synology.md`): CI publishes `:latest` + `:<version>` on the push, and Watchtower recreates
the NAS containers within ~5 minutes. The images the script tags locally are throwaway.

`-Patch` for a fix, `-Major` when something breaks.

**CI enforces this**: since 2026-08-09 the `publish` job compares `VERSION` against the commit the push started
from and **publishes nothing when it is unchanged** — so a push that bumps nothing does not reach the NAS at
all. The build, tests and contract gate still run; only the Docker steps skip, and the run summary says so
loudly, because a silent non-deploy is the one failure this gate introduces. `workflow_dispatch` (Actions → CI
→ Run workflow) forces a publish without a bump, for a rebuild that is not a release.

> Written down 2026-08-09 because it was missed: the log-table search feature (`05885e5`) shipped with no
> bump and needed `4b178c2` to correct it a commit later.

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
- **A floating base-image tag under a patch-pinned `global.json` is a trap, and the error blames the wrong
  file.** `global.json` pins the SDK with `rollForward: latestPatch`, and roll-forward only ever goes *up* —
  no mode accepts an SDK **below** the pin. Docker never re-checks a floating tag it has already cached, so a
  `mcr.microsoft.com/dotnet/sdk:10.0` layer pulled before that patch shipped fails restore with *"A compatible
  .NET SDK was not found … Install the [10.0.301] .NET SDK or update [/src/global.json]"* — which reads as a
  code problem and is really a stale cache (cost a release build, 2026-08-07). Both Dockerfiles therefore pin
  the **SDK stage to an exact patch**, which is immutable and so can never be cached wrong; `release.ps1`
  passes **`--pull`** for the tags still floating (`aspnet:10.0`, `node:24-alpine`), where a stale layer runs
  fine but silently ages. Bump the Dockerfile SDK tag and the `global.json` pin together.
- **The NAS runs a *copy* of `deploy/docker-compose.yml`, and nothing keeps it current.** CI publishes images;
  it does not publish that file. A Container Manager **Project** then snapshots the YAML into DSM, so there are
  potentially three versions of it — repo, NAS disk, DSM. A key added to the committed compose file reaches the
  container only after the running copy is updated *and* the project is **rebuilt**: `${…}` interpolation lives
  in the YAML, so a copy predating a key has nowhere to put it, and the value is simply absent with nothing
  looking wrong. **And Watchtower recreates from the running container's spec, not the compose file** — so a
  container can take a brand-new image while carrying an environment assembled months earlier. That is exactly
  how 0.13.1 landed on the NAS with `Auth0__Management__*` empty, refusing an invited, verified address with
  "not yet invited" (2026-08-14). Diagnose with `docker compose exec webapi env | grep -E 'Auth0|Signup'`: the
  key **absent** means the YAML is stale, **present but empty** means the `.env` is not being read — two
  different fixes. `GET /api/meta` → `identityDeletionConfigured` answers it in one anonymous request, and
  since 0.13.2 the WebApi logs a `Sign-up posture:` line at every boot so a shut door is a stated fact rather
  than something you infer from a refusal.
- **A `MemoryStream` cannot reproduce Kestrel's refusal to write synchronously, and that hid a broken endpoint
  through a release.** `GET /api/account/export` 500'd on the NAS with *"Synchronous operations are disallowed.
  Call WriteAsync or set AllowSynchronousIO to true instead"* while all six of its tests were green
  (2026-08-14). The cause is not obvious from reading the code: **`JsonSerializer.Serialize(Utf8JsonWriter, …)`
  calls `writer.Flush()` when it returns** — always, synchronously, and there is no async overload taking a
  writer — so a `Utf8JsonWriter` pointed at `HttpResponse.Body` writes synchronously on *every* property,
  however carefully the caller awaits its own `FlushAsync`. `AccountExportService`'s two awaited flushes were
  correct and were never the ones doing the writing. The fix is a buffer the writer owns, drained to the
  destination with an awaited `CopyToAsync` at the same points (`BufferedOutput`) — **not** `AllowSynchronousIO`,
  which turns off the guard rather than stopping the write, on the one response shaped like a long transfer.
  The general lesson is the test double: a `MemoryStream` accepts sync writes, so no assertion about the
  *payload* could ever have caught this. `Export_never_writes_synchronously_to_its_destination` exports to an
  `AsyncOnlyStream` that throws the real exception with the real wording, and it was checked to fail against
  the old code before being kept.

`README.md` carries the specification (§1, §3–§6) and is the authority on scope. The numbering has gaps
because three sections moved to the documents that maintain them: the data model to
`docs/specs/2026-07-14-core-data-model/sub-specs/database-schema.md`, and the build order and the
nice-to-haves to `docs/product/roadmap.md` — which is now the authority on build order. Live specs are in
`docs/specs/`; `docs/product/decisions.md` overrides conflicting guidance here and is the first place to look
when something seems contradictory.

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
purchase" uses the manual figure. `MileageReading` exists precisely to decouple this; derive from the
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

## Architecture

.NET 10, PostgreSQL 17, React 19 on Vite, Aspire, EF Core, `ModelContextProtocol.AspNetCore`, docker-compose.
Nine projects under `src/`, all built and shipping in Docker images: `CarTracker.WebApp` (Vite React),
`.WebApi`, `.Gateway` (YARP), `.Data` (EF Core model + migrations), `.ModelContextProtocol`, `.Shared`,
`.Domain` (domain logic and derived metrics — the shared brain), `.ServiceDefaults`, `.AppHost` (Aspire).

The MCP server (§5) is the differentiator, hosted in-process in the same ASP.NET Core app over **Streamable
HTTP**. It reads the same domain service as the web UI. Two token scopes: read-only and read-write; every
write logs `source = "mcp"`. **The package question was settled by DEC-014** (2026-07-20):
`ModelContextProtocol.AspNetCore`, *not* Microsoft Agent Framework — a name `tech-stack.md` carried from
before that SDK existed and has since dropped. A tenth project, `CarTracker.Chat`, is specced but not built.

`CarTracker.Gateway` (DEC-009) is the single public origin: `/` → the app, `/api` → the WebApi, `/scalar`,
`/openapi` and `/mcp` → the WebApi, in dev and prod alike. **CORS is absent by design** — if you ever need it,
something has bypassed the gateway and that is the bug.

**Auth is Auth0, not the API key** (Phase 4.5, above). The fallback policy requires the Auth0 scheme, so
signing in is the way in and a global EF query filter scopes every vehicle query to the signed-in user. The
static `ApiKey:Value` / `X-Api-Key` still exists but **grants no vehicle access** — it fronts only the
anonymous meta and docs endpoints. MCP `AssistantToken` bearers are a third, separate mechanism with their own
scope claims.

## Vehicle facts worth knowing

BT53 AKJ — 2003 Land Rover Freelander 1, 1.8 SE, Rover K-series petrol, manual 5-speed, AWD via viscous
coupling (VCU). Bought 14 Mar 2026 at 76,632 mi. Two known frailties drive much of the spec's design: the
K-series head gasket (the weekly oil-filler-cap and coolant-colour checks are its early-warning system) and the
VCU (prolonged wheelspin can seize it and destroy the IRD/diff). Coolant must be OAT (red/pink) — never mixed
with IAT.
