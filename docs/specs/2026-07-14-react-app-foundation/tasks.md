# Spec Tasks

## Tasks

- [ ] 1. Scaffold, TypeScript, and the dev loop
  - [ ] 1.1 Create `src/CarTracker.WebApp` via Vite with the react-ts template
  - [ ] 1.2 Set `strict`, `noUncheckedIndexedAccess`, and `exactOptionalPropertyTypes` in `tsconfig.json`
  - [ ] 1.3 Add Vitest, React Testing Library, jsdom, and `vitest-axe`; write one smoke test and confirm it runs
  - [ ] 1.4 Add the `/api` dev proxy in `vite.config.ts`
  - [ ] 1.5 Register the app in `CarTracker.AppHost` via `AddNpmApp` with an API reference
  - [ ] 1.6 Configure the API to serve built static assets in production, same origin
  - [ ] 1.7 Verify `dotnet run` on the AppHost starts both, and all tests pass

- [ ] 2. Fonts and tokens
  - [ ] 2.1 Decode the base64 faces from `dashboard.html` lines 3–5 into `.woff2` under `public/fonts/`
  - [ ] 2.2 Subset all three faces to Latin and record the size before and after
  - [ ] 2.3 Write `styles/fonts.css`: `block` for Oswald, `swap` for Inter and JetBrains Mono; preload above-the-fold weights
  - [ ] 2.4 Port both token layers into `styles/tokens.css` verbatim from the artifact, keeping the raw palette and semantic layers separate
  - [ ] 2.5 Wire the semantic layer into Tailwind with `@theme inline` and confirm a utility resolves to `var(--bg)` and not a baked hex
  - [ ] 2.6 Write the token test: no file outside `tokens.css` references a layer-1 palette variable
  - [ ] 2.7 Verify fonts load from `'self'` under a strict CSP with no system fallback, and all tests pass

- [ ] 3. Theme toggle
  - [ ] 3.1 Write tests for resolution order — stored preference, then `prefers-color-scheme`, then light
  - [ ] 3.2 Implement the theme store with localStorage persistence and a three-way Light/Dark/System control
  - [ ] 3.3 Write the pre-paint inline script setting `data-theme` on `<html>` before first paint
  - [ ] 3.4 Add the Vite plugin computing the script's SHA-256 and injecting it into the CSP; fall back to an external `'self'` script rather than weakening the CSP
  - [ ] 3.5 Write tests: `data-theme` lands on `<html>`, persists across remount, and System tracks the media query
  - [ ] 3.6 Verify no flash of wrong theme on hard reload in dark mode, and all tests pass

- [ ] 4. Component port
  - [ ] 4.1 Write tests asserting every status component renders its textual label, queried by accessible name and never by class
  - [ ] 4.2 Define the `Status` discriminated union with all four states, mirroring the domain
  - [ ] 4.3 Port `<Wrap>`, `<SectionHead>`, `<Panel>` and its four variants
  - [ ] 4.4 Port `<StatusPill>` with a required label, and `<StatTile>` with the four states
  - [ ] 4.5 Port `<Dossier>`, `<RegPlate>`, `<Odometer>`, and `<Chip>`, preserving `aria-hidden` on decorative SVG
  - [ ] 4.6 Port `<IntegrityList>` on the `--info` axis, and assert it cannot take a due-status kind
  - [ ] 4.7 Run `vitest-axe` across every ported component
  - [ ] 4.8 Verify a greyscale render still distinguishes overdue from OK, and all tests pass

- [ ] 5. Data layer, codegen, and shell
  - [ ] 5.1 Add `GET /api/meta` to `CarTracker.WebApi` using `TimeProvider`, with a contract test on the OpenAPI document
  - [ ] 5.2 Wire `openapi-typescript` and `npm run gen:api`, writing to `src/api/generated/` with a do-not-edit header
  - [ ] 5.3 Add the CI step regenerating types and failing on `git diff --exit-code`
  - [ ] 5.4 Write the typed fetch wrapper over the generated paths, including the network-error path
  - [ ] 5.5 Add the `QueryClient` provider with a 30s `staleTime` and `refetchOnWindowFocus`
  - [ ] 5.6 Add React Router with stub routes for the Phase 2/3 screens, unnumbered, plus shell and per-route error boundaries
  - [ ] 5.7 Build a page fetching `/api/meta` through TanStack Query, rendering with generated types
  - [ ] 5.8 Verify the full loop — rename a C# property, regenerate, and confirm the front-end build breaks — and all tests pass
