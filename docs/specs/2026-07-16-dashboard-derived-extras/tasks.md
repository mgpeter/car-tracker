# Spec Tasks

## Tasks

- [x] 1. Tank-capacity field and derived full-tank range
  - [x] 1.1 Write domain tests: range = `AverageMpg` × capacity-in-gallons; null when capacity unset; null when `AverageMpg` is null (one fill / none)
  - [x] 1.2 `FuelTankCapacityLitres` on `FluidSpecs` + `VehicleConfiguration` mapping; migration `AddFuelTankCapacity`
  - [x] 1.3 Compute full-tank range in `DerivedMetrics.Compute` (litres→imperial gallons via `Units`), added to the summary as a nullable decimal
  - [x] 1.4 Regenerate the contract and TS types; staleness gate green
  - [x] 1.5 Verify all tests pass

- [x] 2. Surface the range on the dashboard
  - [x] 2.1 Write tests: `FuelPanel` shows a full-tank range with capacity set, and nothing (not a guess) when unset or when average MPG is null
  - [x] 2.2 Render the range on `FuelPanel.tsx`, labelled as a full-tank estimate — not "remaining"
  - [x] 2.3 Vehicle edit path accepts the tank capacity; axe sweep + coverage-guard exemption; plate via `usePlate()`
  - [x] 2.4 Verify all tests pass

- [x] 3. Service-interval templates
  - [x] 3.1 Write tests: a recognised type pre-fills next-due date and mileage; the suggestion is overridable and the saved record holds the saved values, not the template
  - [x] 3.2 A constant type→interval map (months/miles), keyed on the same canonical types the add sheet offers as choices
  - [x] 3.3 Pre-fill `NextDueDate`/`NextDueMileage` in the service add sheet from the chosen type + entered date/mileage — suggestion only, no auto-write
  - [x] 3.4 Verify all tests pass

- [x] 4. Fuel-economy units toggle
  - [x] 4.1 Write tests: toggle flips panel, log and chart (series + derived label) MPG↔L/100 km and persists across a reload; no stored value changes
  - [x] 4.2 A localStorage preference module on the `theme.ts` pattern (safe read → MPG fallback, no-op store, subscribe)
  - [x] 4.3 Fuel surfaces render `Mpg` or `LitresPer100Km` by preference; chart plots the matching series with an inverted good/bad framing and a matching derived label
  - [x] 4.4 Settings Appearance toggle control + toast; axe sweep + exemption
  - [x] 4.5 Verify all tests pass

- [x] 5. Prove it end to end on BT53
  - [x] 5.1 Enter ~59 L; confirm ~380 mi full-tank range appears from the ~29 MPG average; clear it and confirm the range vanishes rather than guessing
  - [x] 5.2 Add a service with a recognised type; confirm next-due pre-fills and can be overwritten
  - [x] 5.3 Toggle to L/100 km; confirm 28.7 → 9.8 across panel, log and chart, persisting across reload, with no stored value changed
  - [x] 5.4 Full suite, both builds, codegen gate; fixture untouched; update roadmap and CLAUDE.md
