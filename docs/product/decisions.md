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

> **Three clauses here have been superseded, each by a later DEC.** Recorded so this entry is not read as
> current: **"one vehicle"** → DEC-007 promoted multi-vehicle to active scope; **"fully imported on first
> run"** → DEC-008 dropped the importer entirely, and history is entered through the MCP write tools;
> **"build order defined by README §1–§8"** → §7 and §8 moved to `docs/product/roadmap.md`, which is now the
> authority on build order. The core of the decision - derived-never-stored, in-process MCP over one domain -
> is unchanged and is why the project exists.

### Context

The spreadsheet works but has drifted: four of its stored derived values are provably wrong as of today, including an MOT countdown showing red for a renewal already completed, and a litres total that is exactly double reality. Data entry is slow enough on a phone that fills get skipped, which corrupts the MPG figures either side of the gap. Both problems are structural rather than clerical - a spreadsheet stores what it computes, and a laptop is not at the forecourt - so patching the sheet would not fix them.

### Alternatives Considered

1. **Keep the spreadsheet, fix the formulas**
   - Pros: Zero build cost; already familiar; no hosting.
   - Cons: Does not address stale-by-design storage or phone entry; no assistant integration; the same class of defect recurs.

2. **Off-the-shelf tracker (Fuelly, Drivvo, aCar)**
   - Pros: Immediate; mobile apps exist; no maintenance.
   - Cons: No MCP/assistant surface; data leaves your control; cannot model K-series and VCU specifics; import of 13 bespoke sheets is not supported.

### Rationale

The MCP server is the reason to build rather than buy - no off-the-shelf tracker exposes its domain to an assistant, and that is the feature that makes the daily loop fast enough to actually happen. Self-hosting keeps the data yours, and the derived-never-stored constraint forecloses the exact defect class the spreadsheet demonstrates.

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
- Discipline required - the temptation to cache will recur.

## 2026-07-14: Import From the Logs, Treat the Dashboard as a Fixture

**ID:** DEC-003
**Status:** Superseded by DEC-008
**Category:** Technical
**Stakeholders:** Tech Lead

> Superseded the same day: DEC-008 drops the importer entirely. The principle below outlived it - the Dashboard
> is still a fixture, never an input, and the four defects are still regression tests. Only the mechanism
> changed: a hand-authored C# fixture rather than a parsed file. Kept as written; this is a log.

### Decision

The importer reads the log sheets and recomputes. The Dashboard sheet is never an input - it becomes a test fixture to validate against. Where the recomputed value disagrees with the Dashboard, the recomputed value wins and the disagreement is asserted as a regression test.

### Context

Four Dashboard values were verified wrong against the underlying logs at reference date 2026-07-14: MOT expiry (says 6 Aug 2026 / 23 days; actually 8 Jul 2027 / 359 days, superseded by the MOT pass logged 8 Jul 2026 at 80,705 mi); total litres (1,112.94 vs 556.47, exactly 2.0000x from double-counting all 13 fills); fuel YTD (£725.70 vs £888.86, a £163.16 gap from one lumped "fuel to date" expense row instead of per-fill entries); and current mileage (manual 80,705 behind the latest logged 80,712). Separately, a Service History row dated 27 Jun 2026 logs 83,000 mi - above current - likely 80,300 mistyped.

### Alternatives Considered

1. **Import Dashboard values as the starting state**
   - Pros: Trivial; preserves what the sheet currently displays.
   - Cons: Imports four known-wrong figures as truth on day one and contradicts DEC-002.

2. **Import logs, silently correct the 83,000 mi row**
   - Pros: Clean monotonic data.
   - Cons: Spec §5.3 requires flagging anomalies rather than accepting them; a silent fix hides a real data-quality question only the owner can answer.

### Rationale

The four defects are the best regression suite available - they are real, verified, and each represents a distinct failure mode. Turning them into tests converts the old system's weakness into the new one's proof.

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

> **Amended by DEC-014 (2026-07-20): the transport is Streamable HTTP, not HTTP/SSE.** SSE was the transport
> the protocol had when this was written; it has since been superseded upstream. Everything else here stands -
> in-process, one domain service, two token scopes, `source = "mcp"` on every write. DEC-014 declared this
> amendment; the marker was never added here, so anyone reading the log in order met the old transport first.

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

- HTTPS becomes mandatory, not optional - the token crosses the network.
- MCP traffic and web traffic share a process and its failure modes.

## 2026-07-14: Front-End Stack

**ID:** DEC-005
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Product Owner, Tech Lead

### Decision

React (Vite) with TailwindCSS 4, Radix primitives via shadcn/ui, and Lucide icons. The field-manual palette is wired as Tailwind theme tokens preserving the dashboard concept's **two-layer** structure: the raw palette feeds a semantic layer (`--bg`, `--surface`, `--fg`, `--ok`, `--soon`, `--due`, `--info`, `--accent`), and components reference only the semantic names. Fonts (Oswald, Inter, JetBrains Mono) are self-hosted and inlined as base64, never CDN-loaded. Uploaded documents live on a local Docker volume with the path stored on the Document entity.

**Corrected 2026-07-14, and the correction was itself half-wrong - see the 2026-07-15 note below.** This entry originally said tokens would use "the existing variable names" and cited `--ink`, `--paper`, `--green`, `--rust`. That was wrong - it described the field manual's raw palette, not what the dashboard concept does.

**Amended 2026-07-15 (fonts):** superseded on font delivery by **DEC-010** - `.woff2` extracted and served from `'self'`, not inlined base64. The CSP property this entry protects is preserved; only the mechanism changed.

**Amended 2026-07-15 (tokens):** the 2026-07-14 correction fixed the wrong half of its own error. It was right that the original variable names were wrong, and wrong to conclude the concept carries **two layers**. Verified against all three files: **neither `dashboard-design-idea/dashboard.html` nor `dashboard-full-claude-design/theme.css` contains a single raw-palette variable.** `--ink`/`--paper`/`--green`/`--orange`/`--rust`/`--blue` exist **only** in `archive/…green-lane-field-manual.html`. The concepts inherited the palette **as hex values, not as variables** - `dashboard.html:9` says so: *"Palette inherited from archive/…green-lane-field-manual.html"*.

So there is **one semantic layer**, and nothing to flatten. The property that matters is real and survives: `--accent` is structural and separate from `--due`, so state survives greyscale. But it is protected by a **comment beside the token**, not by a layer boundary - and the new `theme.css` **dropped that comment**. Restore it in `tokens.css`; it is the only thing guarding the rule.

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

Headless primitives mean accessibility is handled while the identity survives - shadcn/ui is copy-in, so the components are owned rather than depended on. Fonts are inlined because under a strict CSP the CDN version silently falls back to system faces, which is why the dashboard concept already inlines them; that property must not regress.

### Consequences

**Positive:**

- The `archive/` prototypes port over near-directly.
- Identity is preserved under strict CSP.
- Document backup is a folder copy alongside `pg_dump`.

**Negative:**

- Tables, kanban, and date pickers are hand-built on primitives - slower than a batteries-included kit.
- Inlined fonts add to bundle size.
- Document storage is not transactional with the DB; backup must cover both.

## 2026-07-14: Notification Channel Deferred

**ID:** DEC-006
**Status:** Accepted (2026-08-07 - resolved to the UI badge; see the amendment below)
**Category:** Technical
**Stakeholders:** Product Owner

### Decision

Defer the reminders delivery channel (email vs ntfy/Gotify push vs UI badge count) until Phase 3. Keep the channel pluggable so the choice does not block the background job.

> **Amended 2026-08-07 - the deferral has expired and the choice was made.** Phase 3 shipped the reminders
> engine (2026-07-19) with `InAppBadgeChannel` as the sole registered `INotificationChannel`, and the badge has
> been the delivery mechanism ever since. So the channel is **the in-app badge**, not an open question - the
> status sat at *Proposed* for the whole life of the shipped feature, which is not the same claim.
>
> Email and push remain *unbuilt registration points* behind `INotificationChannel`. That is deliberate and
> still true: the seam exists, nothing occupies it, and adding one needs no change to the evaluator. But
> "we chose the badge and left the seam open" is a different statement from "we have not chosen", and three
> documents were making the second one.

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
an existing vehicle. **Vehicles are never seeded** - they are created by the importer or the add-car flow.

### Context

The owner asked Claude Design for a homepage with car selection and an add-car flow, which makes multi-vehicle
UI real rather than hypothetical. DEC-002's decision to model everything around a vehicle id from day one is
what makes this a documentation change rather than a schema rework - no code existed to migrate.

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

The schema bet was already placed (DEC-002); this cashes it in while the change is cheap - before the
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
- Supersedes the "one vehicle now" framing in DEC-001 and README §1 - those read differently from today.

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
  requires MCP writes to validate mileage monotonicity and flag anomalies rather than accept them silently -
  that is a write-path obligation and never depended on the importer. `import_runs` is dropped.

### Context

The importer was `L`-sized: ClosedXML, twelve sheet mappers, Excel serial dates, blank-row filtering, the
lumped-fuel-row heuristic, and a per-registration guard - all for a one-off that runs once and is then dead
code. The MCP write tools are being built anyway (README §5.3, the project's stated differentiator), and they
can enter roughly 97 rows conversationally. Paying for a bespoke parser to avoid one afternoon of agent-driven
data entry is a poor trade.

### Alternatives Considered

1. **Build the importer as specced**
   - Pros: One command, exact fidelity, the four defects validated against the real file.
   - Cons: `L` of effort for code that runs once; duplicates capability the MCP write tools provide anyway.

2. **Keep a test-only xlsx reader**
   - Pros: Fixture always matches the real file; no transcription.
   - Cons: Retains most of the importer's parsing logic (serial dates, blank-row filtering) in the test project - the thing being cut, relocated rather than removed.

3. **Start fresh, abandon the history**
   - Pros: Nothing to migrate.
   - Cons: Four months of fuel data is what makes MPG meaningful; the four defects are the best evidence the derived-never-stored premise works.

### Rationale

The importer's *value* was never the parsing - it was the four defects becoming regression tests, and that
value is preserved by transcribing the figures once into a fixture. What is lost is fidelity to a file that
will be read exactly once by a human-supervised agent, who can check the numbers as they go.

### Consequences

**Positive:**

- Phase 1 loses an `L` and an `M`; the derived-metrics service becomes the next work.
- No ClosedXML dependency, and none needed until the Phase 5 Excel export.
- The anomaly model is simpler: three kinds survive (`MileageNonMonotonic`, `FuelCostDiscrepancy`, `ImplausibleMpg`) and three importer-only ones (`SupersededByMirror`, `UnparseableValue`, `MissingReference`) are dropped.

**Negative:**

- **The database stays empty until Phase 4.** The Dashboard is built against synthetic data with no real figures behind it, and the spreadsheet stays live longer than planned.
- Transcribing the fixture is manual and can itself be mistyped - the irony is noted. The workbook in `archive/` remains the source of truth to check it against.
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
proxies `/api`, `/scalar` and `/openapi` to the Web API - in development exactly as on the NAS. The API is
protected by a **static API key** from configuration (`ApiKey:Value`), sent as `X-Api-Key`; `/api/meta` stays
anonymous. The front-end holds the key in localStorage. **`CarTracker.ServiceDefaults`** is added as the ninth
project. **No CORS anywhere.**

This supersedes three things:

- `react-app-foundation/technical-spec.md` - *"Production: the API serves the built static assets. Same origin, no CORS, no second container."* The gateway is that second container.
- `roadmap.md` Phase 5 and `api-spec.md` - *"Auth lands in Phase 5."* It lands now.
- README §6 - *"simple cookie auth or a reverse-proxy-level auth (e.g. Authelia)"* as the near-term mechanism.

### Context

The owner wants the app reachable on one port on a NAS, with the API under `/api`, and a Scalar browser for the
API. One origin makes CORS unnecessary rather than something to configure - that is the point of the gateway,
not a side effect. An API key was wanted immediately rather than at Phase 5, because the thing will be exposed
long before Phase 5 arrives.

Modelled on `D:\repos\personal\bookmark-feeder`, a working Aspire 13 + YARP + Vite setup with the same shape.

### Alternatives Considered

1. **API serves the static assets (the original spec)**
   - Pros: One container; no gateway; already written down.
   - Cons: Couples the API to asset serving; no clean seam for TLS termination or routing MCP separately later.

2. **Infrastructure proxy (nginx / Caddy / Traefik) instead of a .NET project**
   - Pros: No extra .NET project; conventional for a NAS.
   - Cons: Routing config lives outside the solution and outside Aspire, so dev and prod diverge - the opposite of what was wanted.

3. **DB-backed API keys with scopes now**
   - Pros: Unifies with the MCP read/write tokens (§5.1) the design brief already specs.
   - Cons: Migration, hashing, management endpoints and UI, for one user with one key. Deferred to Phase 4, where MCP forces the question anyway.

### Rationale

One origin in dev and prod means path, origin and auth bugs surface locally instead of on deploy. A static
config key is the smallest thing that is genuinely secure for a single user, and it does not preclude the
scoped MCP tokens later. `/api/meta` stays open so the front-end can distinguish "no key yet" from "the API is
down" - two different problems needing two different messages.

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

> **Amended 2026-08-07 - the revisit condition was met and acted on.** "Revisit if this ever gains a second
> user" is exactly what happened: DEC-016 replaced the localStorage key with Auth0 as the way in (2026-07-24).
> The static key still exists and is still in localStorage, but it now **grants no vehicle access** - it fronts
> only the anonymous meta and docs endpoints, so the XSS exposure above is no longer a route to anyone's car
> data. The gateway topology and the CORS-is-never-needed property in this DEC are untouched.

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

- `tech-stack.md:9` and `:24` - *"inlined as base64 data URIs"*, *"Keep this property."*
- DEC-005 - *"self-hosted and inlined as base64"*, *"that property must not regress"*
- `roadmap.md:38` - *"inlined fonts"*
- `react-app-foundation/sub-specs/technical-spec.md:127` - *"extract to .woff2, do not carry the base64 across"*, calling itself *"a deliberate divergence"* - with no decision entry to make it one.

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
preserves that property exactly. Inlining was a constraint of being one self-contained file - a *format*
constraint of the design artifact, not a design requirement - and in an app it is strictly worse on all three
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

Average price per litre is `SUM(totalCost) / SUM(litres)` - **1.597324** on the real history. The workbook's
Dashboard reports **1.594923**, a plain mean of the price column. Both are correct answers to different
questions; this is the answer to the one worth asking.

This is a **definition difference, not a defect**. The four defects stand at four.

> **Superseded as a count by DEC-012 (same day): there are five.** This entry's point is unaffected - average
> price per litre is still a definition difference and still outside the count - but "stand at four" was
> overtaken hours later by the fabricated first-fill interval. The count is five everywhere else.

### Context

The derived-metrics spec predicted this exactly, and instructed: *"it must be reported to the owner rather than
silently resolved in either direction. Do not change the formula to match the sheet without a decision. Record
the outcome as a decision entry."*

The finding landed as predicted - the sheet's figure matched a simple mean to 16 digits (20.734 ÷ 13). The code
shipped volume-weighted. **The entry was never written** - which is precisely the silent resolution the spec
forbade, even though the outcome is the one it recommended. This closes that gap.

### Alternatives Considered

1. **Match the sheet's simple mean**
   - Pros: every Dashboard figure reproduces; no explaining why a number moved.
   - Cons: answers a question nobody asks. A 50 L fill at £1.40 and a 10 L fill at £1.60 cost £1.433/L, not £1.50 - the mean weights a splash equally with a brim.

2. **Expose both**
   - Pros: no information lost.
   - Cons: two numbers labelled "average price" is worse than one right one. Unlike cumulative-vs-per-fill MPG, where a divergence is a real signal, this divergence is just arithmetic.

### Rationale

"What did fuel cost me per litre" is a question about money over volume. The sheet's mean is a fact about its
own price column, not about the fuel. The gap is small - 0.24p/L - which is exactly why it needs recording:
small enough to look like rounding, and it is not.

### Consequences

**Positive:**

- The figure answers the question its label implies.
- The reason a Dashboard number differs is now written down rather than living in a test comment.

**Negative:**

- A fifth figure differs from the sheet, on top of the four defects - and this one is not the sheet's mistake.
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

DEC-011's average-price difference stays **outside** the count - that one is a definition, this one is a
fabrication.

### Context

Fuel Log row 4 carries `miles since last = 334` against a mileage of 77,537, implying a previous reading of
77,203. No such reading exists anywhere in the workbook - the purchase was at 76,632, and row 4 is the first
fill recorded. The interval is invented.

That fabricated 334 miles yields **24.49 mpg**, which the Dashboard then reports as **Worst MPG** (row 13) and
folds into a 13-value **Average MPG** (row 11). So two headline figures rest on a number with no source.

The derived-metrics spec called it *"arguably a sixth defect"* and left it undecided.

### Alternatives Considered

1. **Leave it as an observation, keep saying "four defects"**
   - Pros: the four are verified, quotable and load-bearing across the docs; renumbering costs edits.
   - Cons: this one is the same *kind* of thing - a stored figure with no support in the logs - and it corrupts two Dashboard headlines. Excluding it because the count is already written down is the wrong reason.

2. **Count it and DEC-011 both, making six**
   - Pros: symmetric.
   - Cons: conflates a fabrication with a definition difference. The average-price gap is the sheet answering a different question correctly; this is the sheet answering the right question from invented input.

### Rationale

The test for a defect has been: *does the stored figure disagree with the logs it claims to summarise?* This
one does - more starkly than some of the four, because there is no underlying row at all. Worst MPG is not
merely stale; it is derived from a measurement that never happened.

Our service measures 12 intervals from 13 fills and reports Worst MPG as 25.42.

### Consequences

**Positive:**

- The count matches the evidence, and the fifth is already covered by tests.
- Strengthens the premise: the sheet does not only go stale, it invents.

**Negative:**

- `CLAUDE.md`, `roadmap.md`, `mission.md` and the derived-metrics spec all say "four" and need amending - the four-defect table is quoted widely.
- A reader of older commits will find the four-defect framing and must reconcile it.

## 2026-07-15: Icon Glyphs Become an SVG Sprite

**ID:** DEC-013
**Status:** Accepted, **amended same day - see Amendment below**
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
| `→` | 69 | section links - *"Underlying expenses →"* |
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
   - Cons: the FAB and the grips render differently per OS - the FAB is a primary control on the phone case
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
- Icons gain accessible names - `<Icon>` takes a label or is explicitly `aria-hidden`, where a bare `✓` in a
  `<button>` today is an unlabelled control.
- The scaffold's `public/icons.svg` (Vite's bluesky/discord/github junk) gets replaced rather than shipped.

**Negative:**

- Task 4 grows: ~10 symbols to draw, and 15 glyph sites across 17 screens to replace during the port.
- The port is no longer a verbatim transcription of the design's markup at those sites; the sprite is a
  deliberate divergence and must be checked visually against the concept.


### Amendment (2026-07-15, during task 4 stage 1) - 8 sprite, 7 font-subset

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
original decision rejected as "a second mechanism rather than replacing one" - and that reasoning was sound
about `⠿ ⌂ ⚙ ⇄`, which no face ships, but it does not survive contact with `₂` sitting inside "CO₂".

Implemented: Inter and JetBrains Mono re-subset from the upstream OFL variable TTFs. Both got **smaller**
(101,160 → 97,356 B total) while gaining coverage, because the work also restored axis parity with the shipped
build - Inter's upstream `opsz` axis pinned (CSS applies it automatically, so shipping it would have silently
changed rendering), JetBrains Mono's `wght` clamped from `100–800` back to `400–800`. Verified in Chrome by
whether the *named face supplied the glyph*, not by eye.

**Two gaps remain, and no subsetting closes either:**

- **Oswald has no `₂`.** `tasks.dc.html:184` puts CO₂ in an `<h4>`, which is `var(--disp)`, so that heading
  takes "CO" from Oswald and `₂` from a system face. The other three CO₂ sites are body copy and resolve to
  Inter, which has it. A screens-spec decision for the tasks screen: span the `₂` in the body face, use a real
  `<sub>`, or accept it.
- **`≡` is absent from Inter upstream** (2,849 codepoints, not that one). It only ever appears in `.cfoot`,
  which is `var(--mono)`, so JetBrains Mono covers it. If it ever moves into body copy it will fall back.

The original decision's consequences stand otherwise: icons gained accessible names, and `public/icons.svg` -
Vite starter junk carrying a raw `#aa3bff` and referenced by nothing - is deleted. The claim that the sprite
makes "no system fallback" *literally* true is now accurate for text, with the single Oswald `₂` exception
named above.

### Implementation note (2026-07-15, task 4 stage 6)

Both gaps above are now visible rather than latent, and the sprite is proved in a browser: eight symbols
resolving from an inline `<use>` under the enforced CSP, zero violations, with the FAB's icon rendering in the
bottom nav at ≤900px.

One consequence worth recording, because it outlives this decision: **the icons had to move to `src/`, not
`public/`.** `tokens.test.ts` only walked `src/**`, and `public/` is copied verbatim into the build - which is
exactly how the Vite starter's `icons.svg` sat there carrying a raw `#aa3bff`, referenced by nothing, for the
whole life of the scaffold. In `src/` the guard forces `currentColor`. Hardening that guard to walk `public/`
then caught two more starter artefacts, including `favicon.svg`: **the app's browser tab had been showing
Vite's logo**. It is now a number plate on dossier green, and the single exemption the guard grants - a favicon
is rendered by browser chrome and can reach no CSS variable, so it is exempt because it *cannot* comply.

## 2026-07-20: MCP Server Package Is ModelContextProtocol.AspNetCore, Transport Is Streamable HTTP

**ID:** DEC-014
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Product Owner, Tech Lead
**Amends:** DEC-004

### Decision

The Phase 4 MCP server is built on **`ModelContextProtocol.AspNetCore`** (the official C# MCP SDK), not
"Microsoft Agent Framework". Transport is **Streamable HTTP** (`AddMcpServer().WithHttpTransport()` + `MapMcp`),
which **amends DEC-004's "HTTP/SSE"** - SSE is the protocol's legacy transport, superseded by streamable HTTP.
Hosting stays in-process in `CarTracker.WebApi` and reached through `CarTracker.Gateway` (DEC-004, DEC-009,
unchanged). `tech-stack.md` is amended to match.

This resolves the open question the `CarTracker.ModelContextProtocol` scaffold and the mcp-server spec both
deferred to "task 1". It also settles two scope points the spec left implicit, recorded here because they shape
the build:

- **Scoped bearer tokens are built on ASP.NET Core authentication schemes + authorization policies** (claims
  `mcp:read` / `mcp:write`), so the future multi-user path (Auth0 + JWT) drops in as another scheme with no
  change to the tools. The static `X-Api-Key` (DEC-009) stays the web front-end's; the scoped tokens are the
  assistant's.
- **Write tools are add/log + safe updates only** (no edit or delete of existing rows via the assistant), and
  the read set covers **all screens** (raw per-screen lists in addition to the derived summaries), per the owner.

> **Amended 2026-08-07 - the edit/delete restriction was lifted and shipped.** The catalogue now carries
> twelve `update_*`/`delete_*` tools (fuel, service, mileage, tyre, wash, equipment), so the second clause
> above no longer describes the system. This is recorded as a reversal rather than quietly rewritten, because
> it was a stated owner decision.
>
> The reasoning for lifting it: the web UI gained edit and remove across every log (2026-07-17) with the
> invariants living in the factories, so an assistant edit runs the same guarded path a web edit does - the
> restriction was protecting against a risk the shared application layer had already absorbed. An assistant
> that can log a fill but cannot fix the digit it mistyped forces the owner to the web UI for the correction,
> which is the workflow the assistant exists to avoid.
>
> What did **not** change: MOT expiry is still not settable (it stays derived from the logged pass), vehicle
> lifecycle stays web-only, and every write is still audited against its token. The in-app chat inherits this
> same boundary rather than widening it further.

### Context

`tech-stack.md` named "Microsoft Agent Framework" as the MCP dependency; the ecosystem has since produced
`ModelContextProtocol.AspNetCore`, the official SDK for hosting an MCP server in ASP.NET Core. The two are not
alternatives for the same job: the Agent Framework is for **building an agent that consumes tools** (an LLM
orchestration loop), whereas this spec **hosts an MCP server that exposes tools**. Treating them as an either/or
was a category error carried in the scaffold's comment.

DEC-004 specified "HTTP/SSE" in 2026-07-14; the MCP transport standard has since moved to Streamable HTTP, which
the C# SDK serves via `MapMcp`. The in-process, single-origin, token-plus-TLS shape of DEC-004 is unaffected -
only the transport wording changes.

### Alternatives Considered

1. **Microsoft Agent Framework as the MCP host**
   - Pros: matches the original `tech-stack.md` wording; one framework if an in-app agent later uses it too.
   - Cons: it is not an MCP *server* host - it would mean hand-rolling protocol/transport, exactly what the spec
     ruled "out of proportion". Its real home is the **future in-app chat** as a tool *consumer*, alongside or
     instead of the Anthropic/OpenAI SDKs - a different layer, a later phase.

2. **Keep SSE transport (honour DEC-004 verbatim)**
   - Pros: no amendment.
   - Cons: builds on the deprecated transport; the SDK's first-class path is streamable HTTP. Honouring a stale
     word over the current standard is the wrong kind of consistency.

3. **Hand-roll the MCP protocol over the existing minimal-API stack**
   - Pros: zero new dependency.
   - Cons: reimplements a maintained protocol; the spec already rejected this.

### Rationale

The official SDK gives DI-registered `[McpServerTool]` methods, streamable HTTP through `MapMcp`, and
per-tool `[Authorize]` via `AddAuthorizationFilters()` - the read/write scope gate and the JWT/Auth0 future are
both first-class rather than custom. Keeping the Agent Framework for the eventual in-app chat consumer preserves
the option without conflating the two layers.

### Consequences

**Positive:**

- Task 1 of the mcp-server spec is closed; the scaffold gains its dependency.
- The scope gate and the multi-user auth future ride the standard ASP.NET Core pipeline.
- DEC-004's "HTTP/SSE" is corrected to the live standard before any code depends on it.

**Negative:**

- `tech-stack.md`, the `CarTracker.ModelContextProtocol.csproj` comment, DEC-004's transport line, and the
  mcp-server spec's "task 1 open question" framing all need amending - this decision is the amendment.
- Committing to add/log-only writes and all-screens reads widens the spec's original catalogue; the endpoint
  logic that half those writes need is extracted into a shared application layer as part of the build.

## 2026-08-07: Registration Lookup Calls DVLA/DVSA, and MOT Lands as a Seed

**ID:** DEC-015
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Product Owner, Tech Lead
**Related Spec:** `docs/specs/2026-07-16-dvla-lookup/`

### Decision

The add-car registration lookup calls two external UK-government APIs **server-side only** - DVLA Vehicle
Enquiry Service (VES) for identity/engine/tax, and DVSA MOT History for the current MOT expiry. This is the
first third-party HTTP call the app makes out to anyone.

Three things follow, and this decision fixes all three:

1. **Credentials are server-side configuration and are absent by default.** Bound from `Lookup:*` (user-secrets
   in dev, the host's secret store in prod), never committed to `appsettings.json`, never shipped to the
   browser. A deployment with no key answers `503 NotConfigured` and the add-car form stays fully usable by
   hand. The feature degrades; the app does not fail to start, and CI never makes a live call. Where each
   credential is obtained, and how to set it locally and in containers, is in the **README Quickstart**.
2. **The DVLA MOT expiry lands on `Vehicle.MotExpirySeed`, not as a fabricated MOT `ServiceRecord`.** The spec
   left this open (`tasks.md` 2.2) and it is decided here.
3. **VES tax due date lands on `VedExpiry`, a legitimately stored input**, because nothing in the app logs a
   road-tax payment - unlike MOT, where a logged pass exists and must win.

### Context

`AddVehicleSheet.tsx` has carried a comment since the port explaining that the design's "Look up" button was
deliberately *not* built, because "no such thing exists: DVLA lookup sits unscheduled in the §8 backlog", and
that shipping a button that leaves someone waiting for a fill-in that never comes is worse than no button. This
decision is what lets the button arrive.

The MOT question is the sharp one. MOT expiry is **derived** everywhere in this app - the max `NextDueDate`
over a vehicle's `Type = "MOT"` service records - and a stored copy is the *first of the five defects the whole
project exists to fix*: the workbook showed a red 23-day countdown for a test that had already passed.
`VehicleEndpoints.cs` refuses to make MOT expiry settable for exactly this reason. So a lookup that writes a
DVLA date somewhere is walking straight at that defect, and where it lands matters more than it looks.

### Alternatives Considered

1. **Materialise an initial MOT `ServiceRecord` from the DVLA date**
   - Pros: Reads through the normal derived path from day one, with no fallback field involved.
   - Cons: **It fabricates an event.** A `ServiceRecord` asserts a test happened - it carries a garage, a cost,
     a mileage and a date of work, none of which the DVLA gives us. The service-history screen would show a
     record nobody performed, and the seed would be indistinguishable from a real logged pass, which is the
     opposite of what "a real record supersedes the seed" requires.

2. **A new stored `MotExpiry` column**
   - Pros: Simplest possible mapping.
   - Cons: Rebuilds defect #1 exactly. Rejected without hesitation.

3. **Client-side lookup straight from the browser**
   - Pros: No server code; no key custody on our side.
   - Cons: The key would be public, and the strict CSP forbids a browser→`api.gov.uk` fetch outright - it could
     not work even if the key were publishable.

4. **Require both API keys or disable the feature**
   - Pros: One configuration state to reason about.
   - Cons: VES alone is a useful lookup (make, colour, year, engine, tax); only the MOT seed is missing. All-or-
     nothing for no reason.

### Rationale

`MotExpirySeed` already exists and is already documented as "read only while no MOT record exists yet". It is
the field designed for precisely this input, and using it means the first logged pass supersedes the DVLA date
**by construction** rather than by a rule someone has to remember. Nothing is fabricated: the seed is visibly a
seed, and the service history stays a record of things that actually happened.

Degrading to `NotConfigured` rather than failing follows the same instinct as the whole feature - it is an
accelerator for a form that works by hand, so its absence must cost nothing.

### Consequences

**Positive:**

- The one place a DVLA date is stored is the one place the domain already treats as provisional.
- A fresh checkout, and CI, need no credentials and make no live calls.
- Key custody is a single documented configuration section, not scattered through the code.
- VES and DVSA fail independently: a DVSA outage still pre-fills the identity fields.

**Negative:**

- Two external dependencies with rate limits, outside our control, whose response shapes can change under us.
- The feature is **unverified against the live APIs** until keys are provisioned - the mapping is tested against
  the documented shapes, not against real traffic. First real use may find field-name drift.
- A second credential pair to provision and rotate, on top of the Auth0 and API-key ones.

### Amendment (2026-08-14): an unconfigured deployment shows no button at all

Point 1 above said the form "stays fully usable by hand", and it does - but the **button stayed on screen**,
and a control that answers 503 on every plate is the fault this decision's own Context paragraph quotes: a fast
path that cannot be taken, on the first screen a new account sees. Nobody had seen it, because the only
deployment anyone used had no second account and the sheet is opened once per car.

`GET /api/meta` now carries **`vehicleLookupConfigured`** beside `identityDeletionConfigured`, and the sheet
renders the "Look up" button and the DVLA promise text **only when it is true** - strictly `=== true`, so an
in-flight `meta` hides rather than offers. It is `VehicleLookupOptions.IsConfigured` (the VES key), not
`IsMotConfigured`: alternative 4 above already settled that VES alone is a useful lookup.

The flag is a **capability, not a credential**, which is what makes it safe on the anonymous endpoint - it says
what this deployment can do, and every visitor learns the same thing by clicking the button once. The 503 stays
exactly as it was: the endpoint is still the authority, the flag only decides what is offered.

## 2026-07-24: Auth0 Accounts and Per-Owner Vehicle Ownership

**ID:** DEC-016
**Status:** Accepted (recorded retrospectively 2026-08-07)
**Category:** Technical
**Stakeholders:** Product Owner, Tech Lead

### Decision

The app has real accounts. **Auth0** fronts the web app (SPA client on tenant `usualexpat.uk.auth0.com`, API
audience `cartracker.api`), and the API's **fallback policy requires the Auth0 scheme** - signing in is the way
in. Vehicles are owned: a `User` keyed by the Auth0 `sub`, a nullable `Vehicle.OwnerId`, and the two globally
unique vehicle indexes reworked per-owner so two people can each own a "BT53 AKJ".

Enforcement is **one global EF query filter on `Vehicle`**, not an `ownerId` threaded through ~35 call sites.
Every child entity is reached only through an already-owner-checked vehicle id, so a cross-user vehicle simply
never resolves and the endpoint 404s. A new endpoint **cannot forget to filter**, because there is nothing for
it to forget.

The static `X-Api-Key` stays registered but **grants no vehicle access** - it fronts only the anonymous meta
and docs endpoints. MCP `AssistantToken` bearers remain a third, separate mechanism carrying their owner.

### Context

This was written up on 2026-08-07, two weeks after it shipped, because **it never had a decision record at
all** - the single largest architectural change in the project, and the decision log went straight from
DEC-015 (a registration lookup) past it. Two later DECs were still describing Auth0 as "the future multi-user
path" while it was already the only way to sign in. The absence is the reason this entry exists; the substance
below is what was decided at the time.

The app began single-user: one shared `X-Api-Key` in localStorage, every vehicle unowned, the garage listing
every car in the database. DEC-009 anticipated the revisit condition precisely - "revisit if this ever leaves
the LAN or gains a second user" - and both came true at once.

### Alternatives Considered

1. **Thread an `ownerId` parameter through every query.** Rejected: ~35 call sites, and the failure mode is
   silent. A missed one leaks another user's data and looks exactly like working code.
2. **Row-level security in Postgres.** Genuinely enforced, but it puts the rule somewhere EF cannot see and
   makes local debugging and tests substantially harder for a two-user app.
3. **Self-hosted identity (a local users table with password hashing).** Rejected: password reset, lockout,
   and rotation are a product of their own, and getting them wrong is worse than not building them.

### Consequences

**Positive:**

- A new endpoint is owner-scoped by default; forgetting to filter is not an available mistake.
- Refresh-token rotation means no silent-auth iframe, so the strict CSP needed only `connect-src` widened -
  no `frame-src` at all.
- The bearer is injected at the single `client.ts` fetch seam, so no call site knows about tokens.

**Negative:**

- An external identity provider is now a hard runtime dependency: no Auth0, no login. A tenant outage is a
  total outage, which a static key never was.
- **Reference tables are still global.** `Garage` and `WashLocation` are shared across users - the chosen fix
  (surrogate id + `OwnerId`, repoint four FK columns, backfill) is its own migration and has not been done.
- The first user to sign in claims all pre-existing unowned vehicles. Correct for this deployment's migration
  from single-user, and a trap on any deployment where that is not the intent.

> **Amended 2026-08-13 by DEC-018 - two clauses are retired, and the core stands.** Recorded in place so the
> two negatives above are not read as current.
>
> **The reference-table negative is closed, and not in the shape it records.** `Garage`, `WashLocation` and
> `ExpenseCategory` - which that bullet never named, and which has the identical defect across two more tables
> - are now keyed `(OwnerId, Name)` with their six foreign keys dropped, **not** the "surrogate id + `OwnerId`,
> repoint four FK columns" this entry describes. DEC-018 carries the argument. It also understates the fault:
> the tables were not merely shared, they were **writable across accounts**. One owner renaming their garage
> issued an `UPDATE` over another owner's service records and workshop tasks, and blanked that owner's
> `vehicles.default_garage` to NULL.
>
> **First-user-claims-all-unowned-vehicles is retired.** The `Users.CountAsync() == 1` adoption block becomes
> an explicit `Ownership:ClaimUnownedVehiclesFor` external id: adoption happens only when the provisioning
> `sub` matches it exactly, and the default is null - no adoption, ever. The clause was right for the
> single-user migration it was written for and is a trap the moment a stranger can be first through the door.
> This deployment is unaffected; BT53 was claimed in July 2026 and no unowned vehicle remains.
>
> **What stands is the substance:** Auth0 as the identity provider, the fallback policy requiring the Auth0
> scheme, the single global query filter as *the* enforcement mechanism - DEC-018 **extends** it to three more
> entities rather than introducing a second style - the static key granting no vehicle access, and MCP
> `AssistantToken` bearers as a third, separate mechanism.
>
> **And one addition, which belongs here because it decides who gets an account at all:** sign-up is behind an
> allowlist (`Signup:AllowedEmails` / `Signup:AllowedDomains`) **over addresses the tenant has verified**,
> checked before an unseen `sub` is provisioned. The verification half is not a refinement of the list, it is
> half of what makes it a list: on a database connection a stranger self-registers with any address they can
> type, so `AllowedDomains=example.com` alone admits whoever writes `anything@example.com`, and the deployment
> reads as invitation-only while being open to the internet. `email_verified` comes back in the same Management
> API answer as the address, so it costs no extra call - and a connection that never verifies admits nobody,
> which is the direction every other unknown here fails in.
> As built the check is **not** in `CurrentUserMiddleware` but in `AccountProvisioner`, a domain service the
> middleware calls: the code deciding whether a stranger gets an account cannot be tested where it sat, because
> there is no `CarTracker.WebApi.Tests` project. Moved, "a refused address creates no `User` row" is a plain
> Data test against a real database - which is the assertion worth making, the 403 being only how the refusal
> is reported. **An empty allowlist means closed** - the fail-safe direction, and the opposite
> of the natural reading, which is why it is stated in `.env.example`, the README Quickstart and the API spec.
> A refused person leaves no `User` row, no half-state for the ownership filter to reason about, and nothing
> to clean up - **but they do leave an Auth0 identity in the tenant.** That is the standard shape of this
> pattern rather than a leak, and it means the tenant accumulates logins that were never admitted. Disabling
> public sign-up in the Auth0 dashboard is the belt to this braces; it is a dashboard action, not code, so
> nothing in this repository can assert it has been done.
>
> **The allowlist needs an address, and this API's access token does not carry one.** `CurrentUserMiddleware`
> has documented that since July 2026 and fell back to `?? sub`, so every `User.Email` in the database holds
> an `auth0|…` string rather than an email - which no allowlist can match and no deletion confirmation can
> ask you to type. So the server asks: at provisioning it calls the Auth0 **Management API**
> (`GET /api/v2/users/{sub}`) for the real address, reusing the same M2M credential the identity-erasure half
> needs, and **backfills `User.Email`** the first time one resolves. The rows needing repair are identifiable
> with certainty - the old fallback stored the subject itself, so `Email == ExternalId` is an equality no real
> address can satisfy, and once repaired the condition is false forever. Adding a custom-claim action in the
> tenant would have avoided the round trip, but it is dashboard configuration this repository cannot assert
> either, and the credential was needed regardless. **The consequence is one credential gating two things:**
> with `Auth0:Management:` unset there is no address to check, so **sign-up is closed and account deletion
> refuses** - both stated in `.env.example` and the README rather than discovered.
>
> **A refused subject is remembered for a minute, and it is that lookup which forces the cache.** A refusal
> deliberately writes no row, so nothing remembers it and the next request repeats the whole thing; the access
> probe behind the "not yet invited" panel inherits `refetchOnWindowFocus`, so an uninvited person leaving the
> tab open re-asks the tenant every time they return to it. The Management API is rate-limited, and a throttled
> tenant answers nothing - which the door correctly reads as "address unknown" and correctly refuses, so **the
> person shut out is not the one generating the traffic but whichever invited newcomer signs in during it.**
> `SignupRefusalCache` caches refusals only and never an admission, so its failure direction is a stranger
> waiting a minute; the client-credentials token is cached for its own lifetime beside it, halving what remains.

## 2026-08-07: The In-App Assistant Absorbs Receipt Capture

**ID:** DEC-017
**Status:** Accepted
**Category:** Product
**Stakeholders:** Product Owner

### Decision

There will be **one path from a file to a record, and it is the assistant.** The in-app chat
(`docs/specs/2026-08-06-in-app-chat-assistant/`) takes uploaded photos and PDFs, identifies what each one is,
reads its figures, and proposes a filled-in write the owner confirms. `docs/specs/2026-07-16-receipt-photo-capture/`
is **deleted**, not deferred.

Three sub-decisions:

1. **Classification is expected behaviour, not an optional extra.** A bare attachment with no instruction is a
   supported input. The model states what it thinks each file is before drafting, declines to draft what it
   cannot place, and asks rather than guesses.
2. **Uploaded files are never stored.** They reach the model and are discarded - no `Document` row, no volume
   write. Filing evidence stays the Documents feature's job.
3. **The package is the official Anthropic SDK**, which retires the "Microsoft Agent Framework" name that
   `tech-stack.md` carried from before that SDK existed (DEC-014 already retired it for the server half).

### Context

The receipt spec's v1 had the owner reading the photo on screen and typing date, amount and vendor by hand,
with the image saved as evidence. It called extraction a deliberate v2 and named two routes: a server-side OCR
service, or *"the MCP assistant reading the attached photo - the assistant already can log expenses; reading a
receipt it can see is a natural extension."* The chat spec, written six weeks later, **is** that second route.
Keeping both would have meant two upload paths and two answers to "how do I file a receipt".

The behaviour being adopted is not speculative: Claude Desktop already does it against this project's MCP
server. You attach a document, say little or nothing, and it works out what it is looking at and calls the
right tool. The in-app assistant should not be worse at that than an external client hitting the same tools.

### Alternatives Considered

1. **Build receipt capture v1 as specced, then the assistant.** Rejected: it ships a manual transcription flow
   that the assistant makes obsolete within one increment, and leaves a camera input on the expense sheet that
   nothing else uses.
2. **Retire the receipt spec in place with a superseded marker.** Rejected in favour of deletion, with its two
   load-bearing rules moved into the assistant spec first so the reasoning survives the file.
3. **Let the assistant also file the photo as a `Document`.** Rejected for now - see the consequence below. It
   remains reachable: the write request already carries a nullable `documentId` that nothing sets.

### Consequences

**Positive:**

- One upload path, one place where extraction lives, one boundary to reason about.
- The receipt spec's governing rule survives and is *better* served: "a wrong auto-filled amount silently
  entered is worse than a field the owner typed" is now enforced by the confirm step rather than by refusing
  to extract at all.
- Fuel stays mirror-only across both surfaces, so no photograph can reopen the £163.16 gap.

**Negative:**

- **A capability is lost, not relocated.** No single action both logs an expense from a receipt and keeps the
  receipt. Filing a document against an expense remains a separate trip to the Documents screen. This is a
  deliberate scope reduction and is stated as one in the spec.
- The only remaining work that needs a credential. With no `Anthropic:ApiKey` there is now **no** file-to-record
  path at all, where previously a keyless manual one was specced.
- A tenth project (`CarTracker.Chat`) and a new external API dependency in the request path of a write.

## 2026-08-13: Reference Lists Are Keyed `(OwnerId, Name)`, and the Six Foreign Keys Go

**ID:** DEC-018
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Product Owner, Tech Lead
**Related Spec:** `docs/specs/2026-08-11-pre-public-release-gates/`
**Amends:** DEC-016

### Decision

`Garage`, `WashLocation` and `ExpenseCategory` gain an `OwnerId` and are keyed **`(OwnerId, Name)`**, with
three query filters beside the `Vehicle` one so every existing read becomes owner-scoped with no call-site
change. **The six foreign-key constraints pointing at those tables are dropped** - and the six columns that
carried them do not change at all:

| Table | Column | Behaviour dropped |
|---|---|---|
| `service_records` | `garage` | `SetNull` |
| `maintenance_tasks` | `assigned_garage` | `SetNull` |
| `vehicles` | `default_garage` | `SetNull` |
| `wash_entries` | `location` | `SetNull` |
| `expense_entries` | `category` | `Restrict` |
| `budget_group_categories` | `category` | `Cascade` |

They stay `varchar`, carrying the same names. That is the entire reason for choosing this shape.

This **reverses the roadmap's recorded shape for gate 1**, which read "surrogate id + `OwnerId`, repoint the
four FK columns, backfill". Four was itself an undercount: `ExpenseCategory` was missing from the gate
altogether, and it is two more columns and two more constraints.

> Cited without a line number on purpose. The roadmap bullet was rewritten to its closed state in the same
> release, so the quoted phrase is no longer anywhere in that file - a line reference here would send a reader
> to text that reads as though it never said this. The quotation is the record; `roadmap.md`'s
> *"Before sign-up can be opened to the public"* section is where the bullet now lives.

Two things follow that are decisions in their own right:

- **The 13 seeded categories stop being seed data.** `ExpenseCategoryConfiguration.HasData(SystemCategories)`
  goes, because a seeded row has no owner and there is no owner to invent for one. The array stays and becomes
  the source `CurrentUserMiddleware` provisions from, per account. `IsSystem` and the `Fuel`/`Purchase`
  rename-lock are unchanged - they now hold within each owner's own set, and the mirrors resolve by the same
  exact constant they always did.
- **Reference rows cascade from `users`**, where `Vehicle.OwnerId` and `AssistantToken.OwnerId` are `Restrict`.
  A vehicle is data whose destruction should be an explicit act; a list entry cannot outlive its list.
  `AccountDeletionService` still deletes them explicitly, because relying on a cascade to do something you
  intended is how the document bytes came to be forgotten.

### Context

The roadmap called this "one user can rename or re-home another's data", which reads as untidiness. It is a
**cross-tenant write**, and it is armed by the second account, not the hundredth.

Phase 4.5's isolation is one query filter on `Vehicle` (DEC-016), and it holds because every other entity is
reached through a vehicle id that was resolved through that filter. `ReferenceListEditor`'s statements do not
go through a vehicle. **They match on a name.** So with a single `garages` row called "K & P Motors" - single
because `Name` is the primary key, so the second owner to type it silently adopts the first one's row, address
and contact included - owner A renaming it runs an `UPDATE` across owner B's service records and workshop
tasks.

The failing test written before any fix (`ReferenceListCrossTenantTests`) states the correct behaviour and
reports all three of B's references as one value, because they do not fail the same way:

```
Assert.Equal() Failure: Values differ
Expected: ["service_records.garage=K & P Motors",
           "maintenance_tasks.assigned_garage=K & P Motors",
           "vehicles.default_garage=K & P Motors"]
Actual:   ["service_records.garage=K&P Motors",
           "maintenance_tasks.assigned_garage=K&P Motors",
           "vehicles.default_garage=<null>"]
```

Two rows rewritten into a name B never chose, and one **blanked**. The blanking is the instructive one:
`context.Vehicles` *is* filtered, so B's default garage was correctly left out of the repointing - and then
the old `garages` row was dropped and the `SetNull` foreign key erased B's field anyway. **Partial scoping was
worse than none.** Scoping the editor's statements without changing the key would have produced exactly that
third line on all four garage/wash columns, which is why the composite key and the FK drops are a prerequisite
of the cascade fix rather than a tidy-up after it.

`ExpenseCategory` is named in no gate and has the same shape twice over, and
`CountCategoryReferencesAsync` already reports every account's usage as your own through
`GET /api/reference/expense-categories`.

### Alternatives Considered

1. **Surrogate id + `OwnerId`, repoint the six columns (the recorded shape)**
   - Pros: keeps a real foreign key; the conventional relational answer.
   - Cons: nobody had counted what those columns feed. `ServiceRecord.Garage` and `WashEntry.Location` render
     directly in `<DataTable>` columns and sit in `useTableView`'s `search.fields`, where free-text search
     matches them as strings; `Vehicle.DefaultGarage` is in `VehicleSummary`; and the MCP tools `add_service`,
     `log_wash` and `update_vehicle_profile` take a garage or location **by name**. Every one needs a join to
     render a name from an id, the search fields change shape, the MCP arguments change meaning, and the
     contract diff stops being additive - for a guarantee the application layer already overrides.

2. **Composite key *and* keep a real FK on `(OwnerId, Name)`**
   - Pros: isolation and referential integrity together.
   - Cons: the FK needs something to point at, so `owner_id` has to be denormalised onto all six child tables.
     That puts the owner in two places per row and invents a fresh class of inconsistency - an owner column on
     an expense entry that can disagree with the owner of its vehicle.

3. **Leave the tables global and scope only the editor's statements**
   - Pros: no migration; smallest diff.
   - Cons: the red test above says what happens - B's default garage goes to NULL. Two owners still cannot
     both have "K & P Motors", so the adoption-by-collision bug survives untouched.

4. **Postgres row-level security on the three tables**
   - Pros: enforced below the application.
   - Cons: rejected for the same reason DEC-016 rejected it for vehicles - the rule lands somewhere EF cannot
     see, and this work exists to *extend* one mechanism, not to add a second.

### Rationale

Read what the six constraints actually do, and every surviving behaviour is application code:

- **`SetNull` on the four garage/wash FKs is a hazard, not a safeguard.** CLAUDE.md records it plainly: a
  delete "would *silently blank* referencing rows unless guarded", which is exactly why `ReferenceListEditor`
  was written to block-with-a-count or re-home instead. The constraint's runtime behaviour is the outcome the
  editor exists to prevent - and the test output above is that behaviour firing.
- **`Restrict` on `expense_entries.category` duplicates a check the editor already performs**, and once the
  cascade is correctly scoped it actively obstructs: `UpdateCategoryAsync` ends in an `ExecuteDelete` of the
  old row, which throws unless the constraint is already gone.
- **`Cascade` on `budget_group_categories.category` is the sharpest of the six** - it silently deletes budget
  memberships when a category goes. The editor re-homes them explicitly, so the cascade only ever fires on a
  path the editor does not take.
- **The rename cascade never used a foreign key at all.** There is no `ON UPDATE CASCADE` anywhere; the editor
  hand-writes insert-new → repoint → drop-old in a transaction, because changing a primary key cannot be an
  in-place update.

So `(OwnerId, Name)` with no constraint costs one dropped guarantee that was never load-bearing, and changes
no DTO, no search field, no MCP argument and no rendered column.

### One more reversal, recorded here because there is nowhere else

**The export adds a raw fuel read.** `FuelEndpoints.cs:43-44` records a deliberate decision that no raw
`FuelEntries` query exists - everything reads through `IDerivedMetricsService`, because "a raw `FuelEntries`
query would hand back rows with no MPG at all and invite the screen to work it out again, which is how two
places start disagreeing." That reasoning holds for every *screen* and is why the rule stays. The export is the
one caller for which it inverts: an export must carry **only** stored rows, because a derived figure written
into an archive is the workbook's five defects reproduced in the one artefact whose purpose is to be read later
when nothing can recompute it. Three other tables needed the same treatment for the same reason - check logs,
budget groups with their memberships, and the issue-watch links, none of which had a list method either.

### Consequences

**Positive:**

- Two accounts can each hold a garage, wash location and expense category of the same name, and neither can
  observe, count, rename or re-home the other's.
- The isolation mechanism stays singular. A future reference table gets a filter and inherits the property;
  there is no second style to choose between.
- The migration touches **no log table**. Children reference by name, and the constraint that would have
  objected is gone by then, so no service record, wash entry or expense row is rewritten.
- The reference-count on every list becomes truthful - it was aggregating strangers' rows.

**Negative:**

- **Referential integrity on those six columns is now entirely the application's job.** `ReferenceWriter`'s
  create-as-used is the single door and must stay so; a write path that sets a garage name without going
  through it can store a name no row backs, and nothing below will object.
- **Two write paths bypass that door by design, and the FK was what backstopped them.** `FuelEntryFactory`
  and `VehiclePurchaseMirror` write the constants `"Fuel"` and `"Purchase"` straight onto
  `ExpenseEntry.Category`, resolving by exact name as they always have. The `Restrict` constraint used to
  guarantee a matching row existed; it is gone, so the guarantee now rests on exactly two things and no
  database check - **provisioning** puts both rows in every account, and the **rename lock**
  (`MirrorRenameLocked`) keeps their names. Both are asserted by `ReferenceListCrossTenantTests` rather than
  assumed, because that is all there is now.
- **The migration deletes rows and is one-way.** EF's generated `Up()` is thrown away for hand-ordered SQL
  (drop the 6 FKs → drop the 3 single-column PKs → add `owner_id` → copy per user → delete the ownerless
  originals → `SET NOT NULL` → add the composite PKs), and `Down()` throws `NotSupportedException`. It also
  **asserts `users` count <= 1 and aborts otherwise**, because a per-user copy of a shared row is only
  unambiguous while there is one user - which is true of this deployment and of every fresh checkout, and is
  enforced rather than trusted.
- The 13 categories become 13 rows **per account** instead of a migration artefact, so a user cannot exist
  without them and provisioning is now two saves rather than one (`user.Id` is store-generated and the owner
  FK is navigation-less).
- Anything that reads `context.Garages` in a bypass context - tests, background work - now sees every
  account's rows, so any bulk operation there must distinguish "no user in scope" from "deliberately
  unscoped". A `BypassOwnership` context makes an isolation test a false green, which is why the tests take an
  explicit owner.

### As built: two details that were decided at the keyboard

**The guard refuses inserts and lets everything else through, and the bypass hazard is closed a different
way.** `ReferenceOwner.Require` throws when there is no account, with two separate sentences for the two
separate bugs - *no request context* (a background job, a design-time tool, a directly constructed test
context) means the caller is wrong; *a request that resolved no account* (an API-key or anonymous principal)
means the pipeline is wrong. But it guards the **four create inserts only**. Reads and edits still run under a
bypass context, because the alternative was to make every existing Data test unrunnable in order to prevent a
hazard those tests do not exhibit. The hazard itself - `Garages.Where(g => g.Name == name).ExecuteDeleteAsync`
deleting *every* account's row of that name, since `BypassOwnership` arrives as a runtime parameter and the
filter contributes nothing - is closed instead by naming **the whole primary key** on all six reference-table
deletes, off the row already loaded. That is also simply what the statement means: delete *this* row. The
three *rename* inserts take their owner from the row being renamed rather than from the accessor, because a
rename changes one key component and not both.

**The correlated subquery held; no fallback was needed.** Every scoped child statement is
`context.<Children>.Where(x => x.<Name> == name && context.Vehicles.Any(v => v.Id == x.VehicleId))`, which
inherits the `Vehicle` filter inside the generated SQL - so the fifteen `ExecuteUpdate`/`ExecuteDelete`/
`Count` statements are owner-scoped without materialising a list of vehicle ids to `Contains`. Fifteen, not
the eleven planned: the five reference **counts** needed it too, and a count that aggregates strangers' rows
is the quiet half of the same leak.

### The account half, recorded here because it shipped in the same release

Three decisions from the export and deletion work that have nowhere else to live:

- **The export carries no derived figure - by rule, not by omission.** It is stored rows only, which cost two
  extra read methods (`ListIssuesAsync`, `DocumentService.ListRowsAsync`) because the screen wrappers carry
  live check status and rendered link labels. A derived value written into an archive is the workbook's five
  defects reproduced in the one artefact whose purpose is to be read later, when nothing can recompute it.
  The file says so in its own `notes`, so a reader who finds no MPG column knows it was withheld deliberately.
  The endpoint declares **no response schema**: the payload is streamed a vehicle at a time and has no static
  shape, and a declared one would be a hand-maintained second definition free to drift from the writer.
- **`Auth0:Management:` gates deletion as well as sign-up, and unset is a refusal rather than a
  degradation.** `DELETE /api/account` answers **503 and deletes nothing**, checked before the transaction
  opens - following the `Lookup:` precedent exactly (503 NotConfigured, distinct from a 502 that would invite
  a retry that cannot succeed). Deleting the rows and leaving the login would be a half-erasure that satisfies
  no article and looks complete. When the credential *is* configured and the call still fails, the rows are
  already gone and a `pending_identity_deletions` row queues an hourly retry; that retry **stops rather than
  marks** when the provider is unconfigured, because fifty rows all reading "not configured" would bury the
  one real error naming the missing grant.
- **The invitation refusal is the only RFC 9457 `type` this app reads.** A not-invited 403 is indistinguishable
  from any other 403 at the client's fetch seam, so `ApiError` carries the problem `type` and `queries.ts`
  exports one constant and one predicate (`NOT_INVITED` / `isNotInvited`) as the single place that reads it.
  `AuthGate`'s access probe **fails open**: a 500 or a dropped
  connection renders the app, and only the invitation refusal stops it. A gate that locked people out whenever
  it could not reach the server would turn a transient outage into a lockout, which is a worse failure than
  the one it guards against.

## 2026-08-14: The Chat Consumes One Tool Catalogue In-Process, Behind `IChatClient`

**ID:** DEC-019
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Product Owner, Tech Lead
**Related Spec:** `docs/specs/2026-08-06-in-app-chat-assistant/`

### Decision

Four things, settled together because each one only makes sense given the others.

1. **One tool catalogue, as a type rather than as a discipline.** The `[McpServerTool]` methods become a
   single `AIFunction[]` that both `/mcp` and the in-app chat consume - `McpServerTool.Create(AIFunction, …)`
   exists precisely to wrap one. The chat defines no tools, holds no domain logic, and gets no capability the
   MCP surface does not have.
2. **In-process invocation, not loopback MCP.** The chat does **not** speak MCP over HTTP to our own `/mcp`.
3. **`Microsoft.Extensions.AI.IChatClient` is the provider seam**, with the official Anthropic SDK behind it
   (`new AnthropicClient().AsIChatClient(…)`). Anthropic direct is the first provider; Claude in Microsoft
   Foundry and anything else are a one-line client swap.
4. **`FunctionInvokingChatClient`'s approval protocol is the suspend-on-write loop** - write tools registered
   as `ApprovalRequiredAIFunction`, suspension as `ToolApprovalRequestContent`, resumption as
   `ToolApprovalResponseContent` - rather than the hand-rolled loop the spec originally described.

### Context

The spec already said "same tools, same DI container, no second catalogue". What it described to achieve that
was hand-rolled reflection over the `[McpServerTool]` attributes - which is a *second derivation* of the same
truth, free to drift, and drift in this particular list is what makes the confirm gate skippable. The question
that prompted this decision was sharper than the spec's answer: should the chat simply *be* an MCP client
against the server the app already hosts?

### Alternatives Considered

1. **Loopback MCP - the chat is an MCP client against `https://localhost/mcp`.**
   - Pros: the strongest possible reading of "one surface". No reflection, no parallel schema, and the
     server-side filters (`McpDatabaseFaultFilter`, `McpAuditFilter`, `AddAuthorizationFilters()`) apply for
     free because the call goes through the real pipeline.
   - Cons: **ownership, and it is disqualifying.** `/mcp` is gated by `RequireAuthorization("McpRead")`, and
     per-user isolation rests on `CurrentUserMiddleware` pinning `ICurrentUserAccessor` from the *request's*
     principal. A loopback call carries whatever credential the chat presents, so it would mean minting an
     `AssistantToken` per signed-in user per request - a bearer credential in the web path, where a minting
     bug is a cross-tenant leak. In-process invocation inherits the correct owner for free, because the tool
     runs inside the user's own authenticated request. Lesser costs: a serialisation round trip per tool call,
     and Streamable HTTP session plumbing to talk to ourselves.

2. **Hand-rolled reflection over the attributes** (what the spec originally described).
   - Pros: no new package, and the tool types stay untouched.
   - Cons: two derivations of one schema, and nothing fails when they diverge. `AIFunction` is already the
     currency both SDKs speak; deriving it a second time by hand is work done to be less correct.

3. **A hand-rolled conversation loop**, on the grounds that the SDK's runner gates tools synchronously while
   this gate spans an HTTP round trip and a human.
   - That is true of the Anthropic SDK's `BetaToolRunner` and **false** of `Microsoft.Extensions.AI`, whose
     approval protocol is exactly a gate that suspends, returns, and resumes from a later request. Rejected on
     the same grounds DEC-014 used to reject hand-rolling MCP: the maintained protocol wins.

4. **Claude in Microsoft Foundry as the first provider** (same rates, billed as Claude Consumption Units
   through the Azure Marketplace, so one invoice with the VM and drawable against an Azure commitment).
   - Deferred, not rejected. Direct is the surface everything is documented against, and Foundry currently
     lacks the Batches API, the Models API, mid-conversation system messages and task budgets. None of those
     bite this feature, which is why it stays a config switch rather than a plan.

5. **A local or self-hosted model.** Rejected on measurement, and the numbers are in the spec's Out of Scope:
   the Azure VM is 2 vCPU / 4 GiB with no GPU (~0.5–2 tok/s for a 4B model), the NAS is slower, an Azure T4 is
   ~£290/month against a chat bill in single-digit pounds, and vision + tool-calling in one turn is the
   combination open runtimes still make you choose between. `IChatClient` is what keeps this cheap to revisit.

### Rationale

The three seams line up: `AIFunction` is what the MCP SDK wraps, what `IChatClient` consumes, and what the
approval protocol marks. Choosing them is choosing to have one object per tool in the process rather than three
descriptions of it - and the ownership filter, the audit trail and the confirm gate all become properties of
that one object rather than rules three call sites must remember.

Choosing Anthropic first costs nothing in portability *because* of the seam, which is what makes it a real
decision rather than a deferral: `AsIChatClient()` means the provider question can be answered later with
evidence instead of now with a guess.

### Consequences

**Positive:**

- A tool added to `/mcp` appears in the chat with no edit, and a drift test fails the build if that ever stops
  being true.
- Swapping model, or provider, is one registration. The measurement that picks between `claude-sonnet-5` and
  `claude-opus-5` on real BT53 paperwork can therefore happen after the code is written, not before.
- The suspend-on-write loop is ~80 lines of configuration instead of ~80 lines of protocol handling, and the
  transcript-integrity rules it would have had to enforce by hand are enforced by the library.

**Negative:**

- **The filters do not come along for free.** `McpDatabaseFaultFilter` and `McpAuditFilter` are wired onto the
  MCP *server* pipeline, so an in-process invocation skips both unless they are lifted into a shared decorator.
  This is the "second route into the domain" the decision exists to prevent, and it hides in the filters rather
  than in the tools - which is why it is a named task rather than a note.
- **Anthropic-specific behaviour has to reach through the abstraction.** Prompt caching breakpoints,
  `fallbacks`, task budgets and the thinking-block round trip are provider shapes, reachable only via
  `ChatOptions.RawRepresentationFactory`. They are confined to one `AnthropicChatExtras` class, and two spikes
  gate the design: if caching or thinking round-tripping cannot survive `IChatClient`, the seam moves up to
  `IChatConversationService` and the SDK is used directly beneath it.
- ~~**`AllowMultipleToolCalls` must be off**, because if any call in a response requires approval then *all* of
  them do - including reads. That costs a round trip per tool and is the price of the read-now/confirm-to-write
  distinction the whole feature rests on.~~ **Reversed 2026-08-14, and the flag was never doing the job it was
  credited with.** The Anthropic seam emits a `tool_choice` only when `ChatOptions.ToolMode` is non-null; that
  property defaults to null and was never set, so `disable_parallel_tool_use` has never been sent and parallel
  tool use has been on throughout. The abstraction warns as much - "the underlying provider is not guaranteed
  to support or honor this flag". A pasted table of sixteen fills therefore arrived as sixteen tool calls in
  one response, of which the loop answered one and dropped fifteen, and the next request was rejected
  outright. The design now **answers every suspension** - which both spec documents already required - and
  keeps the read-now distinction by dropping the requests the loop marks `RequiresConfirmation = false`, which
  is what a read swept in alongside a write arrives as. `ToolMode = Auto` and `AllowMultipleToolCalls = true`
  are now set explicitly; that changes nothing on the wire and stops the request asserting something untrue.
- One more package pair in the graph (`Microsoft.Extensions.AI` and its abstractions), though both were already
  there transitively beneath the MCP SDK.
- **The configuration key is now `Chat:`, not `Anthropic:`** - `Chat:ApiKey`, `Chat:Model`, `Chat:Effort`,
  `Chat:DailyTokenBudget`. A provider-named key under a provider-agnostic seam would be wrong the day the seam
  is used, and it groups the chat's settings the way `Lookup:`, `Signup:` and `Documents:` already group
  theirs. DEC-017's prose still says `Anthropic:ApiKey`; it is a record of what was decided then and is left
  as written.

## 2026-08-18: The Host Leaves This Repository

**ID:** DEC-020
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Product Owner, Tech Lead
**Related Spec:** `docs/specs/2026-08-11-cambelt-azure-deployment/`
**Amends:** the Azure half of `2026-08-11-cambelt-azure-deployment`

### Decision

**The VM, its Bicep, its reverse proxy, its PostgreSQL server and its off-site backup pull are not this
project's concern and move to a separate hosting repository.** Cambelt becomes one tenant of a shared host
that will run several unrelated side projects.

What this repository keeps is the boundary and nothing beyond it:

1. **A compose file that is a good tenant.** The app's own three services, joining two **external** networks
   (`edge` for the proxy, `data-cambelt` for its database), publishing **no ports**, and expecting a database
   that already exists.
2. **`postgres`, `caddy`, `watchtower` and `db-backup` behind a `standalone` compose profile**, so
   `docker compose --profile standalone up` still produces today's self-contained stack. The Synology
   deployment and a fresh checkout are unaffected, and `docs/deployment-synology.md` documents a real install
   that keeps working.
3. **The app-side facts that a host cannot know**: that a dump without `${DATA_ROOT}/documents` restores
   `Document` rows pointing at nothing, that `/mcp` is a long-lived streaming response that a proxy must not
   buffer, and that the Auth0 origin has to be registered before a new address can sign anyone in.

The naming scheme, the priced Azure comparison, the NSG rules and the cloud-init sketch stay in
`sub-specs/` as **handover material for the hosting repository**, marked as such, rather than being deleted.
They are research that was paid for once.

### Context

The spec was written for a VM whose only job was Cambelt. That premise changed: the same box will host several
dockerised projects, so the proxy, the database server and the backup schedule are **host** concerns with more
than one consumer, and a per-project copy of each is how you get four Watchtowers, four certificate stores and
four backup jobs that each look fine alone.

The decisive argument is one this project has already paid for. CLAUDE.md records the NAS running a *copy* of
`deploy/docker-compose.yml` that nothing keeps current, a Container Manager project holding a third copy, and
Watchtower recreating containers from the running container's spec rather than from either - which is how
0.13.1 reached production with `Auth0__Management__*` empty and refused an invited, verified address while
nothing looked wrong. **Infrastructure defined in the repository of one of its tenants is that same shape**:
the file that defines the host lives next to only one of the things the host runs, and drifts from the other
three silently.

Keeping HTTPS's *mechanism* here also made a gate this repository cannot close look like one it could. The
roadmap's HTTPS line has always ended "no code change, which is why nothing in this repository will tell you
it has not been done"; that is now literally true of the whole deployment, and the honest place for it is a
repository whose subject is the host.

### Alternatives Considered

1. **Keep the Bicep here and let the other projects reference it.**
   - Pros: one place, already written; no new repository to set up.
   - Cons: every other project depends on a car-maintenance repository to deploy, and a change made for
     another project's sake lands in this one's history and CI. The dependency points the wrong way.
2. **A dedicated VM per project.**
   - Pros: total isolation; each repository owns its host honestly; a bad deploy cannot touch a neighbour.
   - Cons: the bill multiplies by the number of side projects, each box needs its own certificates, updates
     and monitoring, and most of them are idle most of the time. £33/month once is a hobby; four times is a
     subscription nobody reviews.
3. **A PaaS (Coolify, Dokploy) owning deployment for everything.**
   - Pros: solves proxy, TLS, per-app environment and database provisioning in one product, with a UI.
   - Cons: it wants to own the deployment path this project already has working - CI publishes `:latest` and
     `:<version>` only when `VERSION` changes, and Watchtower recreates from that. Replacing a pipeline that
     works, and whose one sharp edge is documented, with a different one for the sake of a nicer UI is a bad
     trade. Revisit if the number of projects makes hand-written compose files the bottleneck.
4. **One PostgreSQL instance per project on the shared host.**
   - Pros: complete blast-radius isolation, independent major-version upgrades.
   - Cons: each instance costs its own `shared_buffers` and background workers - roughly 400-600 MB of RAM
     across four idle projects on a 4 GiB box - and multiplies backup jobs, upgrades and monitoring targets by
     the number of projects. **One server, one database and one role per project** is the chosen shape;
     per-project `data-<project>` networks mean an app cannot open a socket to a neighbour's database at all,
     which is isolation the shared instance would otherwise have given away.

### Rationale

The property worth protecting is the one that has already failed once here: **one definition of what runs
where**. A tenant repository can honestly say "I need `edge`, `data-cambelt`, and a database called `cambelt`",
and that statement stays true whatever the host is. It cannot honestly define the host, because it is not the
only thing on it.

The `standalone` profile is what keeps this from being a one-way door. The self-contained stack that runs on
the NAS today is one flag away, so a reader can still bring the whole thing up on a laptop or a spare box
without a shared proxy or a shared database existing anywhere.

### Consequences

**Positive**

- This repository stops carrying infrastructure it cannot test. Nothing in `deploy/` needs an Azure
  subscription, a DNS record or a certificate to be exercised.
- The host's concerns get one owner: one proxy with one certificate store, one Postgres, one Watchtower, one
  backup schedule covering every project's dumps and document volumes, and one place where a new project is
  added.
- Per-project `data-<project>` networks give better isolation than the single-VM design had, where every
  container could reach the database.
- The spec's Azure research survives as a handover document instead of being thrown away or, worse, being
  followed later as if it were current.

**Negative**

- **The spec folder is now named for something it no longer contains.** `2026-08-11-cambelt-azure-deployment`
  keeps its name deliberately, because CLAUDE.md, the roadmap and this file already reference it and renaming
  a folder to improve a title falsifies those references. Its `spec.md` says so at the top.
- **A deployment now spans two repositories**, and the failure mode is a compose file here that expects a
  network or a database the host has not created. That is why the tenant contract is written down in
  `docs/deployment-shared-host.md` rather than left as a convention.
- **HTTPS stays open as a gate here** and is closed elsewhere. This repository will never be able to assert it
  has been met, which was already true and is now structural.
- **Blast radius is shared**: one Postgres, one proxy, one host. Accepted, with the mitigation that
  `EnrichNpgsqlDbContext` already installs a retrying execution strategy, so a brief database restart during a
  host upgrade is a retry rather than an outage.

---

## 2026-08-21: A Release Is A Git Tag

**ID:** DEC-021
**Status:** Accepted
**Category:** Technical
**Stakeholders:** Product Owner, Tech Lead
**Amends:** the publish half of the CI arrangement recorded in CLAUDE.md on 2026-08-09

### Decision

**A push to `main` publishes an edge image. A `v<version>` git tag publishes a release.** Four parts:

1. **`ci.yml` publishes `:edge` and `:<sha>`**, and nothing else. The VERSION-value gate that decided whether
   a release happened is retired, along with the run-summary block written to shout about a forgotten bump.
   What replaces it answers the question that gate could only proxy: did anything outside `docs/`, `archive/`
   and root markdown change? A wrong answer there costs one edge build and cannot stop a release.
2. **`release.yml` promotes on a tag** by copying the manifest with `docker buildx imagetools create` into
   `:<version>`, `:latest` and `:stable`. It never rebuilds. It refuses if the tag does not name the `VERSION`
   at its own commit, if that commit never published, if the version tag already exists at a different digest,
   or if the commit is not an ancestor of `main`.
3. **`:latest` and `:stable` are the same digest under two names.** `latest` because it is what a bare
   `docker pull` resolves and it must not 404 or hand out an unblessed build; `stable` because
   `deploy/docker-compose.yml` references it by name and a name should say what it is. Compose now defaults to
   `${TAG:-stable}`; the dogfooding NAS runs `edge`.
4. **The release scripts stop publishing.** `docker push --all-tags` is gone from both; they bump `VERSION`
   and can build locally as `:dev`. CI is the only thing that can write to Docker Hub.

**The `VERSION` file stays**, and stays the source of the assembly version. Stability is expressed as a *tag*
and never as a label.

### Context

The publish job had been made to carry a decision a branch push cannot express. Every push to `main` meant
"release" unless `ci.yml` compared the value of `VERSION` against `github.event.before` and decided otherwise.
That gate then needed two mechanisms of its own to be survivable: a loud step-summary block, because a
forgotten bump was otherwise indistinguishable from a successful deploy in the Actions list, and a
`workflow_dispatch` escape hatch to force a publish past it. One of those was already paid for once - the
log-table search feature (`05885e5`) shipped with no bump and needed `4b178c2` a commit later.

Underneath it there was no way to say *this version is good*. The only alternative to following `:latest` was
pinning `TAG=0.21.0` by hand, which then never moves again, so a deployment was either on every bump or frozen.

The other half of the change is that the images carried **no metadata at all** - no `LABEL` in either
Dockerfile, no `labels:` input, no `metadata-action`. Because `.dockerignore` excludes `.git` and
`Directory.Build.props` sets `IncludeSourceRevisionInInformationalVersion=false`, a running container could
not say which commit built it by any route. It can now: `org.opencontainers.image.revision`.

### Alternatives Considered

1. **The textbook shape: delete `VERSION`, derive the version from the tag with MinVer or
   Nerdbank.GitVersioning, and let `docker/metadata-action` write the tag list.**
   - Pros: one source of truth for the number; no bump script; the conventional answer, and the right default
     on a greenfield service.
   - Cons: **it cannot work inside either image build.** `.dockerignore` excludes `.git`, so MinVer reads no
     history and `<Version>` resolves to its `0.0.0-alpha.0` fallback - the `1.0.0`-on-the-NAS failure of
     `0ddf1cc`, one day old, in a new costume and equally silent. The two escapes both undo a decision already
     taken: un-ignoring `.git` contradicts `Directory.Build.props`, which disables revision stamping precisely
     so one build cannot read two ways depending on where it ran; and computing the version outside and
     passing `--build-arg` puts it in *two* homes, which defeats the reason for going tag-only, in a file
     (`Dockerfile.gateway`) that says NO BUILD ARGUMENTS in capitals. Beyond that, this version is a
     **data-format field**: `BuildInfo` feeds it to `/api/meta`, to an export's `schemaVersion` and to the
     import's newer-than refusal, so every build not exactly on a tag would stamp `0.22.0-alpha.0.4` into
     files users download and re-upload - and `AccountImportService.TryParse` splits on `-`, so such a file
     then reports itself as a version that never shipped. A number written into other people's files should
     be chosen.
2. **Keep everything, and add `:stable` as a fourth tag on the existing publish job.**
   - Pros: purely additive; nothing to migrate; the NAS is untouched.
   - Cons: keeps the gate and both of its compensating mechanisms, and leaves `:latest` meaning the
     *unblessed* channel, so a bare `docker pull` still hands out the least-tested image in the registry.
3. **A `stable` OCI label instead of a tag.**
   - Pros: welded to the digest forever; inspectable.
   - Cons: it cannot be true. A label is written at build time and stability is judged days later. It is also
     unenforceable both ways: `imagetools create` cannot add a label to an existing manifest, so it would mean
     rebuilding a shipped version under a new digest; and nothing consumes labels - Watchtower selects on
     `enable` only, and compose can reference nothing but a tag.
4. **CI creates the git tag itself on every `VERSION` bump.**
   - Pros: no new manual step; "released" and "tagged" become the same event by construction.
   - Cons: then `:stable` equals `:edge` with extra steps. A blessing that happens automatically is not one.

### Rationale

The gate was a workaround for an overloaded trigger, so the fix is to stop overloading the trigger rather than
to improve the workaround. Once the tag is the release event, the forgotten-bump failure mode does not exist
to be compensated for: an un-bumped commit still reaches `:edge`, it just reports the previous number until
someone notices, and no release is silently skipped.

Promotion copies the manifest rather than rebuilding because `ci.yml`'s two "prove the image" steps validate a
specific digest - the deps.json version check and the gateway's run-time config and CSP probe. A rebuild would
place a different digest under the same version number, and those proofs would then be about an artifact the
deployment does not run.

Keeping `VERSION` is not conservatism. Alternative 1 sets out why the standard shape cannot reach inside these
two image builds, and `0ddf1cc` is the evidence that the failure is invisible from a working tree: the build,
the tests and the contract gate all passed while the NAS reported `1.0.0`.

### Consequences

**Positive**

- A version can be run before it is blessed. `:edge` and `:stable` are the two halves of that, and the
  dogfooding box is on the first.
- The one gate that could silently skip a release is gone, and with it the summary block and the escape hatch
  that existed to make it survivable.
- A published image now says which commit built it, which nothing in this repository could answer before.
- Only CI can write to Docker Hub. `docker push --all-tags` from a dev machine could previously have moved any
  tag of either repository, which becomes materially worse the moment a tag means "blessed".

**Negative**

- **A release is now two acts, and the second can be forgotten.** Nothing warns that `main` has been ahead of
  `:stable` for a month. Mitigated only by the tag command appearing in every edge run's summary.
- **Git tags exist in this repository for the first time**, so a clone that fetches no tags, or a
  delete-and-re-push, is a new class of mistake. Guards 3 and 4 in `release.yml` exist for the second.
- **`:latest` changes meaning.** It used to be the tip of `main`; it is now the newest release. Any deployment
  or script following it silently changes what it tracks, which is a behaviour change with no error attached.
- **Two workflow files instead of one**, and the tagged commit must itself contain `release.yml`, because a
  tag push runs the workflow as of the tagged commit.

---

## 2026-08-22: Anyone May Sign Up; A Plan Decides What They May Spend

**ID:** DEC-022
**Status:** Accepted
**Category:** Product / Technical
**Stakeholders:** Product Owner, Tech Lead
**Amends:** DEC-018 (the invitation allowlist), DEC-019 (the chat's cost controls)

### Decision

**Sign-up is open by default, and entitlement moves from the door to a plan resolved on every request.**
Five parts:

1. **`Signup:Mode` (`Open` | `InviteOnly`), defaulting to `Open`.** A blank `Signup:` section used to mean
   nobody new could be admitted; it now means anyone the Auth0 tenant authenticates gets an account. The
   invitation allowlist survives whole for `InviteOnly`, along with its refusal, its RFC 9457 `type` and its
   panel, so a private instance loses nothing.
2. **Two plans, `Free` and `Pro`, derived and stored nowhere.** `IAccountEntitlements` reads the comp list
   (`Plans:CompEmails` / `Plans:CompDomains`) against the account's **verified** address on every request.
   There is no plan column and no migration to add one.
3. **Three allowances, one record.** `PlanAllowances` bounds the assistant (off on Free), the documents an
   account may hold (100 / 2,000) and DVLA lookups per day (3 / 50). Per-file size stays a deployment
   constant at 25 MB.
4. **`User.EmailVerified` becomes a column**, set at provisioning and repaired by the existing address
   backfill. It only ever moves to true.
5. **Entitlement is not an Auth0 permission and not a Stripe-written flag.** Stripe, when it lands, adds one
   step to the resolver and moves nothing above it.

### Context

The allowlist was the only thing between a stranger and the deployment, and it worked by refusing to create
an account. That is right for one person's NAS and wrong for `cambelt.app`: Auth0 verifies addresses, and the
product wants sign-ups. But three surfaces cost real money or somebody else's quota - model tokens, the
documents volume, and the DVLA keys - and none of them was bounded by anything except the absence of
accounts.

So the question was never "how do we keep letting people in", it was "what is the door actually protecting".
The answer is those three surfaces, and each of them can be bounded directly.

### Alternatives Considered

1. **Auth0 RBAC: put `permissions: ["chat:use"]` in the access token and have a Stripe webhook write it
   through the Management API.** The obvious answer, and the one the platform documents.
   - Pros: no database read on the check; composes with the existing `McpRead`/`McpWrite` policies, which
     already match on scope *claims* rather than schemes; the entitlement travels with the credential.
   - Cons: **a JWT carrying an entitlement is a stored derived value**, and it goes stale in both directions -
     a cancelled subscriber keeps access until their token rotates, and somebody who has just paid cannot use
     what they bought. That is the whole premise of this project (README §1) arriving on the one surface where
     being wrong costs money. It also puts revenue on the **Auth0 Management API**, which this codebase has
     already found fragile twice: rate-limited enough to need `SignupRefusalCache`, and empty on the NAS for a
     release while invited people were told they were not invited. "Every subscription change must round-trip
     through it or entitlement is wrong" is not a sentence to accept about that dependency. And Auth0 roles
     are tenant state: nothing in this repository can assert them, test them or restore them, while there is
     no `CarTracker.WebApi.Tests` project and the house rule is that a policy worth being sure about goes in
     the domain and is proved against a real PostgreSQL.
2. **A `User.Plan` column a webhook flips.**
   - Pros: one read, trivially indexed; the obvious shape once Stripe exists.
   - Cons: today nothing would write it, which is the `Vehicle.PurchasePrice` trap - a stored field reaching
     no figure, and this project has already shipped that once and paid for it. The comp list covers the
     present need with no column at all, and the seam that matters is `IAccountEntitlements` rather than the
     schema.
3. **Keep the allowlist and gate the chat on it directly** (`Chat:AllowedEmails`).
   - Pros: smallest change; no new vocabulary.
   - Cons: it answers one of the three surfaces. Documents and DVLA would each grow their own list, and the
     three would drift - which is the "one predicate, read by every surface" rule this codebase applies to
     `EquipmentRules.CostIsSpend` and `WatchCalculator.IsLapsed` for exactly this reason.
4. **Leave sign-up closed and add tiers later.**
   - Pros: no change to the security posture; the polarity flip is genuinely a hazard.
   - Cons: the product is public. This defers the work without removing it, and it is cheaper to move the
     door and the allowances together than to move the door alone and then discover that a free account can
     fill the documents volume.

### Rationale

Entitlement is a derived fact about a subscription, and the central constraint says derived facts are computed
on read. Doing that needs the authority to be somewhere we can query synchronously and transactionally, which
means our own database - Stripe remains the authority on *payment*, and the row we keep is a cache of it with
a reconcile path, the same shape `pending_identity_deletions` already has for a different external system.

Verification moved rather than disappeared. It was load-bearing on the door - "an allowlist that can be
satisfied by typing is not an allowlist" - and it is load-bearing one layer down for the same reason: a comp
list written as a domain would otherwise hand the paid tier to anybody willing to register at that domain.
What changed is the consequence of failing it: not a locked-out stranger, but a free-tier account.

The DVLA allowance is the one that needed a ledger. The other two are derived from rows that exist - a chat
turn writes `chat_usage` because tokens leave no other trace, a document *is* a row and is counted - while a
lookup is a read-through that writes nothing at all.

### Consequences

**Positive**

- Public sign-up is possible without giving strangers the model bill, the documents volume or the DVLA quota.
- One predicate answers "what may this account spend", so the three surfaces cannot disagree.
- Checkout is one step inside `AccountEntitlements.ResolveAsync` and no change above it.
- A private deployment keeps the invitation door intact under one setting.

**Negative**

- **The polarity of a blank `Signup:` section reversed, in the dangerous direction.** A stale `deploy/.env`
  that predates this release opens a deployment its operator believes is shut. Mitigated by the boot posture
  line, by three files stating it, and by the fact that an open deployment now hands out far less than it
  used to - but not eliminated.
- **A deployment that forgets `Plans:CompEmails` switches the assistant off for its own operator.** The boot
  line warns, and it is the first thing to set on an upgrade.
- **Granting the paid tier is a config key and a restart.** No admin UI, and no per-account override.
- **The chat entry point is hidden rather than sold.** Correct while there is nowhere to send somebody, and a
  deliberate reversal the day checkout exists.
- `Signup:Mode` is bound as a **string** and parsed, not as the enum, because the compose file writes every
  key it knows and `""` bound to an enum takes the application down at boot. That is the
  `ChatSettings.DailyTokensPerOwner` trap, and it was reproduced before being designed around.
