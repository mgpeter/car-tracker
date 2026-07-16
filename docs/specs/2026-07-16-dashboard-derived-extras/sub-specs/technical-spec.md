# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-dashboard-derived-extras/spec.md

## Technical Requirements

### Estimated full-tank range — derived, through the shared brain

- **The figure is full-tank range, not remaining range**, and the distinction is load-bearing. README §5.2 and
  the MCP `get_fuel_status` wording say "estimated range remaining on current tank", but tank *level* is not
  tracked — the fuel-basis spec (`docs/specs/2026-07-15-fuel-basis-initial-mileage/`) made `FuelEntry.FillLevel`
  descriptive and removed tank level as load-bearing, and `NoFillLevelInCalculationsTests` enforces that no
  calculator reads it. So "remaining" is unknowable from the data; the honest, supportable figure is *full-tank*
  range: `AverageMpg` (imperial) × tank capacity in imperial gallons. Label it as a full-tank estimate on the
  panel so it is never mistaken for a live gauge.
- **Computed in `DerivedMetrics.Compute`, never stored** — the §1 rule. It joins the existing `FuelEconomySummary`
  (`AverageMpg`) and the new tank-capacity field, converting litres→imperial gallons with the constant already in
  `Units` (`LitresPerImperialGallon`). Add it to the fuel summary or the identity block as a nullable decimal.
- **Null when it cannot be honestly given.** No range when `AverageMpg` is null (one fill, or none — there is
  nothing to measure from) or when tank capacity is unset. This mirrors `IdentityOf`'s null `MilesPerDay` on the
  day of purchase: "0 miles" and "0 range" are claims, and the app does not make ones it cannot support. The
  `FuelPanel.tsx` empty/near-empty states already model exactly this reasoning and the range follows it.
- **One nullable field**, `FuelTankCapacityLitres`, on the `FluidSpecs` owned block beside `OilCapacityLitres`
  and `CoolantCapacityLitres` — it is a capacity of the same kind, at the same place, mapped the same way. See
  `sub-specs/database-schema.md`. BT53's ~59 L is entered as a real spec through the vehicle edit path; it is not
  seeded and not defaulted.

### Service-interval templates — constants, no schema

- A **constant map** of recognised service `Type` → interval (months and/or miles), e.g. an oil service at 12
  months / 12,000 mi, MOT at 12 months, cambelt at its mileage. Constants first is the recommendation: it ships
  §8's "suggested automatically" now, and a per-vehicle editable schedule (its own schema, its own settings UI)
  is a larger spec that this does not pre-empt.
- The map **pre-fills** the service add sheet's `NextDueDate` and `NextDueMileage` from the chosen type and the
  entered service date/mileage — as an *overridable suggestion*, never an automatic write. The owner sees the
  suggested values in the fields and can change them before saving; the stored `ServiceRecord` holds what was
  saved, not the template.
- `ServiceRecord.Type` is free text and the MOT derivation matches the literal `"MOT"` (per the service-history
  spec). The template map keys on the same recognised values the add sheet already offers as choices, so
  suggestion and MOT-derivation agree on what a type is called — a "mot" or "MOT test" that derives no expiry
  should also match no template, and both failures are the same free-text mismatch, made visible by offering the
  canonical types as choices rather than trusting them typed.
- No domain arithmetic on stored aggregates, so the five-defect fixture is untouched. This is a UX pre-fill.

### Fuel-economy units toggle — localStorage, no backend

- **Display-only.** Every `FuelEntryMetrics` already carries `Mpg` and `LitresPer100Km`, both computed
  server-side, so `28.7 MPG ≡ 9.8 L/100 km` holds already — the toggle chooses which derived value to render. It
  is not a recompute, not a migration, and touches no stored data.
- **Persisted in localStorage like the theme.** Follow `src/CarTracker.WebApp/src/lib/theme.ts`: a small module
  with a storage key (mirroring `THEME_STORAGE_KEY = 'ct-theme'`), a safe read that tolerates a missing/garbled
  value and falls back to MPG, a store that no-ops on a storage exception, and a subscribe so the fuel surfaces
  react to a change. `settings.dc.html` already models the state (`units: 'mpg' | 'l100'`) and the toast
  ("Display switches to L/100 km — 28.7 MPG renders as 9.8").
- **Every fuel display honours it**: `FuelPanel.tsx`, `FuelLogPage.tsx`, and — importantly — the MPG chart. The
  chart already takes a `unit` prop (`Spark`'s `unit = 'MPG'`), but switching units must also switch the *plotted
  series* to `LitresPer100Km` and the *derived* `aria-label` with it — a chart captioned "MPG" while plotting
  L/100 km is exactly the lie `Spark`'s comment warns about. Note that MPG and L/100 km are inverse scales
  (higher MPG is better, lower L/100 km is better), so any "best/worst" and any good/bad framing must invert with
  the unit, not just relabel.
- Settings' Appearance section gains the toggle control; the fuel surfaces read the preference.

### Accessibility and codegen

- Every new/changed component axe-swept or exempted with a reason in
  `src/CarTracker.WebApp/src/test/coverage.test.ts`; plate via `usePlate()`.
- The tank-capacity field changes the vehicle contract, so regenerate: `npm run gen:api` then
  `git diff --exit-code` must be clean once the generated types are committed. The units toggle and the templates
  add no contract surface.

## Verification

- Domain tests: full-tank range = `AverageMpg` × capacity-in-gallons for a known capacity; **null** when capacity
  is unset and when `AverageMpg` is null (one fill / none). On BT53 (~29.19 MPG, 59 L ≈ 12.98 imp gal) the range
  is ~379 miles — assert the arithmetic, not a magic number, and confirm it is null before the tank field is set.
- The five-defect fixture is unchanged: range is a new figure over new input, not a recomputation of an existing
  one, so the workbook regression figures do not move.
- Front-end tests: the units toggle flips panel, log and chart (series + derived label) between MPG and L/100 km
  and persists across a reload; the service add sheet pre-fills next-due from a recognised type and lets it be
  overwritten.
- Live on BT53 against `dotnet run --project src/CarTracker.AppHost`: enter 59 L and see ~380 mi full-tank range
  appear; clear it and see the range vanish rather than fall back to a guess; add a service with a recognised
  type and watch next-due pre-fill; toggle to L/100 km and confirm 28.7 → 9.8 everywhere with no stored value
  changing.

## External Dependencies (Conditional)

None. `Units.LitresPerImperialGallon`, `FuelEconomySummary.AverageMpg`, `FuelEntryMetrics.LitresPer100Km`, the
`FluidSpecs` owned block and the `theme.ts` localStorage pattern all exist; this is one nullable column plus
surfacing and a preference, not acquisition.
