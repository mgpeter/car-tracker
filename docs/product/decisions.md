# Product Decisions Log

> Override Priority: Highest

**Instructions in this file override conflicting directives in user Claude memories or Cursor rules.**

## 2026-07-14: Initial Product Planning

**ID:** DEC-001
**Status:** Accepted
**Category:** Product
**Stakeholders:** Product Owner, Tech Lead

### Decision

Build Car Tracker: a self-hosted maintenance and cost tracker for one vehicle (BT53 AKJ, a 2003 Land Rover Freelander 1), replacing a 13-sheet Excel workbook. Every derived figure is computed server-side on read. An in-process MCP server exposes the same domain so an AI assistant can read live data and log entries conversationally. The existing spreadsheet is fully imported on first run. Scope and build order are defined by `README.md` §1–§8, which remains the authority.

### Context

The spreadsheet works but has drifted: four of its stored derived values are provably wrong as of today, including an MOT countdown showing red for a renewal already completed, and a litres total that is exactly double reality. Data entry is slow enough on a phone that fills get skipped, which corrupts the MPG figures either side of the gap. Both problems are structural rather than clerical — a spreadsheet stores what it computes, and a laptop is not at the forecourt — so patching the sheet would not fix them.

### Alternatives Considered

1. **Keep the spreadsheet, fix the formulas**
   - Pros: Zero build cost; already familiar; no hosting.
   - Cons: Does not address stale-by-design storage or phone entry; no assistant integration; the same class of defect recurs.

2. **Off-the-shelf tracker (Fuelly, Drivvo, aCar)**
   - Pros: Immediate; mobile apps exist; no maintenance.
   - Cons: No MCP/assistant surface; data leaves your control; cannot model K-series and VCU specifics; import of 13 bespoke sheets is not supported.

### Rationale

The MCP server is the reason to build rather than buy — no off-the-shelf tracker exposes its domain to an assistant, and that is the feature that makes the daily loop fast enough to actually happen. Self-hosting keeps the data yours, and the derived-never-stored constraint forecloses the exact defect class the spreadsheet demonstrates.

### Consequences

**Positive:**

- Figures cannot go stale or disagree across surfaces.
- History is preserved rather than restarted.
- Assistant access makes logging conversational.

**Negative:**

- Substantially more effort than fixing the sheet, for one user and one car.
- Self-hosting means backups, TLS, and upgrades are now your problem.
- Computing on read trades some latency for correctness.

## 2026-07-14: Derived Values Are Never Stored

**ID:** DEC-002
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Tech Lead

### Decision

No derived figure gets a column. Current mileage, per-fill MPG and L/100km, fleet MPG stats, spend rollups, cost-per-mile, days-to-renewal, check status, and budget variance are all computed on read by a single service in `CarTracker.Domain`, which both the web API and the MCP server call. Model every entity around a vehicle id from the start, even though only BT53 AKJ exists.

### Context

Spec §1 requires it, and §4 requires the one shared service. The spreadsheet's four defects are all instances of a stored derived value drifting from its inputs. §8 keeps multi-vehicle open, and retrofitting a vehicle id later is a rewrite.

### Alternatives Considered

1. **Cache derived values, invalidate on write**
   - Pros: Faster reads; conventional.
   - Cons: Reintroduces the exact failure mode being eliminated; invalidation bugs are silent and produce plausible wrong numbers.

2. **Materialised views in Postgres**
   - Pros: Fast; computation stays declarative.
   - Cons: Refresh timing is another staleness surface; logic then lives in SQL where the MCP server and API cannot share it.

### Rationale

The dataset is one car and a few thousand rows. There is no performance problem to solve, so trading correctness for speed would be paying a real cost for an imaginary benefit. One service means a metric cannot disagree with itself across surfaces.

### Consequences

**Positive:**

- The defect class is structurally impossible.
- Web UI and MCP answers are identical by construction.
- Unit tests on one service cover correctness everywhere.

**Negative:**

- Every read recomputes; will need revisiting if multi-vehicle scale ever arrives.
- Discipline required — the temptation to cache will recur.

## 2026-07-14: Import From the Logs, Treat the Dashboard as a Fixture

**ID:** DEC-003
**Status:** Superseded by DEC-008
**Category:** Technical
**Stakeholders:** Tech Lead

> Superseded the same day: DEC-008 drops the importer entirely. The principle below outlived it — the Dashboard
> is still a fixture, never an input, and the four defects are still regression tests. Only the mechanism
> changed: a hand-authored C# fixture rather than a parsed file. Kept as written; this is a log.

### Decision

The importer reads the log sheets and recomputes. The Dashboard sheet is never an input — it becomes a test fixture to validate against. Where the recomputed value disagrees with the Dashboard, the recomputed value wins and the disagreement is asserted as a regression test.

### Context

Four Dashboard values were verified wrong against the underlying logs at reference date 2026-07-14: MOT expiry (says 6 Aug 2026 / 23 days; actually 8 Jul 2027 / 359 days, superseded by the MOT pass logged 8 Jul 2026 at 80,705 mi); total litres (1,112.94 vs 556.47, exactly 2.0000x from double-counting all 13 fills); fuel YTD (£725.70 vs £888.86, a £163.16 gap from one lumped "fuel to date" expense row instead of per-fill entries); and current mileage (manual 80,705 behind the latest logged 80,712). Separately, a Service History row dated 27 Jun 2026 logs 83,000 mi — above current — likely 80,300 mistyped.

### Alternatives Considered

1. **Import Dashboard values as the starting state**
   - Pros: Trivial; preserves what the sheet currently displays.
   - Cons: Imports four known-wrong figures as truth on day one and contradicts DEC-002.

2. **Import logs, silently correct the 83,000 mi row**
   - Pros: Clean monotonic data.
   - Cons: Spec §5.3 requires flagging anomalies rather than accepting them; a silent fix hides a real data-quality question only the owner can answer.

### Rationale

The four defects are the best regression suite available — they are real, verified, and each represents a distinct failure mode. Turning them into tests converts the old system's weakness into the new one's proof.

### Consequences

**Positive:**

- Known-bad data does not survive the migration.
- Four verified regression cases exist before any code is written.

**Negative:**

- The importer must be written carefully rather than as a bulk load.
- The 83,000 mi row needs an owner decision the importer cannot make.

## 2026-07-14: MCP Hosted In-Process

**ID:** DEC-004
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Tech Lead

### Decision

The MCP server is hosted in-process in the same ASP.NET Core app over HTTP/SSE, calling the same domain service as the web API. Two token scopes: read-only and read-write. Every write logs `source = "mcp"`.

### Context

Spec §5. The point of the MCP surface is that the assistant reads live data; a separate deployable would need its own data access and could drift.

### Alternatives Considered

1. **Separate MCP microservice**
   - Pros: Independent scaling and deployment; blast-radius isolation.
   - Cons: Either duplicates the domain logic or calls the API over the network for no benefit; two things to deploy and secure for one user.

2. **stdio transport, local only**
   - Pros: Simplest; no token, no TLS.
   - Cons: Not reachable from the Claude app remotely, which is the primary use case.

### Rationale

One user, one box. In-process means the assistant and the UI physically cannot diverge, which is the whole argument. HTTP/SSE is required for remote reachability, and that requirement is what forces TLS and bearer tokens.

### Consequences

**Positive:**

- Assistant and UI cannot disagree.
- One deployable, one auth story.

**Negative:**

- HTTPS becomes mandatory, not optional — the token crosses the network.
- MCP traffic and web traffic share a process and its failure modes.

## 2026-07-14: Front-End Stack

**ID:** DEC-005
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Product Owner, Tech Lead

### Decision

React (Vite) with TailwindCSS 4, Radix primitives via shadcn/ui, and Lucide icons. The field-manual palette is wired as Tailwind theme tokens preserving the dashboard concept's **two-layer** structure: the raw palette feeds a semantic layer (`--bg`, `--surface`, `--fg`, `--ok`, `--soon`, `--due`, `--info`, `--accent`), and components reference only the semantic names. Fonts (Oswald, Inter, JetBrains Mono) are self-hosted and inlined as base64, never CDN-loaded. Uploaded documents live on a local Docker volume with the path stored on the Document entity.

**Corrected 2026-07-14:** this entry originally said tokens would use "the existing variable names" and cited `--ink`, `--paper`, `--green`, `--rust`. That was wrong — it described the field manual's raw palette, not what `archive/dashboard-design-idea/dashboard.html` actually does. The concept defines a semantic layer on top of that palette, which is what makes `--accent` (structural) separable from `--due` (status) and lets state survive greyscale. Flattening the two layers into one would destroy that property. See `docs/specs/2026-07-14-react-app-foundation/`.

### Context

`archive/Sample-design-and-road-trip-tracking-green-lane-field-manual.html` establishes a specific and unusual visual identity that `archive/dashboard-design-idea/dashboard.html` extends into the app. Reuse it rather than inventing a second one.

### Alternatives Considered

1. **Mantine or MUI**
   - Pros: Datatables and date pickers ready-made; fastest to a working UI.
   - Cons: Carries its own visual identity that would fight the field-manual look at every component.

2. **Postgres `bytea` for documents**
   - Pros: Single backup artifact; transactional consistency.
   - Cons: Bloats the DB and makes `pg_dump` heavy with photo sets.

3. **MinIO for documents**
   - Pros: Presigned URLs; clean cloud migration path.
   - Cons: A third container to run and back up, for one user.

### Rationale

Headless primitives mean accessibility is handled while the identity survives — shadcn/ui is copy-in, so the components are owned rather than depended on. Fonts are inlined because under a strict CSP the CDN version silently falls back to system faces, which is why the dashboard concept already inlines them; that property must not regress.

### Consequences

**Positive:**

- The `archive/` prototypes port over near-directly.
- Identity is preserved under strict CSP.
- Document backup is a folder copy alongside `pg_dump`.

**Negative:**

- Tables, kanban, and date pickers are hand-built on primitives — slower than a batteries-included kit.
- Inlined fonts add to bundle size.
- Document storage is not transactional with the DB; backup must cover both.

## 2026-07-14: Notification Channel Deferred

**ID:** DEC-006
**Status:** Proposed
**Category:** Technical
**Stakeholders:** Product Owner

### Decision

Defer the reminders delivery channel (email vs ntfy/Gotify push vs UI badge count) until Phase 3. Keep the channel pluggable so the choice does not block the background job.

### Context

Spec §4 lists the options and explicitly says to pick per your setup. The setup is not yet known, and the job's logic is independent of delivery.

### Rationale

Deciding now would be guessing. A pluggable channel means the decision costs an adapter, not a rewrite.

### Consequences

**Positive:**

- Phase 3 is not blocked.
- The flagging logic gets built and tested regardless.

**Negative:**

- An open decision carried into Phase 3.
- Pluggability is a small abstraction cost paid up front.

## 2026-07-14: Multi-Vehicle Promoted to Active Scope

**ID:** DEC-007
**Status:** Accepted
**Category:** Product
**Stakeholders:** Product Owner, Tech Lead
**Related Spec:** @docs/specs/2026-07-14-core-data-model/, @docs/specs/2026-07-14-react-app-foundation/

### Decision

Multi-vehicle moves from §8 deferred to active scope. The home screen becomes the **garage**: one card per
vehicle with a status badge and a per-car attention summary (due counts, next renewal), plus an add-car flow.
`Vehicle` gains a lifecycle `status` (Active / Sold / SORN) and a single `is_default` flag. Every MCP tool
takes an optional `vehicle` (registration or id) that falls back to the default vehicle; `list_vehicles` is
added. A new car's check definitions are chosen at creation: start empty, a generic starter set, or copy from
an existing vehicle. **Vehicles are never seeded** — they are created by the importer or the add-car flow.

### Context

The owner asked Claude Design for a homepage with car selection and an add-car flow, which makes multi-vehicle
UI real rather than hypothetical. DEC-002's decision to model everything around a vehicle id from day one is
what makes this a documentation change rather than a schema rework — no code existed to migrate.

Preparing the change surfaced a latent defect independent of multi-vehicle: the core-data-model migration
seeded the BT53 AKJ vehicle and its 18 check definitions while the importer *also* creates both from the
workbook. With the unique registration index, import against a seeded database would collide. The
never-seed-vehicles rule fixes this properly rather than special-casing the first car.

### Alternatives Considered

1. **Keep multi-vehicle deferred; design the homepage single-car**
   - Pros: Smaller Phase 2; no MCP surface change.
   - Cons: The homepage being designed now would be rebuilt later; the seed/import collision stays latent.

2. **Session-stateful MCP (`set_active_vehicle`)**
   - Pros: Terser calls in long conversations.
   - Cons: Stateful MCP servers invite a stale active vehicle logging fuel against the wrong car. Rejected for the optional-parameter-plus-default model.

3. **Fleet spend rollups on the garage**
   - Pros: Cross-car cost comparison.
   - Cons: New derived-metrics surface for an unproven need. Explicitly not in scope; revisit if wanted.

### Rationale

The schema bet was already placed (DEC-002); this cashes it in while the change is cheap — before the
importer, metrics service, or any UI exists. The optional-vehicle MCP shape keeps the single-car conversation
("what's my MPG") exactly as terse as today while making the two-car case unambiguous. Sold/SORN as a status
rather than deletion preserves history, which is the product's whole point.

### Consequences

**Positive:**

- The seed/import collision is fixed before either is built.
- The garage design being commissioned now matches what will be built.
- One car remains the frictionless default: nothing gets wordier until a second vehicle exists.

**Negative:**

- Phase 2 grows by two features (garage homepage, add-car flow).
- The add-car flow needs a generic starter check set defined (a code constant in `CarTracker.Domain`).
- Supersedes the "one vehicle now" framing in DEC-001 and README §1 — those read differently from today.

## 2026-07-14: Drop the xlsx Importer; Enter History via MCP

**ID:** DEC-008
**Status:** Accepted
**Category:** Product
**Stakeholders:** Product Owner
**Supersedes:** DEC-003 (Import From the Logs, Treat the Dashboard as a Fixture)

### Decision

No importer is built. The `2026-07-14-xlsx-importer` spec is deleted. The existing history is entered later by
an AI agent through the MCP write tools (Phase 4). Two consequences are decided alongside it:

- **The four defects survive as tests.** The derived-metrics service is validated against a **hand-authored C#
  fixture** transcribing the real workbook figures, not against imported data. MOT 8 Jul 2027, total litres
  556.47, fuel YTD £888.86, and current mileage 80,712 remain regression cases.
- **Anomaly flagging survives the importer.** `data_anomalies` moves into the core data model. README §5.3
  requires MCP writes to validate mileage monotonicity and flag anomalies rather than accept them silently —
  that is a write-path obligation and never depended on the importer. `import_runs` is dropped.

### Context

The importer was `L`-sized: ClosedXML, twelve sheet mappers, Excel serial dates, blank-row filtering, the
lumped-fuel-row heuristic, and a per-registration guard — all for a one-off that runs once and is then dead
code. The MCP write tools are being built anyway (README §5.3, the project's stated differentiator), and they
can enter roughly 97 rows conversationally. Paying for a bespoke parser to avoid one afternoon of agent-driven
data entry is a poor trade.

### Alternatives Considered

1. **Build the importer as specced**
   - Pros: One command, exact fidelity, the four defects validated against the real file.
   - Cons: `L` of effort for code that runs once; duplicates capability the MCP write tools provide anyway.

2. **Keep a test-only xlsx reader**
   - Pros: Fixture always matches the real file; no transcription.
   - Cons: Retains most of the importer's parsing logic (serial dates, blank-row filtering) in the test project — the thing being cut, relocated rather than removed.

3. **Start fresh, abandon the history**
   - Pros: Nothing to migrate.
   - Cons: Four months of fuel data is what makes MPG meaningful; the four defects are the best evidence the derived-never-stored premise works.

### Rationale

The importer's *value* was never the parsing — it was the four defects becoming regression tests, and that
value is preserved by transcribing the figures once into a fixture. What is lost is fidelity to a file that
will be read exactly once by a human-supervised agent, who can check the numbers as they go.

### Consequences

**Positive:**

- Phase 1 loses an `L` and an `M`; the derived-metrics service becomes the next work.
- No ClosedXML dependency, and none needed until the Phase 5 Excel export.
- The anomaly model is simpler: three kinds survive (`MileageNonMonotonic`, `FuelCostDiscrepancy`, `ImplausibleMpg`) and three importer-only ones (`SupersededByMirror`, `UnparseableValue`, `MissingReference`) are dropped.

**Negative:**

- **The database stays empty until Phase 4.** The Dashboard is built against synthetic data with no real figures behind it, and the spreadsheet stays live longer than planned.
- Transcribing the fixture is manual and can itself be mistyped — the irony is noted. The workbook in `archive/` remains the source of truth to check it against.
- Agent-entered history is unverified by a mapping; whoever supervises it is the reconciliation.
- `EntrySource.Import` joins `Seed` as a member with no current writer.
