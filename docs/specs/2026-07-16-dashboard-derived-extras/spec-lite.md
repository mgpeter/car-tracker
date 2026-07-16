# Spec Summary (Lite)

Three small §8 / settings-design items that share a theme — a derived figure or a display preference, none a new
stored number. **Estimated full-tank range** on the dashboard (average MPG × tank capacity — *full-tank*, not
"remaining", because tank level is deliberately not tracked), resting on one nullable `FuelTankCapacityLitres`
field beside `OilCapacityLitres` and showing nothing when it is empty rather than defaulting to a guessed 59 L.
**Service-interval templates** — a constant type→interval map that pre-fills the add sheet's next-due fields as
an overridable suggestion, no schema, no auto-write. **A fuel-economy units toggle** (MPG↔L/100 km) — display-only,
stored in localStorage like the theme, flipping which already-computed value the fuel surfaces render, because
every `FuelEntryMetrics` already carries both `Mpg` and `LitresPer100Km`.

The only schema touch is the single nullable tank-capacity column; templates are constants and the toggle is a
client preference.
