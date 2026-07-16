# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-green-lane-trips/spec.md

## Technical Requirements

### Scope this to (a), the outing log — the rest is deferred

This spec is deliberately staged (see the spec's Scope). The technical work below is **v1 = the outing log
only**; the route reference (b) and the map (c) are noted where they would attach but are not built here. This is
net-new scope outside README §1–§8 and should not grow past (a) without the DEC the spec calls for.

### The `Trip` entity and its mileage reading

- A new `Trip` entity, vehicle-scoped like every other (DEC-007): `VehicleId`, `TripDate`, `Place`/`RouteName`,
  `Terrain` (a small enum or free text — green lane / byway / pay-and-play, mirroring the manual's R1–R4
  categories), `Difficulty` (1–3, the manual's bars), an optional `EndMileage`, `Notes`, and the usual
  `IAuditable` stamps (`CreatedAt`, `UpdatedAt`, `Source`). Explicit `IEntityTypeConfiguration<Trip>`, column
  types specified — the project's convention.
- **On save, if `EndMileage` is given, write a `MileageReading`** dated `TripDate` from that odometer. The
  mileage log is the single source of current mileage (`MileageReading.cs`: "current mileage is derived from
  here — never from a manual field"), so an outing's odometer must land there to move any derived figure. Not
  monotonic-constrained — the log deliberately accepts and flags anomalies (the 83,000 mi row), and an outing is
  no exception; it runs the same `AnomalyScanner` the other write paths run.
- **`MileageOrigin` for an outing.** The enum is `Manual/Fuel/Tyre/Wash/Service/Purchase`. An outing fits none
  cleanly. Two honest choices, to settle in the DEC/task 1: **add `MileageOrigin.Trip`** (truthful about where
  the reading came from — the enum exists precisely to record which log produced a reading), or **reuse
  `Manual`** (no schema churn, but loses the provenance the enum is for). Recommendation: add `Trip` — the enum
  already distinguishes `Purchase` from `Manual` for exactly this "where did this reading come from" reason, and
  an outing reading correlating with hard use is worth being able to identify.

### The maintenance loop — prompt, never act

- On save, surface two suggested follow-ups drawn from the manual's aftercare: **a wash reset** ("rinse
  underside + arches" — the manual's R1/R3 footer) and **a coolant/oil recheck** (the K-series head-gasket
  checks). These are *prompts*: the owner confirms them into a `WashEntry` and a coolant `CheckLog`
  respectively, or dismisses them. The app **does not auto-log** the wash or the check — consistent with the
  anomaly lifecycle's and the head-gasket watch's "flag, never act for the owner".
- The coolant/oil checks are `CheckDefinition`s on the vehicle (the weekly oil-filler-cap and coolant-colour
  checks the head-gasket watch is built on). The prompt deep-links to logging them; it does not invent a second
  logging path. If those definitions don't exist on the vehicle, the prompt degrades to advice text.
- This is the feature's whole justification: without the prompts it is a diary; with them it ties the drive to
  the maintenance the drive creates. If the loop is cut for effort, reconsider whether the feature earns its
  place at all.

### Screens

- A new **Trips screen** (`TripsPage.tsx`) under `src/CarTracker.WebApp/src/screens/` — a list of outings with
  difficulty (the manual's 1–3 bars), terrain and miles, and an add-outing sheet. Reuse the design language: the
  green-lane visual identity *is* this app's identity, so the route-card treatment (difficulty bars, the
  eyebrow, the aftercare footer) maps directly onto the tokens already in `theme.css`. Use `--green` for OK,
  keep the status axis separate from the orange structural accent, per the design notes.
- The add sheet's save surfaces the two maintenance prompts inline. `usePlate()` handles the plate; every new
  component axe-swept or exempted (`coverage.test.ts`).
- **(b), deferred:** a read-only route reference would be static content (R1–R4 with difficulty/season/legal
  status) — no new entity, a content screen. **(c), deferred:** a map needs self-hosted Leaflet tiles or a
  static image because the CSP blocks the CDN Leaflet the manual uses (the fonts were inlined for the same
  reason). Neither is built in v1.

### API

- Endpoints follow the existing pattern (`ExpenseEndpoints.cs`, `MileageEndpoints.cs`): `POST /api/trips`,
  `GET /api/trips` (per vehicle), edit/delete as the log screens have. The create runs the mileage-reading write
  and the anomaly scan in one path, and returns the created trip plus whatever the maintenance prompts need. New
  endpoints regenerate the OpenAPI contract and TS types; staleness gate green.

### Interactions to preserve

- **No auto-log of wash or checks.** The outing prompts; the owner acts. Silently logging a wash the owner
  didn't do would corrupt the wash cadence the maintenance loop depends on.
- **The mileage reading is real and flagged like any other** — an outing's odometer below the current reading
  raises the same anomaly, never a silent accept or reject.
- Deleting a trip: decide (task 1) whether the mileage reading it wrote is cascaded or kept. Recommendation:
  **keep the reading** (an observation of the odometer is true regardless of why it was logged), matching the
  Documents "the certificate outlives the row" principle — but this is a judgement call to record.

## Verification

- **Outing writes a reading**: creating a trip with an end odometer produces one `Trip` and one
  `MileageReading` (origin `Trip` or `Manual` per the decision) dated the outing; a trip with no odometer writes
  no reading.
- **Prompts, not writes**: saving a trip surfaces the wash-reset and coolant-recheck prompts and logs *neither*
  until the owner confirms — assert no `WashEntry` or `CheckLog` appears from the save alone.
- **Anomaly parity**: an outing odometer below current raises the same flag the other write paths raise (the
  `AnomalyScanner`), no new arithmetic.
- **Live on BT53**: log a plausible Surrey outing (an R2/R3-style byway day), confirm the mileage moves, the
  prompts appear, and confirming them logs the wash and the coolant check through the normal paths.

## External Dependencies (Conditional)

- **None for v1 (the outing log).** It reuses `MileageReading`, `WashEntry`, `CheckLog` and the existing write
  patterns; the only schema is the new `Trip` table (see `sub-specs/database-schema.md`).
- **(c), deferred only:** a map would need self-hosted map tiles or a static-map asset (the CDN Leaflet the
  manual uses is CSP-blocked) — an external dependency that arrives only if (c) is ever built, not in v1.
