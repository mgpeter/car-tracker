# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-14-react-app-foundation/spec.md

## Technical Requirements

### TSX, not JSX

**Decision: TypeScript throughout, `.tsx` for components and `.ts` for everything else.** `strict: true`, plus
`noUncheckedIndexedAccess` and `exactOptionalPropertyTypes`.

The argument is not general "types are good" — it is specific to this project. The derived-metrics service
returns figures that are **legitimately null**, by design:

| Figure | Null when |
|---|---|
| `mpg` | First fill, or the interval spans a partial fill |
| `percentUsed` | The category's budget is zero |
| `costPerMile` | Zero miles since purchase |
| `lastChecked` | The check has never been logged |

In JSX, `summary.fuelEconomy.averageMpg` on a renamed or absent field yields `undefined`, renders as an empty
cell, and looks like a styling bug for a week. Every one of those nulls means something specific that the UI
must say out loud — "no previous fill", "never logged" — and the null-vs-missing distinction is precisely what a
type system exists to keep straight.

`exactOptionalPropertyTypes` matters more than usual here: it keeps "the property is absent" separate from "the
property is present and null", which is the difference between a bug and a real state.

Cost is real: TSX is slower to write, and generated API types mean a C# rename breaks the front-end build. That
break is the feature.

### Project layout

```
src/CarTracker.WebApp/
  index.html
  vite.config.ts
  src/
    app/          # shell, router, providers, error boundary
    styles/       # tokens.css, fonts.css, index.css
    components/   # ported primitives
    features/     # per-screen, added by later specs
    api/          # generated types (do not edit), query client, fetch wrapper
    lib/          # formatters, hooks
  public/fonts/   # extracted .woff2
```

`api/generated/` carries a `do not edit` header and is committed — CI diffs it against a fresh generation.

### Token extraction: preserve both layers

`archive/dashboard-design-idea/dashboard.html` defines a **two-layer** system, and flattening it is the single
most likely way to ruin this port.

- **Layer 1 — raw palette.** The field manual's colours: `--ink #1E241B`, `--paper #E8E2CF`, `--green #5E7A34`,
  `--orange #B85C29`, `--rust #A23B2E`, `--blue #3E6187`, `--sand #C9B588`.
- **Layer 2 — semantic tokens.** What the artifact actually uses: `--bg`, `--surface`, `--surface-2`, `--fg`,
  `--muted`, `--faint`, `--line`, `--line-strong`, `--head-bg`, `--head-fg`, `--head-dim`, `--accent`, `--ok`,
  `--soon`, `--due`, `--info`, their `-wash` variants, `--shadow`, and the three font stacks.

**Components reference layer 2 only. Never layer 1.** This is what keeps `--accent` (structural: rules,
eyebrows, section marks) separable from `--due` (status). The artifact says so in a comment on line 10:
*accent is structure only — never status*. A component reaching for `--orange` directly is the bug this
structure prevents.

Note the semantic layer is not a rename of the palette — `--ok` is `--green`, but `--soon` is `#C79A22`, a
yellow that exists in the field manual as a waymark and has no palette token. The layers are genuinely
different sets.

### Tailwind v4 mapping

Tailwind v4 is CSS-first. Define both layers as ordinary custom properties in `styles/tokens.css` — carried
across from the artifact nearly verbatim — then expose the semantic layer to Tailwind:

```css
@import "tailwindcss";

@theme inline {
  --color-bg:      var(--bg);
  --color-surface: var(--surface);
  --color-fg:      var(--fg);
  --color-accent:  var(--accent);
  --color-ok:      var(--ok);
  --color-soon:    var(--soon);
  --color-due:     var(--due);
  --color-info:    var(--info);

  --font-display: var(--disp);
  --font-body:    var(--body);
  --font-mono:    var(--mono);
}
```

**`@theme inline` is required, not stylistic.** Plain `@theme` resolves values at build time, baking in the
light-mode colour and breaking dark mode entirely. `inline` emits `var(--bg)` into the utility so the runtime
value wins.

**Consequence worth stating: components need almost no `dark:` variants.** Because the semantic tokens
themselves flip, `bg-surface text-fg` is correct in both themes automatically. Reach for `dark:` only where a
component needs a genuinely different *treatment* in dark, not merely a different colour. If `dark:` variants
start proliferating, the token layer is being bypassed.

`.num { font-variant-numeric: tabular-nums }` becomes Tailwind's `tabular-nums` utility, applied to every
aligned figure per the design language.

### Theme toggle, and the CSP tension it creates

The artifact defines `prefers-color-scheme` **and** `:root[data-theme="light"|"dark"]` overrides, but nothing
in its markup ever sets `data-theme` — the toggle does not exist yet. Build it.

- Resolution order: stored preference (`localStorage`) → `prefers-color-scheme` → light.
- The toggle writes `data-theme` on `<html>` and persists the choice. A three-way control (Light / Dark /
  System) is required, because a two-way toggle makes "follow my OS" unreachable once a preference is stored.

**The flash-of-wrong-theme problem.** React sets `data-theme` after hydration, so a dark-mode user sees a frame
of cream `#E8E2CF` on every load. Preventing it needs a small blocking script in `index.html` that runs before
first paint — which collides directly with the strict CSP that motivated inlining the fonts in the first place.

Resolve it deliberately, do not stumble into it:

- Compute the SHA-256 of the inline script at build time and emit `script-src 'sha256-…'`. **Not** `'unsafe-inline'`, which would defeat the CSP wholesale.
- A Vite plugin generates the hash and injects it into the CSP `<meta>` (dev) and the API's response header (prod), so the hash cannot drift from the script.
- Fallback if hashing proves awkward in the Aspire dev loop: ship the script as a tiny `<script src>` from
  `'self'`. Costs one blocking request, needs no hash. Take this route rather than weakening the CSP.

### Fonts: extract to .woff2, do not carry the base64 across

The artifact inlines Oswald, Inter, and JetBrains Mono as base64 data URIs — about 135 KB of its 175 KB, on
lines 3–5.

**That was a constraint of being one self-contained file, not a design requirement.** CLAUDE.md records the
reason as CSP: the field manual loads fonts from Google's CDN, and under a strict CSP those silently fall back
to system faces. The requirement is *self-hosted*, not *inlined*. In a real app, inlining is strictly worse:

- Base64 costs ~33% over binary.
- Fonts sit in the CSS, so they cannot be cached separately and every CSS change re-downloads them.
- Render is blocked on the whole stylesheet.

So: decode the data URIs to `.woff2` under `public/fonts/`, serve from `'self'`, and set `font-src 'self'`. The
CSP property that motivated the inlining is fully preserved; delivery gets better. This is a deliberate
divergence from the artifact and the only one in this spec.

- Subset to Latin. The full faces are large and this app is English-only.
- `font-display: swap` for Inter and JetBrains Mono.
- **`font-display: block` for Oswald.** It is the display face carrying the identity, it appears in the dossier
  header above the fold, and a FOUT swapping condensed Oswald for Arial Narrow is far more visible than a brief
  blank. Cap with a short block period.
- Preload only the Oswald and Inter weights used above the fold.

### Component port

From the artifact's CSS, the reusable vocabulary:

| Component | Artifact source | Notes |
|---|---|---|
| `<Wrap>` | `.wrap` | 1180px max, 20px gutter |
| `<Dossier>` | `.dossier`, `.contours`, `.chips` | Header. Contour SVG is decorative — `aria-hidden` |
| `<RegPlate>` | `.plate`, `.gb`, `.reg` | GB plate. Hard-coded blue/yellow are *real-world* colours, correctly outside the token system |
| `<Odometer>` | `.odo`, `.drum` | Digit drums |
| `<SectionHead>` | `.sec-head`, `.rule` | h2 + orange rule. Structural accent |
| `<Panel>` | `.panel` + `.pad`/`.attn`/`.renewals`/`.integrity` | Variants via props, not class strings |
| `<StatusPill>` | `.pill` | Uppercase mono label. **Label is required, not optional** |
| `<StatTile>` | `.tile`, `.t-n`, `.t-l` | Four states: `due`/`soon`/`ok`/`info` |
| `<Chip>` | `.chip` | Mono key/value |
| `<IntegrityList>` | `.ilist`, `.iw`, `.cmp`, `.ar` | Old-value/new-value comparison rows |
| `<VehicleCard>` | — (no artifact source yet) | Garage card: reg plate, status badge, attention summary (DEC-007). Design arrives from the garage-homepage Claude Design pass; listed here so the port inventory is complete |

**Status components take a discriminated union, never a colour.**

```ts
type Status =
  | { kind: 'ok' }
  | { kind: 'dueSoon'; daysRemaining: number }
  | { kind: 'overdue'; daysOverdue: number }
  | { kind: 'neverLogged' }
```

This mirrors the domain's four check states exactly, and makes `neverLogged` unrepresentable-as-`ok` at the type
level — the same bug that makes the workbook's Dashboard count 17 checks out of 18.

`<StatusPill label>` is a required prop. It cannot be omitted, because the label *is* the state; colour is
reinforcement. The artifact's four tiles read "Overdue / Due soon / OK / Never logged" (7/3/7/1, summing to 18)
— never bare colour.

`--info` (blue) is reserved for data-integrity flags and is a different axis from due-status. `<StatusPill>`
must not accept `info` as a status `kind`; integrity flags are a separate component.

### App shell and routing

- React Router v7, declarative routes. **Routing is vehicle-scoped** (DEC-007): `/` is the garage (selection
  and the add-car entry point); every vehicle screen lives under `/:reg/…` — `/:reg/dashboard`, `/:reg/fuel`,
  and so on. Placeholder routes for the Phase 2/3 screens so navigation exists from day one; each renders a
  stub.
- **The registration is the URL segment, not the database id.** `/bt53akj/fuel` is readable, stable across a
  re-import, and shareable; `/vehicles/3/fuel` is none of those. Normalise case and spacing on match, the same
  rule as the DB's unique index.
- **The URL is the vehicle context.** A `VehicleProvider` reads the route param and resolves the vehicle;
  there is no global "current vehicle" store to go stale — the bug class where the header shows one car and
  the data belongs to another cannot be built. Switching cars is navigation.
- localStorage remembers the last-visited registration to power a "jump back in" affordance **on the garage
  page** — never an automatic redirect past it. The garage is the home screen; with one car it is one tap
  deep, which is cheap enough.
- One error boundary at the shell, one per route. An unknown registration renders a not-found state offering
  the garage, not a crash.
- The artifact is a single scrolling page with no nav — nav is this spec's invention. Keep it minimal: the
  field manual numbers its sections 01–05 because a document has a reading order, and CLAUDE.md is explicit
  that this must not carry into app UI. A dashboard is scanned, not read; numbering it is decoration posing as
  information.

### Data layer

TanStack Query v5, one `QueryClient` at the shell.

- `staleTime` 30s default; `refetchOnWindowFocus` on — coming back to a tab should not show a stale countdown.
- Mutations invalidate by query key. No manual cache surgery.

**On DEC-002.** The query cache is a client-side read cache with explicit invalidation and a short TTL, holding
a response in memory until refetch. It is **not** a stored derived value in the DEC-002 sense: nothing is
persisted, nothing is authoritative, and a stale entry is a UI freshness question, not a data-integrity one. The
server still computes every figure on every read.

Stating this because the boundary will be tested later: caching *a response* on the client is fine; caching *a
computed figure* on the server is what DEC-002 forbids. If a future change proposes persisting a query result,
that is a decision entry, not a config tweak.

**The client performs no arithmetic on money or measurements.** Ever. It formats and displays. C# `decimal`
crosses the wire as a JSON number and lands in a float64, where `0.1 + 0.2 !== 0.3` — but that never bites,
because nothing on the client adds. A component summing a column has both violated DEC-002 and introduced float
error. Formatters take numbers and return strings; they never combine them.

### API type generation

- `openapi-typescript` against the ASP.NET OpenAPI document (built into .NET 10 via `Microsoft.AspNetCore.OpenApi`).
- `npm run gen:api` writes `src/api/generated/schema.d.ts`, committed.
- CI regenerates and runs `git diff --exit-code`. Stale types fail the build.
- A thin typed `fetch` wrapper consumes the generated paths. No client generator — the endpoint surface is small
  and a generated client is more machinery than it earns.

**Proving the loop needs one live endpoint.** `GET /api/meta` exists solely so codegen, fetch, TanStack Query,
and render can be demonstrated end-to-end without waiting on the Dashboard. See `sub-specs/api-spec.md`.

### Aspire and dev loop

**Revised by DEC-009 and built 2026-07-14.** The original text said the API would serve the built static
assets — "same origin, no CORS, no second container". A `CarTracker.Gateway` (YARP) does that job instead.
The same-origin property survives; the mechanism changed.

- `CarTracker.AppHost` registers the Vite app via **`AddViteApp`** (not `AddNpmApp` — `Aspire.Hosting.NodeJs`
  was renamed `Aspire.Hosting.JavaScript` in Aspire 13 and `AddNpmApp` no longer exists), with the gateway
  referencing both it and the API, so one `dotnet run` starts everything.
- **The gateway is the only origin, in dev and prod alike**: `/` → the app, `/api` → the WebApi, `/scalar` and
  `/openapi` → the WebApi. Vite does **not** proxy `/api`; the gateway owns it, and a second routing authority
  that existed only in development would be exactly the dev/prod divergence this avoids.
- Dev: a catch-all route in the gateway's `appsettings.Development.json` proxies `/` to the Vite dev server.
  Prod: the gateway serves `dist` via `UseStaticFiles` + `MapFallbackToFile`, gated on `!IsDevelopment()`.
  Parity is at the URL level; a dev server and a static bundle are different things.
- **HMR works through YARP** and is verified. Leave `server.ws` unset so the HMR client connects back to the
  origin it was served from (the gateway); YARP forwards the WebSocket upgrade by default. But **set
  `allowedHosts: true`** — Vite otherwise rejects the proxied request. Confirmed empirically against a working
  reference project; the symptom is a quiet `[vite] Direct websocket connection fallback.` in the console
  rather than an error.
- Postgres: give `AddPostgres` an explicit password parameter with a default in the AppHost's
  `appsettings.Development.json`. With `WithDataVolume()` and a generated password, run two onwards fails
  authentication against the volume from run one, and the AppHost hangs on `WaitFor` with nothing in its log.
  A parameter with no resolvable value blocks on a **dashboard modal**, also silently.

### Testing

- Vitest + React Testing Library. jsdom.
- **The greyscale property is testable, and must be tested.** Assert that every status component renders its
  textual label — query by accessible name, never by class. A status conveyed only by colour fails the test,
  which is the point: the property that "state survives greyscale" cannot be verified by a human eyeballing a
  screenshot every time.
- Theme toggle tests: resolution order (stored → system → light), persistence across remount, and that
  `data-theme` lands on `<html>`.
- `vitest-axe` on the shell and every ported component. The artifact already uses `aria-hidden` on decorative
  SVG and `aria-label` on controls; the port must not lose them.
- A token test asserting no component references a layer-1 palette variable directly — grep the built CSS for
  raw palette names outside `tokens.css`.

## External Dependencies (Conditional)

- **react** (19.x), **react-dom** (19.x) - UI runtime. Per tech-stack.md.
- **vite** (7.x) + **@vitejs/plugin-react** - Build and dev server. Per tech-stack.md.
- **typescript** (5.7+) - Per the TSX decision above.
- **tailwindcss** (4.x) + **@tailwindcss/vite** - Per DEC-005. v4 for CSS-first `@theme inline`, which the
  dark-mode token flipping depends on.
- **react-router** (7.x) - Routing.
  - **Justification:** Mature, and this app's routing is unremarkable. TanStack Router's typed routes are
    attractive alongside strict TSX, but its benefit concentrates in complex nested search-param state that this
    app does not have.
- **@tanstack/react-query** (5.x) - Server state.
  - **Justification:** Selected 2026-07-14. Twelve screens across Phases 2–3 each need loading, error, and
    refetch-after-mutate; hand-rolling that twelve times produces twelve inconsistent versions.
- **openapi-typescript** (7.x, dev) - Generate TS types from the OpenAPI document.
  - **Justification:** Keeps the C# result records the single source of truth. Types-only, no runtime weight.
- **lucide-react** - Icons. Per DEC-005.
- **vitest**, **@testing-library/react**, **@testing-library/jest-dom**, **jsdom**, **vitest-axe** (dev) - Tests.
  - **Justification:** Vitest shares Vite's transform pipeline, so the test build cannot drift from the app
    build. `vitest-axe` enforces the accessibility the artifact already demonstrates.
- **subset-font** or **glyphhanger** (dev, one-off) - Decode the artifact's base64 faces and subset to Latin.
  - **Justification:** Used once during the font extraction task, not a runtime or CI dependency. Either tool
    is fine; the extraction is a one-time job.
