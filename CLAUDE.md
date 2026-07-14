# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## State of play

**There is no code yet.** The whole repo is `README.md`, `.gitignore` (stock GitHub `VisualStudio.gitignore`),
this file, and three inputs under `archive/`. `src/` and `docs/` exist on disk but are empty and untracked —
git does not track empty directories, so a fresh clone won't even have them.

There are therefore **no build, test, or lint commands**. Do not infer them from the stack described below —
none of those projects exist. Add a Commands section here once the solution is actually scaffolded.

`README.md` is not a readme so much as the full specification (§1–§8), and it is the authority on scope. §7
gives the intended build order; start there rather than inventing one.

## What `archive/` is for

These are load-bearing inputs, not historical clutter. Convention: one self-contained HTML per design concept.

- **`ORIGINAL-TRACKER-IN-EXCEL-Freelander_BT53AKJ_Tracker.xlsx`** — the live system this project replaces, and
  the import source for first run (spec §6). 13 sheets: Dashboard, Vehicle Info, Expenses Log, Fuel Log,
  Service History, DIY To-Do, Workshop To-Do, Regular Checks, Wash Log, Tyre Log, Budget, Issues Watchlist,
  Equipment.
- **`Sample-design-and-road-trip-tracking-green-lane-field-manual.html`** — the origin of the visual identity.
  See Design language below.
- **`dashboard-design-idea/dashboard.html`** — a concept for the Dashboard (spec §3.1) that extends that
  identity into the app. Self-contained: Oswald/Inter/JetBrains Mono are inlined as base64 data URIs, no CDN.
  Built on the real imported figures at a reference date of 2026-07-14, and it demonstrates the four import
  flags below as a live "Import check" panel. Useful as a reference for what the derived-metrics service has
  to produce, and for status treatment: severity stripe + uppercase mono label first, colour second, so state
  survives greyscale.

## The central constraint

Spec §1: *every derived number must be computed server-side, never stored stale*. §4 requires one
derived-metrics service that both the web API and the MCP server call, so a metric can never disagree with
itself across surfaces. Multi-vehicle is active scope (DEC-007): the garage is the home screen, every entity
is vehicle-scoped, and vehicles are never seeded — they arrive via import or the add-car flow. Only BT53 AKJ
exists today.

Derived, never stored: current mileage (max/latest `MileageReading`), per-fill MPG and L/100km, fleet MPG
stats, spend rollups, cost-per-mile, days-to-renewal, check status from last log + interval, budget variance.

## Importing the spreadsheet: read the logs, not the Dashboard

The xlsx **Dashboard sheet holds stored derived values, and four of them are provably wrong.** Import from the
log sheets and recompute; treat Dashboard as a fixture to validate against, never as input. All four were
verified against the underlying logs (reference date 2026-07-14):

| Dashboard says | Reality | Cause |
|---|---|---|
| MOT expiry 6 Aug 2026 (23 days) | 8 Jul 2027 (359 days) | Stale. Superseded by the MOT pass logged 8 Jul 2026 at 80,705 mi. Would show a red countdown for a renewal already done. |
| Total litres pumped 1,112.94 | 556.47 | Exactly 2.0000× — the summary double-counts all 13 fills. Anything downstream (range-per-tank) is out by half. |
| — | Service History row dated 27 Jun 2026 logs **83,000 mi**, above the current 80,712 | Mileage is not monotonic. Likely 80,300 mistyped. Spec §5.3 requires flagging this, not silently accepting it. |
| Fuel YTD £725.70 | Fuel Log totals £888.86 | £163.16 gap: Expenses Log carries one lumped "fuel to date" row instead of per-fill entries. Spec §3.2's auto-mirroring of fills into expenses is what closes it. |

Also note **current mileage (manual) 80,705 is behind latest logged 80,712** — the sheet's "miles since
purchase" uses the manual figure. `MileageReading` (spec §2) exists precisely to decouple this; derive from the
latest reading.

Other importer gotchas:

- **Dates are Excel serials, epoch 1899-12-30** (e.g. 46217 = 2026-07-14). Every date column across every
  sheet is a bare integer.
- The Regular Checks sheet has 18 rows but the Dashboard counts 17 — "Spare tyre pressure" has never been
  logged and falls out of the OK/due-soon/overdue buckets. Never-logged is a real fourth state.
- Expenses Log has ~30 trailing blank rows that carry a running-total formula; filter on a populated Date.
- Reference lists (expense categories, wash locations, garages) sit in side columns of their sheets and should
  become seed data per spec §2.

## Design language

`archive/…green-lane-field-manual.html` establishes the identity. Reuse it rather than inventing a second one.

- **Type** — Oswald (display, uppercase, condensed), Inter (body), JetBrains Mono (all data/labels). Use
  `font-variant-numeric: tabular-nums` anywhere digits align.
- **Palette** — `--ink #1E241B`, `--paper #E8E2CF`, `--paper-2 #DFD8BF`, `--panel #F1ECDD`,
  `--green-deep #2F3D2C`, `--green #5E7A34`, `--orange #B85C29`, `--rust #A23B2E`, `--blue #3E6187`,
  `--sand #C9B588`.
- Orange reads as the structural accent (rules, eyebrows, section marks). When building status UI, keep the
  semantic axis separate from it, or the two fight: the dashboard concept uses `--green #5E7A34` OK,
  `#C79A22` due soon (the manual's yellow waymark), `--rust #A23B2E` overdue, and reserves `--blue #3E6187`
  for data-integrity flags, which are a different axis from due-status. §3.1 thresholds: red under 30 days,
  amber under 60.
- The field manual loads Google Fonts and Leaflet from CDNs; the dashboard concept inlines its fonts instead.
  Under a strict CSP the CDN version silently falls back to system faces, which is why the fonts got inlined.
- The manual numbers its sections 01–05 because a document has a reading order. Don't carry that into app UI —
  a dashboard is scanned, not read, and numbering it is decoration posing as information.

## Intended architecture (none of this exists yet)

Per README: .NET 10, PostgreSQL, React (Vite), Aspire, EF Core, Microsoft Agent Framework, docker-compose.
Projects planned under `src/`: `CarTracker.WebApp` (Vite React), `.WebApi`, `.Data` (EF Core model +
migrations), `.ModelContextProtocol`, `.Shared`, `.Domain` (domain logic and derived metrics — the shared
brain), `.AppHost` (Aspire).

The MCP server (§5) is the differentiator, hosted in-process in the same ASP.NET Core app over HTTP/SSE. It
reads the same domain service as the web UI. Two token scopes: read-only and read-write; every write logs
`source = "mcp"`.

## Vehicle facts worth knowing

BT53 AKJ — 2003 Land Rover Freelander 1, 1.8 SE, Rover K-series petrol, manual 5-speed, AWD via viscous
coupling (VCU). Bought 14 Mar 2026 at 76,632 mi. Two known frailties drive much of the spec's design: the
K-series head gasket (the weekly oil-filler-cap and coolant-colour checks are its early-warning system) and the
VCU (prolonged wheelspin can seize it and destroy the IRD/diff). Coolant must be OAT (red/pink) — never mixed
with IAT.
