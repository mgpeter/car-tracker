# Spec Tasks

## Tasks

- [x] 1. Scaffold, TypeScript, and the dev loop — **complete 2026-07-15** (1.1–1.2, 1.4–1.7 landed 2026-07-14; 1.3 with the test harness)
  - [x] 1.1 Create `src/CarTracker.WebApp` via Vite with the react-ts template
  - [x] 1.2 Set `strict`, `noUncheckedIndexedAccess`, and `exactOptionalPropertyTypes` in `tsconfig.json`
  - [x] 1.3 Add Vitest, React Testing Library, jsdom, and ~~`vitest-axe`~~ **axe-core directly**; write one smoke test and confirm it runs — **done 2026-07-15**. `vitest-axe@0.1.0` is its latest release and throws on import under Vitest 4: it reaches into `__vitest_poll_takeover__`, a Vitest internal that no longer exists. `src/test/axe.ts` implements the matcher on `axe-core` in ~20 lines with no coupling to test-runner internals. The smoke suite includes a test asserting axe *reports* a known violation, so a silently no-op axe fails loudly instead of making every later `toHaveNoViolations()` pass vacuously
  - [x] 1.4 ~~Add the `/api` dev proxy in `vite.config.ts`~~ — superseded by DEC-009: the gateway owns `/api`, and a Vite proxy would be a second routing authority existing only in dev. Set `allowedHosts: true` instead, which Vite needs to accept proxied requests.
  - [x] 1.5 Register the app in `CarTracker.AppHost` via **`AddViteApp`** (`AddNpmApp` no longer exists in Aspire 13)
  - [x] 1.6 ~~Configure the API to serve built static assets in production~~ — superseded by DEC-009: `CarTracker.Gateway` serves them
  - [x] 1.7 Verify `dotnet run` on the AppHost starts everything — **done 2026-07-14**: `/` serves the app, `/api/meta` returns live JSON, `/openapi` and `/scalar` serve, and HMR reconnects over the gateway's WebSocket

  **Built 2026-07-14 (scaffold only — tasks 2-4, the design-system port, are untouched):**
  - Nine projects: added `CarTracker.Gateway` and `CarTracker.ServiceDefaults` (DEC-009).
  - API-key auth (`X-Api-Key`), `/api/meta` anonymous, `/api/meta/authenticated` to verify a key. `src/lib/settings.ts` holds the key in localStorage; `src/api/client.ts` injects it and distinguishes 401 from a network error.
  - **`Microsoft.OpenApi` pinned to 2.10.0** — `Microsoft.AspNetCore.OpenApi` 10.0.10 pulls 2.0.0, which has a high-severity advisory (NU1903). v2 also flattened the `Microsoft.OpenApi.Models` namespace.
  - Task 1.3 (Vitest/axe) deliberately not done: it belongs with the component port that has components to test.

- [x] 2. Fonts and tokens — **done 2026-07-15**
  - [x] 2.1 Decode the three base64 faces from `dashboard-full-claude-design/fonts.css` into `.woff2` under `public/fonts/` (DEC-010) — 135KB of base64 → 101KB of `.woff2` (Inter 48K, JetBrains Mono 31K, Oswald 21K)
  - [x] 2.2 ~~Subset all three faces to Latin and record the size before and after~~ — **already subset upstream**. Verified with fontTools: ~230 codepoints each (a full Inter carries ~2,500), all three variable. Before = after; there is nothing to remove. The real finding was the opposite of the task's assumption — see 2.8
  - [x] 2.3 Write `styles/fonts.css` pointing at the extracted `.woff2`: `block` for Oswald (identity, above the fold), `swap` for Inter and JetBrains Mono. Note the design ships `block` for all three — we differ deliberately. **Also corrected the declared weight ranges to the real `fvar` axes** (Inter `100 900`, not the design's `400 600`; JetBrains Mono `400 800`, not `400 700`): the design under-declared them, so a `font-weight:700` Inter — which `.brand`, `.pmeta b` and `footer b` all ask for — would have been clamped to 600 and synthetically emboldened
  - [x] 2.4 Port the single semantic token layer into `styles/tokens.css` from `theme.css`, **after diffing it against the forked copies inlined in `dashboard.dc.html` and `fuel-log.dc.html`**; ~~fix `--shadow` missing from `[data-theme="dark"]`~~; restore the `/* structure only — never status */` comment on `--accent`. **The diff found the tokens byte-identical across all three copies** — no drift; `theme.css` is canonical (its `.mark` is the superset, carrying `text-decoration:none;display:inline-block` for `<a>` use). The forks' real differences are per-screen hero treatments (dashboard's `.plate .reg` at 30px vs the shared 15px), which become component props. **Both token "bugs" in the plan dissolved on inspection:** `--shadow` is declared in three places and consumed in *none* across all 20 files, so it was dropped rather than propagated — a token nothing reads is not worth a dark variant; and `--sand` is theme-independent *by design*, since all seven usages sit on `--head-bg`, which is dark in both themes
  - [x] 2.5 Wire the semantic layer into Tailwind with `@theme inline` and confirm a utility resolves to `var(--bg)` and not a baked hex — asserted in `tokens.test.ts` against the real Tailwind compiler, and re-confirmed in `dist`: `--color-bg:var(--bg)`
  - [x] 2.6 Write the token test: no component references a raw hex or a palette name (`--ink`/`--paper`/`--green`/`--orange`/`--rust`/`--blue`) — they must use the semantic tokens. **Rewritten 2026-07-15:** the original test ("no file outside tokens.css references a layer-1 palette variable") passed vacuously, because no layer-1 variable exists anywhere outside the field manual. The replacement caught a real `#666` in `App.tsx` on its first run
  - [x] 2.7 Verify fonts load from `'self'` under a strict CSP — `dist` serves all three from `/fonts/*.woff2`, same-origin, with the `block`/`swap` split intact. **Confirmed in Chrome against the enforced CSP (see task 3.6):** all three report `loaded` under `font-src 'self'`, with the corrected axes live. ~~with no system fallback~~ — **true for text, and 2.8 is the exception**: the 15 icon glyphs still fall back until DEC-013's sprite lands in task 4, which is the only reason this line is not unqualified
  - [x] 2.8 **New — the finding that reframes 2.2/2.7.** The design's own subset omits **15 glyphs the design itself uses**: `＋` (the quick-add FAB, ×29), `→` (×69), `✓` (×23), `▾`, `⌂`, `⇄`, `⠿` (the settings drag grips), `Δ` (budget variance), `⚙`, `₂`, `≈`, `≡`, `↔`, `↑`, `↓`. They render today only because the *system font* silently supplies them — precisely what DEC-010 forbids, and a per-glyph fallback that a per-font check like 2.7 cannot see. Re-subsetting cannot fix it: `⠿` is a Braille pattern, and `⌂`/`⚙`/`⇄` ship in no Inter, Oswald or JetBrains Mono at any subset level. **Owner's call 2026-07-15 → SVG icon sprite (DEC-013)**, which is also the conventional answer, since every one of these is an icon wearing a glyph's clothes. Built in task 4

- [x] 3. Theme toggle — **done 2026-07-15**
  - [x] 3.1 Write tests for resolution order — stored preference, then `prefers-color-scheme`, then light
  - [x] 3.2 Implement the theme store with localStorage persistence and a three-way Light/Dark/System control. Storage key `ct-theme`, kept from the design. **`<ThemeToggle>` renders a radiogroup, not the design's three `aria-pressed` buttons** — this is one choice among three, and three toggle buttons side by side announce as three independent toggles. Arrow-key navigation follows from the role rather than from extra code; the visual treatment is the design's, unchanged
  - [x] 3.3 Write the pre-paint inline script setting `data-theme` on `<html>` before first paint. It handles only an **explicit** choice: `system` is the *absence* of the attribute, resolved by `tokens.css` (`@media (prefers-color-scheme: dark) :root:not([data-theme='light'])`), so the script never asks the OS anything and stays at 134 bytes. This is also what keeps `system` tracking a live OS change — the design's `componentDidMount` approach applies the theme after first paint, which is the flash
  - [x] 3.4 Add the Vite plugin computing the script's SHA-256 and injecting it into the CSP; ~~fall back to an external `'self'` script rather than weakening the CSP~~ — not needed, the hash works. **`THEME_SCRIPT` is exported as a string and is the single source of both the injected markup and the hash**; if the two ever differ by a byte the browser silently refuses to run it and the flash returns. Applied on **build only**: Vite's dev server injects its own inline scripts for HMR, so a strict policy in dev would force `'unsafe-inline'` — and a policy with `'unsafe-inline'` is not a policy, it only looks like one
  - [x] 3.5 Write tests: `data-theme` lands on `<html>`, persists across remount, and System tracks the media query — plus the inverse (an explicit choice must *not* follow the OS), and the shipped script string is executed against real `localStorage` rather than reimplemented in the test
  - [x] 3.6 Verify no flash of wrong theme on hard reload in dark mode, and all tests pass — **35 tests, `npm run build` exit 0.** The mechanism is asserted against the built `dist/index.html`: the script precedes the stylesheet, and the CSP hash matches the shipped bytes exactly.

    **Caught here: the CSP was landing *after* the script it hashed.** Both tags are head-prepended in array order, and a `<meta>` CSP governs only what is parsed after it — so the policy never saw the script, the hash was decorative, and a wrong hash would have failed silently in the direction that looks like success. Fixed, and `theme-csp.test.ts` now asserts the ordering.

    **Verified in Chrome against the built bundle, 2026-07-15.** Served `dist` over `vite preview` (so the CSP is the real one) with `ct-theme=dark`:

    - **No flash, proven rather than argued.** Sampled on a fresh load, `data-theme` was already `dark` while `performance.getEntriesByType('paint')` was still **empty** — the theme was settled before first paint had happened at all. `domInteractive` 19.5ms; the React bundle's `responseEnd` was 36.7ms, so React cannot have been what set it. In the bytes the browser actually receives, the script is at offset 449, the React module at 828, the render-blocking stylesheet at 908, `<body>` at 990: the browser cannot paint before the stylesheet, and the script runs ~460 bytes earlier. The ordering is structural, not lucky.
    - **The CSP hash does real work.** `disposition: "enforce"`, and an unhashed inline script injected at runtime was **blocked** (`inlineScriptExecuted: false`) while our pre-paint script ran. That is the pair of facts that matters: the policy rejects inline scripts, and ours is honoured.
    - **Fonts load from `'self'` under that policy** — all three report `loaded`, with the corrected axes live (`Inter 100 900`, `JetBrains Mono 400 800`). This closes **task 2.7's other half**.
    - `--shadow` resolves to empty, confirming the dropped token is genuinely gone rather than inherited.
    - Toggling Light/Dark/System repaints live and persists.

    ⚠️ **Tooling note for later:** `read_console_messages` does **not** surface browser-generated CSP violation reports — a deliberately-triggered `img-src` violation left no console trace, while a `console.warn` probe in the same page was captured. Do not read a quiet console as "no CSP violations"; listen for the `securitypolicyviolation` event instead, which is what produced the evidence above.

- [ ] 4. Shell and component port

  **Expanded 2026-07-15.** The original inventory had 11 components taken from a single-screen concept with no
  navigation. `theme.css` ships **~60 component classes** including a full nav system, a bottom nav, sheets,
  filters and a toast — and the shell is **copy-pasted into all 17 screens**, theme logic included. Extracting
  it once is the single biggest win in this task.

  - [ ] 4.1 Write tests asserting every status component renders its textual label, queried by accessible name and never by class
  - [ ] 4.2 Define the `Status` discriminated union with all four states, mirroring the domain; `--info` is a separate axis, so `<StatusPill>` must not accept it
  - [ ] 4.3 Extract the shared shell **once**: `<TopNav>` (6 links + More dropdown + theme button), `<BottomNav>` (5 slots incl. the quick-add FAB), `<PageHead>` (contours SVG `aria-hidden`, eyebrow, h1, reg plate), `<Footer>`, `<Toast>`. **Reconcile the two nav taxonomies** — desktop groups More as Records/Watch & plan/Reference; mobile uses Daily/Records/Watch & plan/Reference and files Garage, Mileage and Wash differently
  - [ ] 4.4 Port `<Wrap>`, `<SectionHead>`, `<Panel>`, `<Btn>`, `<Kv>`, `<Stats>`, `<Chip>`
  - [ ] 4.5 Port `<StatusPill>` (label required), `<StatTile>` (four states), `<RegPlate>`, `<Odometer>`, `<IntegrityList>`, `<VehicleCard>`
  - [ ] 4.6 Port `<Sheet>` + `<Field>` + `<Seg>` + `<Filters>`/`<FChip>`/`<FSel>`. **The sheet needs what the design lacks**: `aria-modal`, focus trap, Escape, focus restore, scroll lock — today it is `role="dialog"` on a plain div dismissed only by a scrim click
  - [ ] 4.7 Run `vitest-axe` across every ported component
  - [ ] 4.8 Verify a greyscale render still distinguishes overdue from OK, and all tests pass

- [ ] 5. Data layer, codegen, and shell
  - [x] 5.1 Add `GET /api/meta` to `CarTracker.WebApi` using `TimeProvider` — **done 2026-07-14** (DEC-009 scaffold). Also live: `/api/meta/authenticated`, `POST /api/vehicles`, `GET /api/vehicles/{reg}/summary`
  - [ ] 5.2 Wire `openapi-typescript` and `npm run gen:api`, writing to `src/api/generated/` with a do-not-edit header
  - [ ] 5.3 Add the CI step regenerating types and failing on `git diff --exit-code`
  - [ ] 5.4 Write the typed fetch wrapper over the generated paths, including the network-error path
  - [ ] 5.5 Add the `QueryClient` provider with a 30s `staleTime` and `refetchOnWindowFocus`
  - [ ] 5.6 Add React Router: `/` garage, `/:reg/…` for all 17 screens, unnumbered, plus shell and per-route error boundaries. `VehicleProvider` reads the route param — no global current-vehicle store to go stale. **The design has no routing at all** (flat filenames, registration never in a URL), so this is entirely new
  - [ ] 5.7 Build a page fetching `/api/meta` through TanStack Query, rendering with generated types
  - [ ] 5.8 Verify the full loop — rename a C# property, regenerate, and confirm the front-end build breaks — and all tests pass
