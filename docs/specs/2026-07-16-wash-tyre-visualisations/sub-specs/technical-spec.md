# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-wash-tyre-visualisations/spec.md

## Technical Requirements

### Nothing new is computed

- Both features read figures the screens already hold. `WashPage.tsx` derives `sinceLast`
  (`daysBetween(last.washDate, today)`), `averageGap`, and the target constants `TARGET_MIN = 21` /
  `TARGET_MAX = 28`. `TyresPage.tsx` holds every per-corner pressure and tread on `TyreReading`, the `CORNERS`
  array, `LEGAL_TREAD = 1.6`, and already computes `lowest` tread. **No calculator, no endpoint, no summary
  field changes.** If a reviewer finds themselves adding a number to `IDerivedMetricsService` for this spec,
  the spec has been misread.
- The five-defect fixture is untouched, because no domain arithmetic is added — the same guarantee the
  service-history spec made.

### Wash cadence bar

- A presentational component (e.g. `CadenceBar`) taking the numbers already in scope: `sinceLast`, `TARGET_MIN`,
  `TARGET_MAX`. It renders in the `Cadence` section that today holds only the `Kv` stats, above or beside them —
  the stats stay; the bar is the picture of the same figures.
- Geometry, following `wash.dc.html`'s `cad-track`: a track scaled to a sensible ceiling (the design runs the
  scale to ~day 28+; pick a ceiling a little past `TARGET_MAX` so an overdue marker still lands on the track and
  does not clip), a filled portion to `sinceLast`, a highlighted band spanning `TARGET_MIN`–`TARGET_MAX`, a
  "today · day N" marker at `sinceLast`, and a scale labelled "day 21 — window opens · day 28 — overdue". All
  CSS/HTML; no SVG.
- **Empty and one-wash states.** No bar before the first wash (there is no `sinceLast`). The design cannot show
  this because it has washes frozen in; the app must, the same way `WashPage` already guards `averageGap` as
  "needs a second wash". The bar wants only `sinceLast`, which one wash does provide, so a single wash may show
  the bar but the average-gap copy stays honest about cadence needing a second.
- **Status pill.** OK while `sinceLast <= TARGET_MAX`, Overdue past it — the *same* predicate the existing note
  uses (`sinceLast > TARGET_MAX` → "over the 28-day target"), lifted to one place so the pill and the note
  cannot drift. Due axis only: green OK, amber approaching, rust overdue — never blue, which is the integrity
  axis (`lib/status.ts`). Reuse the existing `<Pill tone="due">`/status primitives rather than a bespoke colour.

### Tyre corner diagram

- A presentational component (e.g. `TyreDiagram`) taking the latest `TyreReading` — `data?.at(-1)`, already
  computed as `latest`. It renders **above** the existing `<DataTable>`, which stays. The diagram is the latest
  reading shaped like a car; the table remains the history.
- Layout from `tyres.dc.html`: a CSS grid car-body with four corner cards (`CORNERS`: front-left, front-right,
  rear-left, rear-right) each showing pressure and tread, and a full-width spare card below. Reuse the
  `corner(v, unit)` helper's rule — `null` renders `Absent`, never a zero, because "not measured is not flat".
- **The asymmetry is the point.** Five pressures, four treads. Corner cards show both; the spare card shows a
  pressure (`psiSpare`) and states it has *no* tread target — there is no `treadSpare` field and there must not
  appear to be one. When `psiSpare === null` the spare card reads "never logged", matching the table's
  `Absent>never</Absent>` and the reason BT53's old dashboard counts 17 of 18 checks.
- **Do not reuse the design's blue for the spare.** `tyres.dc.html` styles the spare card with `--info`/blue,
  but blue is this codebase's integrity axis and using it here would misread "unlogged spare" as a data-integrity
  flag. The spare's unlogged state is an absence on the due/maintenance axis; give it a neutral or muted
  treatment, not `--blue`/`--info`.
- **Tread warn.** A corner whose tread is at or below a named threshold approaching `LEGAL_TREAD` (1.6 mm) gets a
  due-axis warn tone and a short note (the design's "MOT advisory" register). The exact threshold is a *display*
  choice, not domain arithmetic — name it as a constant beside `LEGAL_TREAD` (the widely-cited replacement point
  is 3 mm, well above the 1.6 mm legal floor) and comment why the number was chosen. Below `LEGAL_TREAD` the
  tyre is illegal, not merely worn — a stronger tone than warn.
- **Empty state.** No diagram before the first reading; the existing "No tyre readings yet" panel stands.

### Screens and accessibility

- Every new component is swept by axe or exempted with a reason in `src/CarTracker.WebApp/src/test/coverage.test.ts`,
  the same guard the other screens honour. `CadenceBar` and `TyreDiagram` are rendered by their pages and can be
  swept with them (like `FuelPanel`/`AttentionPanel`), or listed with a reason.
- The plate comes from `usePlate()`, which both pages already call — no `plate={reg}` regression, which
  `coverage.test.ts` fails on.
- The bar and diagram must stay legible in greyscale: the target window, the marker and the warn state cannot be
  colour-only. A position on the track and a text label ("today · day N", "MOT advisory") carry the meaning;
  colour is the second channel, as everywhere else in the app.

## Verification

- Component tests: the cadence bar places the marker at `sinceLast` and the window at 21–28; the pill reads OK at
  day 14 and Overdue at day 30 (the boundary case, `> 28`). No bar with zero washes.
- Component tests: the tyre diagram renders four corners plus a spare; `psiSpare === null` shows "never logged";
  a corner at 1.5 mm tread shows the illegal tone and one at 2.5 mm the warn tone (against whatever threshold is
  chosen), a corner at 6 mm neither.
- Axe sweep green for both, plate via `usePlate()`.
- Live on BT53: enter a tyre reading with the spare left blank and one rear tread near the limit, and confirm the
  diagram shows four corners, a "never logged" spare, and a warn on that rear; log a wash and confirm the bar and
  pill move — the exact pictures `wash.dc.html` and `tyres.dc.html` depict, over the real data.

## External Dependencies (Conditional)

None. Both visualisations are CSS/HTML over React state the two screens already compute; no library, no endpoint,
no schema.
