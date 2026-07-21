# Guide screenshots

`USER-GUIDE.md` references the images below, and they are **all captured and committed**. Each is a **dark-theme,
phone-width (390 px)** frame of the live app running the BT53 AKJ demo data, taken with Chrome DevTools at a 2×
device-pixel ratio. The guide's `![…](screenshots/<name>.png)` links resolve.

> **To re-capture** (after a design change, or at a different width): run the app (`dotnet run --project
> src/CarTracker.AppHost`), open it at ≈390 px wide until the bottom nav bar appears, and save each frame over
> the matching filename below. All eleven are referenced inline by the guide.

| File | Screen | What to capture |
|---|---|---|
| `dashboard.png` | `/{reg}/dashboard` | Top of the dashboard: the dossier (plate, odometer drum) down to the start of **Needs attention**, with the bottom nav bar showing the centre **status glyph** (green ✓ when all clear). |
| `more-sheet.png` | any screen → **More** | The **All Screens** sheet open, showing the grouped tiles (Daily / Records / Watch & plan / Reference) — note the tiles are now even height. |
| `fuel-cards.png` | `/{reg}/fuel` | Scrolled to the **Fills** list so 2–3 fuel **cards** are visible, each with its labelled lines (Odometer, Litres, £/L, Total, Station, Fill, MPG) and the bottom nav's **＋** FAB. |
| `edit-fill.png` | `/{reg}/fuel` → tap a row | The **Edit fill** sheet open, fields **preloaded** with the fill's values, and the footer showing **Delete** + **Save changes**. (Optionally arm Delete to show the two-line "Confirm delete — with its expense & reading".) |
| `checks.png` | `/{reg}/checks` | The checks list with statuses and the **Log** actions; bottom-nav tell-tale visible. |
| `service-history.png` | `/{reg}/service` | The service **records** as cards, ideally including the **MOT** record that drives the dashboard countdown. |
| `budget.png` | `/{reg}/budget` | **Against target** with the period toggle (This year / **Last 12 months** / Since purchase) and the summary tiles. |
| `settings-statutory.png` | `/{reg}/settings` | The **Statutory & policies** panel: the derived read-only MOT row, plus Road tax and Insurance rows with **Edit**. |

### Extras (also captured and now referenced inline)

- `renewals.png` — the dashboard **Renewals** rows on mobile, countdown stacked over its status pill (§2).
- `insurance-edit.png` — the Insurance edit sheet **preloaded** with insurer/policy/premium, showing the
  "values, not placeholders" fix (§10).
- `check-definitions.png` — the Check definitions list as mobile cards with **Cadence / Interval days / Active**
  labels (§5).
