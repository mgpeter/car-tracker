# Product Roadmap

> Build order follows README §7, which is the authority. Phases group its seven steps; do not reorder without
> updating the spec.

## Phase 1: Foundation & Import

**Goal:** Get the full history out of the spreadsheet and into a schema that cannot store a stale derived value, with the shared brain that computes them proven by tests.

**Success Criteria:** All 13 sheets imported. The derived-metrics service reproduces every Dashboard figure that the old sheet got *right*, and the four known-bad figures resolve to their verified values (MOT 8 Jul 2027, 556.47 L, fuel YTD £888.86, mileage 80,712). The 83,000 mi service row is flagged, not swallowed.

### Features

- [ ] EF Core data model — all 14 entities per spec §2, vehicle id on everything from the start `L`
- [ ] Migrations + seed data — global reference data only (13 expense categories); vehicles are never seeded, they arrive via import or add-car (DEC-007) `S`
- [ ] xlsx importer — 13 sheets, Excel serial dates (epoch 1899-12-30), skip ~30 trailing blank Expense rows `L`
- [ ] Import validation pass — mileage monotonicity, anomaly flags, never-logged check state `M`
- [ ] Derived-metrics service — mileage, MPG, L/100km, spend rollups, cost-per-mile, days-to-renewal, check status, budget variance `L`
- [ ] Unit tests on derived metrics — old Dashboard as fixture, including the four defects as regression cases `M`

### Dependencies

- `archive/ORIGINAL-TRACKER-IN-EXCEL-Freelander_BT53AKJ_Tracker.xlsx` is the import source and the fixture
- Postgres running via Aspire / docker-compose

## Phase 2: Daily Loop

**Goal:** Make the phone-in-the-driveway case faster than the spreadsheet it replaces, and put the live Dashboard in front of it.

**Success Criteria:** A fill-up can be logged from a phone in under 30 seconds and its MPG appears immediately on the Dashboard. Every Dashboard figure is computed on read. The spreadsheet stops being opened.

### Features

- [ ] Design system foundation — Tailwind theme tokens, inlined fonts, status treatment (stripe + mono label first, colour second) `M`
- [ ] Garage homepage — one card per vehicle with status badge and attention summary, vehicle switcher (DEC-007) `M`
- [ ] Add-car flow — vehicle form plus check-source choice: empty / generic starter set / copy from existing `M`
- [ ] Dashboard — every computed value from spec §3.1, red <30 days / amber <60, per vehicle `L`
- [ ] Fuel log + quick-add — on-the-fly MPG, outlier warning, auto-mirror to expenses `M`
- [ ] Expense log + quick-add `M`
- [ ] Mileage readings log + quick-add `S`
- [ ] Regular checks — computed status, "mark done today", batch weekly walk-around `M`

### Dependencies

- Phase 1 derived-metrics service
- `archive/dashboard-design-idea/dashboard.html` as the Dashboard reference
- `archive/Sample-design-and-road-trip-tracking-green-lane-field-manual.html` for the visual identity

## Phase 3: Full Coverage & Reminders

**Goal:** Retire the spreadsheet entirely — every remaining sheet has a home — and stop requiring the app to be opened to learn something needs attention.

**Success Criteria:** No sheet in the workbook lacks an equivalent view. A renewal or overdue check surfaces without being looked for.

### Features

- [ ] Tasks (DIY + Workshop) — grouped by status, bundle-for-garage with summed cost, promote-to-service-record `L`
- [ ] Service history, tyre readings, wash log `M`
- [ ] Budget — editable targets, derived YTD, variance highlighting, period toggle `M`
- [ ] Issues watchlist + equipment inventory `M`
- [ ] Vehicle info / settings — fluid specs, tyre pressures, reference list management `M`
- [ ] Documents — upload, tag, link to record, viewer/download `M`
- [ ] Reminders background job — spec §4, pluggable channel `M`

### Dependencies

- Phase 2 design system and CRUD patterns
- Notification channel decision (email vs ntfy/Gotify vs UI badge) — see DEC-006

## Phase 4: MCP Server

**Goal:** The differentiator. Make the assistant a first-class client of the same domain, able to answer live and log on your behalf.

**Success Criteria:** "What needs my attention?" returns the same answer the Dashboard shows, because both called the same service. A spoken fill-up appears in the browser immediately, audited as `source = "mcp"`.

### Features

- [ ] MCP host — in-process, HTTP/SSE transport `M`
- [ ] Read tools — spec §5.2, `get_due_items` first; `list_vehicles` plus optional vehicle param with default-vehicle fallback (DEC-007) `L`
- [ ] Write tools — spec §5.3, mileage validation, auto-mirroring, `source = "mcp"` audit, same optional vehicle param `L`
- [ ] Token scopes — read-only and read-write, bearer auth `M`
- [ ] Tool description pass — explicit and example-rich; structured JSON plus a short human summary `S`

### Dependencies

- Phase 1 derived-metrics service (read tools call it directly)
- Phase 3 write paths exist and are validated
- HTTPS termination — the token must never cross plaintext

## Phase 5: Ship & Harden

**Goal:** Make it survivable — running unattended, backed up, and recoverable.

**Success Criteria:** A restore from backup reproduces the DB and documents. One-click export back to Excel/CSV keeps parity with the old workflow as a safety net.

### Features

- [ ] Backup — `pg_dump` on a timer plus documents folder copy to a second location `M`
- [ ] Export to Excel/CSV `M`
- [ ] Docker packaging — single image, compose with app + Postgres + reverse proxy, env config `M`
- [ ] Auth — single-user cookie auth or reverse-proxy-level `S`
- [ ] HTTPS + deployment hardening `S`

### Dependencies

- Phase 4 (MCP endpoint shapes the reverse-proxy and TLS requirements)

## Deferred (spec §8)

Not scheduled. Revisit once the daily loop is proven.

- Fuel price / MPG / spend trend charts
- DVLA/MOT lookup to auto-refresh expiry from the reg
- Barcode/receipt photo capture pre-filling an expense
- Estimated tank range on the Dashboard (not just via MCP)
- Fleet spend rollups on the garage (cross-car totals — explicitly excluded by DEC-007, revisit if wanted)
- Service-interval templates suggesting "next due" automatically
