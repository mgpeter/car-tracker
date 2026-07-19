# Spec Tasks

## Tasks

- [x] 1. Wash cadence bar and status pill
  - [x] 1.1 Write tests: marker at `sinceLast`, window at 21–28, pill OK at day 14 / Overdue at day 30, no bar with zero washes
  - [x] 1.2 `CadenceBar` (CSS/HTML) — elapsed fill, highlighted 21–28 window, "today · day N" marker, scale labels
  - [x] 1.3 Status pill on the due axis (green/amber/rust), reusing the single `sinceLast > TARGET_MAX` predicate so pill and note cannot drift
  - [x] 1.4 Render it in the existing Cadence section alongside the `Kv` stats; guard the empty/one-wash states
  - [x] 1.5 Axe sweep + coverage-guard exemption with a reason; plate via `usePlate()`
  - [x] 1.6 Verify all tests pass

- [x] 2. Tyre corner diagram and spare card
  - [x] 2.1 Write tests: four corners + spare rendered; `psiSpare === null` shows "never logged"; tread at 1.5 mm illegal tone, 2.5 mm warn, 6 mm neither
  - [x] 2.2 `TyreDiagram` (CSS grid car-body) from `latest`, reusing the `corner(v, unit)` "never a zero" rule
  - [x] 2.3 Full-width spare card — pressure only, "no tread target", "never logged" when null; neutral tone, NOT the design's blue (blue is the integrity axis)
  - [x] 2.4 Tread warn/illegal states against a named threshold beside `LEGAL_TREAD`, on the due axis, greyscale-legible
  - [x] 2.5 Render above the unchanged `<DataTable>`; guard the no-readings state
  - [x] 2.6 Axe sweep + coverage-guard exemption with a reason; verify all tests pass

- [x] 3. Prove it end to end on BT53
  - [x] 3.1 Log a wash and confirm the bar fills, the marker moves to day N, and the pill reads OK inside the window
  - [x] 3.2 Enter a tyre reading with the spare left blank and a rear tread near 1.6 mm; confirm four corners, a "never logged" spare, and a warn on that rear
  - [x] 3.3 Full front-end suite, build, axe sweep; update roadmap and CLAUDE.md
