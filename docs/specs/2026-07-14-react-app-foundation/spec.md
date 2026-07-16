# Spec Requirements Document

> Spec: React App Foundation
> Created: 2026-07-14
> Status: Complete

## Overview

Productionalise `archive/dashboard-full-claude-design/` into a running React + TypeScript application: extract its token system into Tailwind theme configuration, port its markup into typed components, and stand up the app shell, routing, data layer, and API type generation that every screen will build on. The design output is a one-time seed; after this spec the React app is the source of truth.

**Repointed 2026-07-15.** This spec was written against the superseded single-screen concept `archive/dashboard-design-idea/dashboard.html`. The reference is now the 17-screen output with its shared `theme.css` and `fonts.css`. The screens themselves are specced in `docs/specs/2026-07-15-frontend-screens/`; this spec is the foundation they stand on.

## User Stories

### The identity survives the port

As the owner, I want the app to look like the field manual it was designed as, so that the thing I use daily is the thing that was designed rather than a generic admin panel wearing its colours.

The design concept is not a colour scheme — it is a system. Status reads as a severity stripe and an uppercase mono label *first* and a colour second, so state survives greyscale and colour-blindness. Orange is structural (rules, eyebrows, section marks) and never semantic, kept on a separate axis from the green/amber/rust status scale, with blue reserved for data-integrity flags. Fonts are self-hosted because a strict CSP silently degrades CDN-loaded faces to system fallbacks (DEC-010 — extracted to `.woff2` and served from `'self'`, which preserves that property; *self-hosted* was always the requirement, not *inlined*). Each of these is a decision that a careless port would quietly discard, and none of them announces itself when broken.

### A wrong figure cannot render as a blank

As the developer, I want the API response types generated from the C# domain, so that a nullable MPG is a type error rather than an empty cell.

The derived-metrics service returns figures that are legitimately null: MPG with no previous fill, percent-used on a zero budget, cost-per-mile at zero miles. Hand-written TypeScript interfaces drift from the C# records silently — which is the exact defect class this project exists to eliminate, reintroduced at the wire. Generating from OpenAPI makes a domain change break the build.

### The next screen is cheap

As the developer, I want the shell, routing, data layer, and component primitives in place, so that building the Dashboard is building the Dashboard and not re-deciding the stack.

The design delivers **17 screens**. Every decision deferred here gets made seventeen times, inconsistently. (Corrected 2026-07-15: this said twelve — Phase 2 became seven screens under DEC-007, and Phase 3's seven bullets contain ten screens.)

## Spec Scope

1. **Vite + React + TypeScript scaffold** - `CarTracker.WebApp` with TSX throughout, strict mode, and the Aspire/API dev proxy wiring.
2. **Design token extraction** - The design's **single semantic token layer** as Tailwind v4 theme config (`@theme inline`), including the full dark-mode set and a working theme toggle. (Corrected 2026-07-15: there is no second layer — see the technical spec.)
3. **Component port** - The design's markup as typed, reusable components: the shared shell (top nav, bottom nav, page head, footer, toast — currently copy-pasted into all 17 screens), plus the ~60-class vocabulary in `theme.css`: panel, section head, status pill, stat tile, sheet, filters, segmented control, reg plate, odometer, data-integrity list, vehicle card.
4. **API type generation** - `openapi-typescript` wired to the ASP.NET OpenAPI document, with a generated-types check in CI, proven end-to-end against one live endpoint.
5. **App shell and data layer** - Vehicle-scoped React Router routes (`/` garage, `/:reg/…` screens; DEC-007), TanStack Query provider, error boundary, and loading/error conventions the later screens follow.

## Out of Scope

- The Dashboard itself, and every figure on it. This spec ports the *shell and vocabulary*; `Phase 2 → Dashboard` fills them. The artifact's hard-coded numbers do not become fixtures here.
- Any log CRUD, quick-add form, or table.
- Real API endpoints beyond the single meta endpoint that proves the codegen loop.
- Authentication (Phase 5).
- The Excel/CSV export and any charting (Phase 5 and §8 respectively).
- Re-porting future design-project output. Per the pipeline decision, the artifact is a one-time seed and React becomes the source of truth; no sync machinery is built.
- Mobile-native shells. Responsive web only — README §1's "fast data entry from a phone" is a browser on a phone.

## Expected Deliverable

1. `npm run dev` serves an app at the field-manual identity: the dossier header, section rules, and panel chrome from the artifact render as React components with Oswald/Inter/JetBrains Mono loading from `.woff2` files served from `'self'` under a strict CSP, with no system fallback (DEC-010).
2. The theme toggle switches light and dark and persists across reload; with no stored preference the app follows `prefers-color-scheme`. A greyscale screenshot of a status row still distinguishes overdue from OK, because the stripe and mono label carry the state.
3. `npm run gen:api` regenerates types from the running API's OpenAPI document, and CI fails if the committed types are stale. A page fetches the meta endpoint through TanStack Query and renders its response with generated types, proving the whole loop.
