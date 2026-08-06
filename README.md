# Car Tracker

A self-hosted maintenance and cost tracker for the cars you actually own, with an MCP server so an AI
assistant can read the same live data and log entries on your behalf.

It replaces a 13-sheet Excel workbook. That workbook's Dashboard sheet stores its derived figures, and five of
them are provably wrong: it double-counts every fill, so "total litres pumped" reads 1,112.94 against a real
556.47. It shows an MOT expiring in 23 days that was superseded by a pass logged three weeks earlier. It
averages MPG over an interval that never happened. None of these are typos — they are what happens when a
computed number gets a column to sit in and nobody recomputes it.

So nothing derived is stored here. Current mileage, per-fill MPG, spend rollups, cost-per-mile,
days-to-renewal, check status, budget variance — all of it is computed on read by one domain service, and both
the web UI and the MCP server call that same service. A figure cannot disagree with itself across surfaces,
because there is only one of it. The workbook's five bad figures are kept as regression tests.

The founding vehicle is BT53 AKJ, a 2003 Land Rover Freelander 1 bought at 76,632 miles.

## Screenshots

Desktop, showing the sample vehicle (BT53 AKJ). Every figure on every screen is computed live from the logs -
nothing derived is stored. A phone-oriented walkthrough with mobile captures lives in
[`docs/guide/USER-GUIDE.md`](docs/guide/USER-GUIDE.md).

**Dashboard - what needs you today, and what the car has cost**

![Dashboard: dossier, renewals, spend and fuel, regular checks - all derived](docs/images/dashboard-desktop.png)

**Fuel log - tank-to-tank MPG, price trends, and the fills table**

![Fuel log: fleet stats, MPG and price-over-time charts, and the fills table](docs/images/fuel-desktop.png)

**Service history - the MOT expiry is derived from the logged pass, never a stored date**

![Service history: the derived-MOT panel and the records table](docs/images/service-desktop.png)

**The garage - the multi-vehicle home screen**

![The garage: a vehicle card with live odometer, running cost, MOT and fuel, plus add-a-vehicle](docs/images/garage-desktop.png)

## Quickstart

```bash
dotnet run --project src/CarTracker.AppHost   # everything; app on http://localhost:5080
dotnet build
dotnet test          # needs Docker - Testcontainers starts a real PostgreSQL 17
```

Aspire brings up Postgres, the API, the gateway and the Vite dev server together, and the WebApi applies
migrations on startup in Development. One gotcha worth knowing before it costs you an afternoon:
**`ASPNETCORE_ENVIRONMENT` must be `Development` or user-secrets are not loaded** — a correct API key
returning 401 is almost always this.

Tests run against real PostgreSQL via Testcontainers, applying the real migrations. Not the in-memory
provider, which ignores column types, check constraints and FK behaviour — i.e. most of what the schema
asserts.

For the container stack (gateway + API + Postgres, plus a backup sidecar), see
[`docs/deployment-synology.md`](docs/deployment-synology.md).

## Tech

.NET 10, PostgreSQL 17, React 19 on Vite, EF Core, .NET Aspire for local orchestration, docker-compose for
deployment. The MCP server is hosted in-process in the same ASP.NET Core app using the official C# SDK
(`ModelContextProtocol.AspNetCore`), not as a separate deployable.

- `src/CarTracker.WebApp` - pure vite react app
- `src/CarTracker.WebApi` - Web API
- `src/CarTracker.Gateway` - YARP reverse proxy; the single public origin (DEC-009)
- `src/CarTracker.Data` - EF Core data model and migrations
- `src/CarTracker.ModelContextProtocol` - MCP server and protocol definition
- `src/CarTracker.Shared` - shared types and helpers
- `src/CarTracker.Domain` - domain logic and derived metrics
- `src/CarTracker.ServiceDefaults` - OpenTelemetry, health checks, service discovery, HTTP resilience
- `src/CarTracker.AppHost` - aspire host wiring the dependencies up
- `docs/` - current documentation
- `archive/` - original artifacts and design concepts

## Specification

What follows is the scope authority for the project. Two sections have moved to the documents that maintain
them properly, and the numbering is left alone so existing cross-references still resolve — hence the gaps:

- **§2 (data model)** → [`docs/specs/2026-07-14-core-data-model/sub-specs/database-schema.md`](docs/specs/2026-07-14-core-data-model/sub-specs/database-schema.md),
  which has the real tables, column types, constraints and the reasoning behind them.
- **§7 (build order) and §8 (nice-to-haves)** → [`docs/product/roadmap.md`](docs/product/roadmap.md), which
  tracks both against what has actually shipped.

## 1. Goals and principles

- Multiple vehicles are first-class: every record is scoped to a vehicle, the home screen is the garage, and one vehicle is the designated default for assistant calls that don't name one.
- Every derived number is computed server-side on read, never stored stale.
- Fast data entry from a phone - a fuel fill-up, marking a check done, logging a wash - is the primary daily use case.
- The MCP server exposes the same domain, so the assistant always reads live data and can log entries conversationally.
- The spreadsheet's history is entered through the MCP write tools by an agent, not by a bespoke importer (DEC-008). The workbook stays in `archive/` as the reference for those figures.

---

## 3. Core web features

Sixteen of these seventeen screens are built and running on BT53's real history. Documents (§3.9) is the
exception, and is called out below.

### 3.0 Garage (home)

- Landing screen: one card per vehicle - reg plate, name, status badge (Active / Sold / SORN), current mileage, and an attention summary (overdue/due-soon counts, next renewal with day count).
- Add-car flow: the vehicle form plus a choice of where its regular checks come from - start empty, a generic starter set, or copy from an existing vehicle. The starter set expands inline so it can be pruned to the car before the vehicle is created.
- Sold/SORN vehicles keep their history and stay browsable, but are visually parked and excluded from attention noise.
- Switching cars is navigation - the vehicle lives in the URL, not in hidden session state.

### 3.1 Dashboard (per-vehicle home)

Every computed value from the old Dashboard sheet, recomputed live on each load:

- Vehicle status: registration, current mileage, latest logged mileage, miles since purchase.
- Renewals and due dates with day-countdowns: MOT expiry, insurance expiry, road tax expiry, next service target (date and miles). Thresholds are red under 30 days, amber under 60. MOT expiry is derived from the last logged pass and has no stored field to go stale in.
- Spend YTD by group (fuel; service and repairs; insurance + tax + MOT) plus total since purchase, monthly average, and cost-per-mile since purchase.
- Fuel economy: average / best / worst MPG, total litres, avg price/L, last fill date, and full-tank range where a tank capacity is recorded.
- Action items: open DIY count, open workshop count, high-priority open count, open issues count, in-progress and scheduled counts.
- Regular checks status: overdue / due soon / attention / OK counts, last wash, days since last wash, last tyre check.

### 3.2 Logs (CRUD, table + quick-add)

Expenses, fuel, service history, tyre readings, wash log and mileage readings each get a filterable, sortable
table and a mobile-friendly quick-add sheet. Rows are editable and removable in place - click a row to open it
seeded for edit.

Fuel quick-add computes MPG as you type and warns on outliers, since an implausible figure usually means a
missed fill or a mistyped odometer rather than a real one. Every fill mirrors into expenses automatically,
which is what closes the £163 gap the workbook carried by logging one lumped "fuel to date" row instead of
per-fill entries.

### 3.3 Tasks (DIY + Workshop)

- Grouped-by-status board. Filter by kind, priority, status.
- A "bundle for next garage visit" view listing open workshop tasks with a summed estimated cost, ready to send to the garage.
- A completed workshop task converts into a ServiceRecord in one click, creating the record, its mileage reading and its mirrored expense in a single transaction.

### 3.4 Regular checks

- List with computed status per check and next-due date. A check whose latest log recorded Attention or Failed escalates to Attention whatever its date, and clears when a later log records OK - so a failed verdict cannot read as green.
- "Mark done today" creates a CheckLog with an optional result note. A batch action marks the weekly walk-around done in one go.

### 3.5 Budget

A category table with an editable annual budget against a derived YTD actual, showing remaining, % used and
over-budget highlighting, plus per-mile budgeted cost. The period toggles between calendar year, rolling 12
months and since purchase.

### 3.6 Issues watchlist

A severity-sorted list with the current observation and last-checked date editable inline, a worst-case total
cost, and a Monitoring/Resolved filter.

### 3.7 Equipment inventory

A table with owned / on-order / to-order totals, where the "to order" items double as a shopping shortlist.

### 3.8 Vehicle info / settings

The editable static reference. Fluid specs and tyre pressures live here, for looking up at the pump or the
wash. Expense categories, check definitions and garages are managed here too: a rename cascades to every row
pointing at it, and a delete is blocked with a count or re-homed rather than silently blanking references.

### 3.9 Documents

**Not built.** Upload and tag PDFs and photos (insurance docs, V5C, MOT certs, receipts, condition photo
sets), link them to a service record, expense or issue, and view or download them. It is the last screen
outstanding, and the only one that needs file upload - which is why it is last.

---

## 4. Derived logic and reminders engine

These calculations live in one service that both the web UI and the MCP server call, so the two cannot
disagree:

- Current mileage - the most recent MileageReading **by date**, not the highest reading. The workbook has a service row dated later at 83,000 miles against a current 80,712, and taking the maximum would enshrine that typo as the odometer.
- Per-fuel-entry MPG, L/100km, miles since last. A partial fill defers its MPG to the next full fill rather than posting two wrong figures.
- Fleet MPG stats (avg/best/worst), total litres, volume-weighted avg price/L.
- Spend rollups by category and group, running totals, cost-per-mile, monthly average.
- Days-to-renewal for MOT / insurance / tax / service (date and mileage based).
- Check status from last CheckLog + interval, escalated by the latest logged verdict.
- Budget YTD actual and variance.

**Reminders.** A hosted background service wakes on an interval, evaluates the same derived summary the
dashboard reads, and fans the result out to any registered notification channel: MOT/insurance/tax within N
days, service due by date or mileage, checks overdue, wash cadence exceeded (target every 3-4 weeks), tyre
check overdue. It re-derives nothing, so the badge and the dashboard are the same figure. The in-app badge is
the only channel built; email and push are registration points that DEC-006 leaves open.

---

## 5. MCP server (the key differentiator)

The domain is exposed as MCP tools so the assistant reads live data and can log on your behalf. Hosted
in-process in the same ASP.NET Core app, calling the same derived-metrics service the web UI does.

### 5.1 Transport and auth

- Streamable HTTP at `/mcp`, routed through the gateway like every other path, so it is reachable remotely from the Claude app (DEC-014 - this replaced the SSE transport named in the original spec).
- Scoped bearer tokens, minted in Settings → *Assistant access* with the secret shown once. A token is read-only or read-write, and the distinction is enforced by policy: every write tool carries the write scope, so a read-only token is physically refused rather than trusted not to try.
- Every write is recorded in an audit trail keyed to the token that made it; reads are counted on the token.
- Never expose it unauthenticated, and never over plaintext - the token is the whole boundary.

Connection recipe: [`docs/mcp-connect.md`](docs/mcp-connect.md).

### 5.2 Read tools

Every tool takes an optional `vehicle` (registration or id). Omitted, it resolves to the designated default
vehicle; an ambiguous or unknown name is an error, never a guess.

The read set covers every screen, not just the summaries. `get_due_items` is the important one: it is the
"what needs my attention" call, and it returns exactly what the dashboard's attention panel shows. Alongside
it sit the other derived summaries, a raw `list_*` per log, and `get_reference` for the questions that come up
at the pump - what oil does it take, what pressure for a full load.

### 5.3 Write tools (read-write token only)

Writes take the same optional `vehicle` parameter, and each one runs the same factory or service its web
equivalent runs - so a fill logged by voice and a fill typed on a phone produce identical rows. The catalogue
covers the logs, records and tasks, vehicle settings, and correction of existing rows. MOT expiry is
deliberately not settable: it stays derived from the logged pass. Full list with parameters in
[`docs/mcp-connect.md`](docs/mcp-connect.md).

**MCP design notes:**

- Tool descriptions should be explicit and example-rich so the model calls them correctly.
- Return structured JSON plus a short human summary string.
- Validate mileage monotonicity and flag anomalies rather than silently accepting them. A flag is retracted automatically when a later scan finds its condition gone, so a correction does not leave a stale warning behind.
- Log every write with source = "mcp" for auditability.

---

## 6. Non-functional

- **Getting history in:** no importer (DEC-008). The existing `.xlsx` history is entered through the MCP write tools by an agent, supervised against the workbook in `archive/`. The five figures its Dashboard gets wrong (DEC-012) are preserved as a hand-authored test fixture for the derived-metrics service, which is where their value always was.
- **Auth:** accounts are real. Auth0 fronts the web app (SPA client, API audience `cartracker.api`), and the fallback policy requires it, so signing in is the way in. Vehicles are owned: a single global EF query filter scopes every query to the signed-in user, which means a new endpoint cannot forget to filter - a vehicle you do not own simply never resolves. The static `X-Api-Key` still exists but grants no vehicle access; it fronts only the anonymous meta and docs endpoints (DEC-009). The MCP server's scoped tokens (§5.1) are a third, separate mechanism.
- **Backup:** `pg_dump` on a timer, plus a folder copy of the documents volume, to a second location. The compose stack runs a `db-backup` sidecar for this. One-click export back to Excel/CSV is a nice safety net and keeps parity with the old workflow.
- **Topology:** `CarTracker.Gateway` is the single public origin - the React app on `/`, the API on `/api`, Scalar on `/scalar`, the MCP server on `/mcp`. Identical in development and on the NAS, so **CORS is never needed** (DEC-009). If you ever find yourself needing it, something has bypassed the gateway and that is the bug.
- **Deployment:** `docker-compose` with gateway + API + Postgres, plus the backup sidecar and watchtower for image updates. Postgres lives on a host bind mount so it survives `down -v` and image rebuilds. Config via environment variables. HTTPS is mandatory, since the MCP endpoint carries a bearer token.
- **Audit trail:** created/updated timestamps and a source (web / mcp / import / seed) on every mutable entity.
- **Testing:** unit tests on the derived-metrics service - MPG, cost-per-mile, due-date logic - since that is where correctness matters most, and the workbook's five defects are the regression cases.
