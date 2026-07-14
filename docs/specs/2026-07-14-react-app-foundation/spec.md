# Spec Requirements Document

> Spec: React App Foundation
> Created: 2026-07-14
> Status: Planning

## Overview

Productionalise `archive/dashboard-design-idea/dashboard.html` into a running React + TypeScript application: extract its token system into Tailwind theme configuration, port its markup into typed components, and stand up the app shell, routing, data layer, and API type generation that every Phase 2 and Phase 3 screen will build on. The artifact is a one-time seed; after this spec the React app is the source of truth.

## User Stories

### The identity survives the port

As the owner, I want the app to look like the field manual it was designed as, so that the thing I use daily is the thing that was designed rather than a generic admin panel wearing its colours.

The design concept is not a colour scheme — it is a system. Status reads as a severity stripe and an uppercase mono label *first* and a colour second, so state survives greyscale and colour-blindness. Orange is structural (rules, eyebrows, section marks) and never semantic, kept on a separate axis from the green/amber/rust status scale, with blue reserved for data-integrity flags. Fonts are inlined because a strict CSP silently degrades CDN-loaded faces to system fallbacks. Each of these is a decision that a careless port would quietly discard, and none of them announces itself when broken.

### A wrong figure cannot render as a blank

As the developer, I want the API response types generated from the C# domain, so that a nullable MPG is a type error rather than an empty cell.

The derived-metrics service returns figures that are legitimately null: MPG with no previous fill, percent-used on a zero budget, cost-per-mile at zero miles. Hand-written TypeScript interfaces drift from the C# records silently — which is the exact defect class this project exists to eliminate, reintroduced at the wire. Generating from OpenAPI makes a domain change break the build.

### The next screen is cheap

As the developer, I want the shell, routing, data layer, and component primitives in place, so that building the Dashboard is building the Dashboard and not re-deciding the stack.

Phase 2 adds five screens after this one and Phase 3 adds seven more. Every decision deferred here gets made twelve times, inconsistently.

## Spec Scope

1. **Vite + React + TypeScript scaffold** - `CarTracker.WebApp` with TSX throughout, strict mode, and the Aspire/API dev proxy wiring.
2. **Design token extraction** - The artifact's two-layer token system (raw palette → semantic tokens) as Tailwind v4 theme config, including the full dark-mode set and a working theme toggle.
3. **Component port** - The artifact's markup as typed, reusable components: app shell, dossier header, panel, section head, status pill, stat tile, and the data-integrity list.
4. **API type generation** - `openapi-typescript` wired to the ASP.NET OpenAPI document, with a generated-types check in CI, proven end-to-end against one live endpoint.
5. **App shell and data layer** - React Router routes, TanStack Query provider, error boundary, and loading/error conventions the later screens follow.

## Out of Scope

- The Dashboard itself, and every figure on it. This spec ports the *shell and vocabulary*; `Phase 2 → Dashboard` fills them. The artifact's hard-coded numbers do not become fixtures here.
- Any log CRUD, quick-add form, or table.
- Real API endpoints beyond the single meta endpoint that proves the codegen loop.
- Authentication (Phase 5).
- The Excel/CSV export and any charting (Phase 5 and §8 respectively).
- Re-porting future design-project output. Per the pipeline decision, the artifact is a one-time seed and React becomes the source of truth; no sync machinery is built.
- Mobile-native shells. Responsive web only — README §1's "fast data entry from a phone" is a browser on a phone.

## Expected Deliverable

1. `npm run dev` serves an app at the field-manual identity: the dossier header, section rules, and panel chrome from the artifact render as React components with Oswald/Inter/JetBrains Mono loading from inlined faces under a strict CSP.
2. The theme toggle switches light and dark and persists across reload; with no stored preference the app follows `prefers-color-scheme`. A greyscale screenshot of a status row still distinguishes overdue from OK, because the stripe and mono label carry the state.
3. `npm run gen:api` regenerates types from the running API's OpenAPI document, and CI fails if the committed types are stale. A page fetches the meta endpoint through TanStack Query and renders its response with generated types, proving the whole loop.
