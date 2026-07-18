# Guide screenshots

`USER-GUIDE.md` references the images below. They are **not committed yet** — capture them from the running app
at a phone width (≈390 px wide, or a browser window narrowed until the bottom nav bar appears) and drop them in
here with the exact filenames. The guide's `![…](screenshots/<name>.png)` links will then resolve.

> **Why they aren't already here:** these were reviewed live in a headless browser whose screenshots don't land
> on the filesystem, so they couldn't be saved into the repo automatically. On a real phone or a narrowed
> desktop Chrome, each is a two-second capture.

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

### Optional extras (nice to have; not referenced by the guide yet)

- `insurance-edit.png` — the Insurance edit sheet **preloaded** with insurer/policy/premium (shows the "values,
  not placeholders" fix).
- `check-definitions.png` — the Check definitions list as mobile cards with **Cadence / Interval days / Active**
  labels.
- `renewals.png` — the dashboard **Renewals** rows on mobile, countdown stacked over its status pill.

Add any of these and wire them into the guide with a `![…](screenshots/<name>.png)` line if you'd like them
inline.
