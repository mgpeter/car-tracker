# Spec Requirements Document

> Spec: Dashboard Derived Extras — tank range, service-interval templates, units toggle
> Created: 2026-07-16
> Status: Complete

## Overview

Ship three small, long-tail items from README §8 and the settings design that share a theme — a derived figure
or a display preference, none of them a new stored number. Estimated full-tank range on the dashboard,
service-interval templates that suggest a record's next-due, and an MPG↔L/100km units toggle. Two are pure
presentation over figures the domain already computes; the one arithmetic addition (range) rests on a single
nullable vehicle field and refuses to invent a value when that field is empty, exactly as the rest of the app
refuses to invent `milesPerDay` on the day of purchase.

## User Stories

### How far on a tank

As the owner, I want an estimated full-tank range on the dashboard, so that "roughly how far does a fill take
me" is answered from my own MPG rather than a brochure figure.

README §8 asks for "Estimated range on current tank surfaced on the dashboard, not just via MCP", and the MCP
`get_fuel_status` tool is specced to report it too. The honest figure this app can give is **full-tank range** —
average MPG × tank capacity — because tank *level* is not tracked: the fuel-basis spec deliberately made
`FillLevel` descriptive and removed tank level as load-bearing, so "remaining on the current tank" is unknowable
and would be a guess dressed as a reading. Full-tank range needs a real tank size; BT53's Freelander is ~59 L.
Rather than hardcode 59 L and present a guess in the same font as the derived figures, this adds one nullable
tank-capacity field to the vehicle and shows nothing when it is empty — the same restraint as a null
`milesPerDay`.

### Next due, suggested not typed

As the owner, I want the service add sheet to suggest a next-due date and mileage from the service type, so that
an oil change does not need me to remember "twelve months, twelve thousand miles" every time.

README §8: "Service-interval templates so 'next due' is suggested automatically." Today `ServiceRecord.NextDueDate`
and `NextDueMileage` are typed by hand, and the MOT expiry the dashboard derives depends on that hand-typed
`NextDueDate`. A small template map keyed on recognised service types pre-fills those two fields in the add sheet
— a suggestion the owner can accept or overwrite, never an automatic write behind their back. The record still
stores the actual figures; the template only saves the typing.

### The units I read in

As the owner, I want to switch fuel economy between MPG and L/100 km, so that the display speaks the unit I think
in without changing a single stored number.

`settings.dc.html`'s Appearance section offers exactly this, with the toast "Display switches to L/100 km — 28.7
MPG renders as 9.8". It is display-only by construction: every `FuelEntryMetrics` already carries both `Mpg` and
`LitresPer100Km`, computed server-side, so `28.7 MPG ≡ 9.8 L/100 km` already holds — the toggle picks which
derived value to render. The preference lives in localStorage beside the theme (`theme.ts`), touches no backend,
and recomputes nothing.

## Spec Scope

1. **Tank-capacity field** — one nullable `FuelTankCapacityLitres` on the vehicle's `FluidSpecs` owned block,
   beside `OilCapacityLitres`; BT53's ~59 L entered as a real spec, not defaulted.
2. **Estimated full-tank range (derived)** — average MPG × tank capacity, computed in the shared brain, surfaced
   on the dashboard fuel panel, and null (shown as nothing) when capacity or average MPG is absent.
3. **Service-interval templates** — a constant map of type → interval (months/miles) that pre-fills the add
   sheet's next-due fields as an overridable suggestion, no auto-write.
4. **Fuel-economy units toggle** — an MPG↔L/100 km display preference in localStorage (theme pattern), flipping
   which already-computed value the fuel surfaces render, including the chart series and its derived label.
5. **Placement** — range on the dashboard `FuelPanel`, templates on the service add sheet, the toggle in
   Settings' Appearance section with the fuel displays honouring it.

## Out of Scope

- **"Remaining on the current tank" as a live figure.** Tank level is not tracked — the fuel-basis spec made
  `FillLevel` descriptive on purpose — so a remaining-range number would be invented. Full-tank range is the
  honest thing the data supports, and it is labelled as such. Restoring tank-level tracking to compute remaining
  is a much larger change than §8 asks for.
- **A guessed tank size when the field is empty.** The app shows "—" rather than fabricate `milesPerDay` or a
  one-fill MPG; a hardcoded 59 L fallback would put a guess in the same typeface as the derived figures. No
  capacity, no range.
- **A per-vehicle editable service-interval table.** Constants ship the value now; a settings-managed,
  per-vehicle interval schedule is more than §8's "suggested automatically" and would be its own Settings spec
  with its own schema.
- **The units toggle as a stored/server preference or a currency/multi-unit system.** It is a client display
  preference like the theme, single-user and self-hosted; a server-persisted or multi-tenant preference store is
  not wanted (README §6 does not want a login flow), and currency stays GBP.
- **Recomputing or re-storing any fuel figure for L/100 km.** Both units are already derived per fill; the toggle
  is a render choice, not a migration or a recompute.

## Expected Deliverable

1. Entering BT53's ~59 L tank capacity makes the dashboard fuel panel show an estimated full-tank range from its
   ~29 MPG average (~380 miles), labelled as a full-tank estimate; clearing the field makes the range disappear
   rather than fall back to a guess — with no stored range anywhere.
2. In the service add sheet, choosing a recognised type (e.g. oil service) pre-fills next-due date and mileage
   from the template, and those suggestions can be overwritten before saving; the stored record holds whatever
   was saved, not the template.
3. Toggling units in Settings switches every fuel display — panel, log, and the chart with its derived label —
   from 28.7 MPG to 9.8 L/100 km and back, persists across reloads via localStorage, and changes no stored
   value.
