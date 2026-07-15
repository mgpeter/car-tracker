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

**Corrected 2026-07-14, and the correction was itself half-wrong — see the 2026-07-15 note below.** This entry originally said tokens would use "the existing variable names" and cited `--ink`, `--paper`, `--green`, `--rust`. That was wrong — it described the field manual's raw palette, not what the dashboard concept does.

**Amended 2026-07-15 (fonts):** superseded on font delivery by **DEC-010** — `.woff2` extracted and served from `'self'`, not inlined base64. The CSP property this entry protects is preserved; only the mechanism changed.

**Amended 2026-07-15 (tokens):** the 2026-07-14 correction fixed the wrong half of its own error. It was right that the original variable names were wrong, and wrong to conclude the concept carries **two layers**. Verified against all three files: **neither `dashboard-design-idea/dashboard.html` nor `dashboard-full-claude-design/theme.css` contains a single raw-palette variable.** `--ink`/`--paper`/`--green`/`--orange`/`--rust`/`--blue` exist **only** in `archive/…green-lane-field-manual.html`. The concepts inherited the palette **as hex values, not as variables** — `dashboard.html:9` says so: *"Palette inherited from archive/…green-lane-field-manual.html"*.

So there is **one semantic layer**, and nothing to flatten. The property that matters is real and survives: `--accent` is structural and separate from `--due`, so state survives greyscale. But it is protected by a **comment beside the token**, not by a layer boundary — and the new `theme.css` **dropped that comment**. Restore it in `tokens.css`; it is the only thing guarding the rule.

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

## 2026-07-14: Gateway Topology and API-Key Auth

**ID:** DEC-009
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Product Owner, Tech Lead
**Related Spec:** @docs/specs/2026-07-14-react-app-foundation/

### Decision

A **`CarTracker.Gateway`** project (YARP) becomes the single public origin. It serves the React app at `/` and
proxies `/api`, `/scalar` and `/openapi` to the Web API — in development exactly as on the NAS. The API is
protected by a **static API key** from configuration (`ApiKey:Value`), sent as `X-Api-Key`; `/api/meta` stays
anonymous. The front-end holds the key in localStorage. **`CarTracker.ServiceDefaults`** is added as the ninth
project. **No CORS anywhere.**

This supersedes three things:

- `react-app-foundation/technical-spec.md` — *"Production: the API serves the built static assets. Same origin, no CORS, no second container."* The gateway is that second container.
- `roadmap.md` Phase 5 and `api-spec.md` — *"Auth lands in Phase 5."* It lands now.
- README §6 — *"simple cookie auth or a reverse-proxy-level auth (e.g. Authelia)"* as the near-term mechanism.

### Context

The owner wants the app reachable on one port on a NAS, with the API under `/api`, and a Scalar browser for the
API. One origin makes CORS unnecessary rather than something to configure — that is the point of the gateway,
not a side effect. An API key was wanted immediately rather than at Phase 5, because the thing will be exposed
long before Phase 5 arrives.

Modelled on `D:\repos\personal\bookmark-feeder`, a working Aspire 13 + YARP + Vite setup with the same shape.

### Alternatives Considered

1. **API serves the static assets (the original spec)**
   - Pros: One container; no gateway; already written down.
   - Cons: Couples the API to asset serving; no clean seam for TLS termination or routing MCP separately later.

2. **Infrastructure proxy (nginx / Caddy / Traefik) instead of a .NET project**
   - Pros: No extra .NET project; conventional for a NAS.
   - Cons: Routing config lives outside the solution and outside Aspire, so dev and prod diverge — the opposite of what was wanted.

3. **DB-backed API keys with scopes now**
   - Pros: Unifies with the MCP read/write tokens (§5.1) the design brief already specs.
   - Cons: Migration, hashing, management endpoints and UI, for one user with one key. Deferred to Phase 4, where MCP forces the question anyway.

### Rationale

One origin in dev and prod means path, origin and auth bugs surface locally instead of on deploy. A static
config key is the smallest thing that is genuinely secure for a single user, and it does not preclude the
scoped MCP tokens later. `/api/meta` stays open so the front-end can distinguish "no key yet" from "the API is
down" — two different problems needing two different messages.

### Consequences

**Positive:**

- CORS never enters the codebase.
- Verified working: React at `/`, API at `/api`, Scalar at `/scalar`, and **HMR over the gateway's WebSocket**.
- The gateway is a seam for TLS and for routing MCP separately in Phase 4.

**Negative:**

- Two processes to run instead of one, and a dev/prod split inside the gateway (proxy to Vite vs serve `dist`).
- **The key lives in localStorage, which is XSS-readable.** Acceptable for a single-user self-hosted app whose key guards one person's car data; the alternative is an HttpOnly cookie, which needs the login flow README §6 explicitly does not want yet. Revisit if this ever leaves the LAN or gains a second user.
- Auth arriving early means Phase 5's auth item becomes hardening, not greenfield.
- README's seven-project list becomes nine.

## 2026-07-15: Fonts Are Extracted to .woff2, Not Inlined

**ID:** DEC-010
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Product Owner, Tech Lead
**Amends:** DEC-005

### Decision

The three faces (Oswald, Inter, JetBrains Mono) are decoded from base64 to `.woff2` files under
`public/fonts/`, subset to Latin, and served from `'self'` with `font-src 'self'`. `font-display: block` for
Oswald; `swap` for Inter and JetBrains Mono.

### Context

This contradiction has been live across four documents for two days, and the docs disagreed 3–1 for inlining:

- `tech-stack.md:9` and `:24` — *"inlined as base64 data URIs"*, *"Keep this property."*
- DEC-005 — *"self-hosted and inlined as base64"*, *"that property must not regress"*
- `roadmap.md:38` — *"inlined fonts"*
- `react-app-foundation/sub-specs/technical-spec.md:127` — *"extract to .woff2, do not carry the base64 across"*, calling itself *"a deliberate divergence"* — with no decision entry to make it one.

Worse, `spec.md:17` and `:51` said *"inlined faces"* while `technical-spec.md:127` in the same folder said the
opposite. And the new design output re-inlined 135 KB at `font-display: block` for all three faces.

### Alternatives Considered

1. **Keep inlining**
   - Pros: matches DEC-005, tech-stack, the roadmap and both design outputs; zero amendments; port `fonts.css` nearly as-is.
   - Cons: ~33% base64 overhead, no separate caching, render blocked on the whole stylesheet.

2. **Defer the decision again**
   - Pros: unblocks the port immediately.
   - Cons: how it survived two days across four documents.

### Rationale

**The requirement was only ever *self-hosted*, not *inlined*.** CLAUDE.md records the reason as CSP: the field
manual loads fonts from a CDN, and under a strict CSP those silently degrade to system faces. `font-src 'self'`
preserves that property exactly. Inlining was a constraint of being one self-contained file — a *format*
constraint of the design artifact, not a design requirement — and in an app it is strictly worse on all three
counts above.

`block` for Oswald because it is the display face carrying the identity, above the fold in the page head; a
FOUT swapping condensed Oswald for Arial Narrow is more visible than a brief blank. `swap` for the other two.

### Consequences

**Positive:**

- Fonts cache independently of the CSS; a token change no longer re-downloads 135 KB.
- Smaller payload, and subsetting to Latin shrinks it further.
- The CSP property that motivated inlining is fully preserved.

**Negative:**

- Amends DEC-005 and requires edits to `tech-stack.md`, `roadmap.md`, and the react spec's own contradicting deliverable.
- A one-off decode/subset step, and the extracted files must be regenerated if the design ships new faces.
- Diverges from both design artifacts, so `archive/` and `src/` differ on this point by design.

## 2026-07-15: Average Price Per Litre Is Volume-Weighted

**ID:** DEC-011
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Product Owner, Tech Lead

### Decision

Average price per litre is `SUM(totalCost) / SUM(litres)` — **1.597324** on the real history. The workbook's
Dashboard reports **1.594923**, a plain mean of the price column. Both are correct answers to different
questions; this is the answer to the one worth asking.

This is a **definition difference, not a defect**. The four defects stand at four.

### Context

The derived-metrics spec predicted this exactly, and instructed: *"it must be reported to the owner rather than
silently resolved in either direction. Do not change the formula to match the sheet without a decision. Record
the outcome as a decision entry."*

The finding landed as predicted — the sheet's figure matched a simple mean to 16 digits (20.734 ÷ 13). The code
shipped volume-weighted. **The entry was never written** — which is precisely the silent resolution the spec
forbade, even though the outcome is the one it recommended. This closes that gap.

### Alternatives Considered

1. **Match the sheet's simple mean**
   - Pros: every Dashboard figure reproduces; no explaining why a number moved.
   - Cons: answers a question nobody asks. A 50 L fill at £1.40 and a 10 L fill at £1.60 cost £1.433/L, not £1.50 — the mean weights a splash equally with a brim.

2. **Expose both**
   - Pros: no information lost.
   - Cons: two numbers labelled "average price" is worse than one right one. Unlike cumulative-vs-per-fill MPG, where a divergence is a real signal, this divergence is just arithmetic.

### Rationale

"What did fuel cost me per litre" is a question about money over volume. The sheet's mean is a fact about its
own price column, not about the fuel. The gap is small — 0.24p/L — which is exactly why it needs recording:
small enough to look like rounding, and it is not.

### Consequences

**Positive:**

- The figure answers the question its label implies.
- The reason a Dashboard number differs is now written down rather than living in a test comment.

**Negative:**

- A fifth figure differs from the sheet, on top of the four defects — and this one is not the sheet's mistake.
- Anyone reconciling against the old workbook will find it and must be told why.

## 2026-07-15: The Sheet's Invented First Interval Is a Fifth Defect

**ID:** DEC-012
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Product Owner, Tech Lead

### Decision

The workbook's Fuel Log computes an MPG for its **first** fill from a "miles since last" that has no basis in
its own data. This is a **fifth defect**, not a definition difference. The project's framing becomes *five
defects*; `CLAUDE.md`, `roadmap.md` and the derived-metrics spec are amended.

DEC-011's average-price difference stays **outside** the count — that one is a definition, this one is a
fabrication.

### Context

Fuel Log row 4 carries `miles since last = 334` against a mileage of 77,537, implying a previous reading of
77,203. No such reading exists anywhere in the workbook — the purchase was at 76,632, and row 4 is the first
fill recorded. The interval is invented.

That fabricated 334 miles yields **24.49 mpg**, which the Dashboard then reports as **Worst MPG** (row 13) and
folds into a 13-value **Average MPG** (row 11). So two headline figures rest on a number with no source.

The derived-metrics spec called it *"arguably a sixth defect"* and left it undecided.

### Alternatives Considered

1. **Leave it as an observation, keep saying "four defects"**
   - Pros: the four are verified, quotable and load-bearing across the docs; renumbering costs edits.
   - Cons: this one is the same *kind* of thing — a stored figure with no support in the logs — and it corrupts two Dashboard headlines. Excluding it because the count is already written down is the wrong reason.

2. **Count it and DEC-011 both, making six**
   - Pros: symmetric.
   - Cons: conflates a fabrication with a definition difference. The average-price gap is the sheet answering a different question correctly; this is the sheet answering the right question from invented input.

### Rationale

The test for a defect has been: *does the stored figure disagree with the logs it claims to summarise?* This
one does — more starkly than some of the four, because there is no underlying row at all. Worst MPG is not
merely stale; it is derived from a measurement that never happened.

Our service measures 12 intervals from 13 fills and reports Worst MPG as 25.42.

### Consequences

**Positive:**

- The count matches the evidence, and the fifth is already covered by tests.
- Strengthens the premise: the sheet does not only go stale, it invents.

**Negative:**

- `CLAUDE.md`, `roadmap.md`, `mission.md` and the derived-metrics spec all say "four" and need amending — the four-defect table is quoted widely.
- A reader of older commits will find the four-defect framing and must reconcile it.

## 2026-07-15: Icon Glyphs Become an SVG Sprite

**ID:** DEC-013
**Status:** Accepted, **amended same day — see Amendment below**
**Category:** Technical
**Stakeholders:** Product Owner, Tech Lead
**Amends:** DEC-010

### Decision

The 15 non-ASCII glyphs the design uses as icons are replaced by an SVG sprite at `public/icons.svg` and an
`<Icon>` component, built in `react-app-foundation` task 4. No app text depends on a glyph absent from the
three self-hosted faces.

### Context

DEC-010 requires fonts to load from `'self'` so that a strict CSP cannot silently degrade them to system
faces. While extracting the fonts (task 2.1) the subsets were checked against what the 17 screens actually
render, and **the design's own subset omits 15 glyphs the design itself uses**:

| Glyph | Uses | Role |
|---|---|---|
| `→` | 69 | section links — *"Underlying expenses →"* |
| `＋` | 29 | the quick-add FAB and `＋ Fuel` buttons |
| `✓` | 23 | mark-done buttons |
| `▾` | 16 | the More dropdown caret |
| `⌂` | 16 | Garage link |
| `⇄` | 14 | the `⇄ mirror` tag on auto-mirrored expenses |
| `⠿` | 8 | drag grips in the quick-add settings list |
| `Δ` | 3 | the mileage log's `Δ prior` column |
| `⚙` `₂` `≈` `≡` `↔` `↑` `↓` | 1–4 each | assorted |

They render correctly in the design's standalone HTML only because the **system font** supplies them. That is
exactly the degradation DEC-010 exists to prevent, and it is invisible to a per-font check: fallback happens
**per glyph**, so `react-app-foundation` task 2.7 ("verify fonts load from `'self'` with no system fallback")
would have passed while nine icons quietly came from Segoe UI Symbol.

Re-subsetting cannot close it. `⠿` is U+283F, a *Braille pattern*; `⌂`, `⚙` and `⇄` appear in no Inter, Oswald
or JetBrains Mono at any subset level. Only `→ ✓ Δ ₂ ≈ ≡ ↑ ↓` exist upstream at all.

### Alternatives Considered

1. **Re-subset from full Google Fonts sources, SVG only for the rest**
   - Pros: keeps the design's markup verbatim for 8 of the 15.
   - Cons: needs a font download and a subsetting step in the build; adds ~10–20KB; still needs SVG for
     `⠿ ⌂ ⚙ ⇄`, so it buys a second mechanism rather than replacing one.

2. **Declare an explicit symbol fallback (`'Segoe UI Symbol'`, etc.)**
   - Pros: cheapest; markup unchanged.
   - Cons: the FAB and the grips render differently per OS — the FAB is a primary control on the phone case
     the product is built around. Requires amending DEC-010 to say text loads from `'self'` but symbols may
     not, which retracts the property DEC-010 was written to establish.

### Rationale

Every one of the 15 is an icon wearing a glyph's clothes. Using text glyphs for iconography is what created
the trap: it made a rendering dependency invisible to both the CSP and the font check. An SVG sprite is the
conventional answer, is inspectable, scales with `currentColor`, and makes DEC-010's "no system fallback"
literally true rather than nominally true.

It also removes a class of bug this project is otherwise strict about: `Δ prior` and `⇄ mirror` are *data
labels*, and a label that renders as a tofu box on a machine without the glyph is the front-end cousin of a
stale derived figure.

### Consequences

**Positive:**

- DEC-010's guarantee becomes checkable end to end, and task 2.7's claim becomes true.
- Icons gain accessible names — `<Icon>` takes a label or is explicitly `aria-hidden`, where a bare `✓` in a
  `<button>` today is an unlabelled control.
- The scaffold's `public/icons.svg` (Vite's bluesky/discord/github junk) gets replaced rather than shipped.

**Negative:**

- Task 4 grows: ~10 symbols to draw, and 15 glyph sites across 17 screens to replace during the port.
- The port is no longer a verbatim transcription of the design's markup at those sites; the sprite is a
  deliberate divergence and must be checked visually against the concept.


### Amendment (2026-07-15, during task 4 stage 1) — 8 sprite, 7 font-subset

**The decision above was wrong on its own evidence, and this corrects it.** It swept all 15 glyphs into the
sprite while its own Context table names `→ ✓ Δ ₂ ≈ ≡ ↑ ↓` as glyphs that *do* exist upstream. Seven of the
fifteen are not icons at all:

| Glyph | Where | Why it cannot be an icon |
|---|---|---|
| `₂` | `Compression + CO₂ sniff test` | It is **inside a word**. |
| `Δ` | `Δ prior` column header; `Δ computed vs 24 Jun` | A header and running prose. |
| `≈` | `≈ 206 days at 33 mi/day` | Drop it and an approximation reads as a fact. |
| `≡` | `28.7 MPG ≡ 9.8 L/100 km` | Asserts equivalence mid-sentence. |
| `↔` | `Fuel ↔ expense mirror` | Part of the rule's name. |
| `↑` | `front ↑` in the tyre diagram | The arrow *is* the orientation. |
| `↓` | `sorted · date ↓` | The only thing saying *descending*. |

So: **`→ ＋ ✓ ▾ ⌂ ⇄ ⠿ ⚙` become the sprite; `Δ ₂ ≈ ≡ ↔ ↑ ↓` go into the font subset.** This is the hybrid the
original decision rejected as "a second mechanism rather than replacing one" — and that reasoning was sound
about `⠿ ⌂ ⚙ ⇄`, which no face ships, but it does not survive contact with `₂` sitting inside "CO₂".

Implemented: Inter and JetBrains Mono re-subset from the upstream OFL variable TTFs. Both got **smaller**
(101,160 → 97,356 B total) while gaining coverage, because the work also restored axis parity with the shipped
build — Inter's upstream `opsz` axis pinned (CSS applies it automatically, so shipping it would have silently
changed rendering), JetBrains Mono's `wght` clamped from `100–800` back to `400–800`. Verified in Chrome by
whether the *named face supplied the glyph*, not by eye.

**Two gaps remain, and no subsetting closes either:**

- **Oswald has no `₂`.** `tasks.dc.html:184` puts CO₂ in an `<h4>`, which is `var(--disp)`, so that heading
  takes "CO" from Oswald and `₂` from a system face. The other three CO₂ sites are body copy and resolve to
  Inter, which has it. A screens-spec decision for the tasks screen: span the `₂` in the body face, use a real
  `<sub>`, or accept it.
- **`≡` is absent from Inter upstream** (2,849 codepoints, not that one). It only ever appears in `.cfoot`,
  which is `var(--mono)`, so JetBrains Mono covers it. If it ever moves into body copy it will fall back.

The original decision's consequences stand otherwise: icons gained accessible names, and `public/icons.svg` —
Vite starter junk carrying a raw `#aa3bff` and referenced by nothing — is deleted. The claim that the sprite
makes "no system fallback" *literally* true is now accurate for text, with the single Oswald `₂` exception
named above.

### Implementation note (2026-07-15, task 4 stage 6)

Both gaps above are now visible rather than latent, and the sprite is proved in a browser: eight symbols
resolving from an inline `<use>` under the enforced CSP, zero violations, with the FAB's icon rendering in the
bottom nav at ≤900px.

One consequence worth recording, because it outlives this decision: **the icons had to move to `src/`, not
`public/`.** `tokens.test.ts` only walked `src/**`, and `public/` is copied verbatim into the build — which is
exactly how the Vite starter's `icons.svg` sat there carrying a raw `#aa3bff`, referenced by nothing, for the
whole life of the scaffold. In `src/` the guard forces `currentColor`. Hardening that guard to walk `public/`
then caught two more starter artefacts, including `favicon.svg`: **the app's browser tab had been showing
Vite's logo**. It is now a number plate on dossier green, and the single exemption the guard grants — a favicon
is rendered by browser chrome and can reach no CSS variable, so it is exempt because it *cannot* comply.
