# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-14-xlsx-importer/spec.md

## Technical Requirements

### Placement and invocation

- Importer lives in `src/CarTracker.Data/Import`. It depends on the entity model and nothing else — no domain, no API.
- Invoked as a CLI verb against a file path, not a startup hook. A first-run hosted service that silently imports on boot is the wrong shape for an operation this consequential; it should be run deliberately and its output read.
- Runs inside a single transaction. Either the whole workbook lands or none of it does — a half-imported database has no correct interpretation.

### Sheet mapping

Twelve sheets import. The Dashboard sheet is **read only by the test harness**, never by the importer.

| Sheet | Target |
|---|---|
| Vehicle Info | `vehicles` + owned blocks, created `status = 'Active'`; if it is the first vehicle in the database, `is_default = true` (DEC-007). The manual current mileage becomes a `mileage_readings` row with `origin = 'manual'` |
| Expenses Log | `expense_entries`; side-column category list reconciled against the seeded `expense_categories` |
| Fuel Log | `fuel_entries`, each mirrored to an `expense_entry` and a `mileage_readings` row with `origin = 'fuel'` |
| Service History | `service_records` + `mileage_readings` (`origin = 'service'`); side-column garage list → `garages` |
| DIY To-Do | `maintenance_tasks` with `kind = 'DIY'` |
| Workshop To-Do | `maintenance_tasks` with `kind = 'Workshop'` |
| Regular Checks | `check_definitions` + `check_logs` |
| Wash Log | `wash_entries` + `mileage_readings` (`origin = 'wash'`); side-column location list → `wash_locations` |
| Tyre Log | `tyre_readings` + `mileage_readings` (`origin = 'tyre'`) |
| Budget | `budget_categories` |
| Issues Watchlist | `issues` |
| Equipment | `equipment_items` |
| **Dashboard** | **Not imported.** Fixture only. |

Every imported row gets `source = 'import'`.

**Reference tables are upserted, not assumed empty** (DEC-007). Garages and wash locations may already exist
from another vehicle's import or from settings edits — a garage found in a side column is matched by name and
reused, never duplicated. The import is scoped to one vehicle; the database around it is not assumed blank.

### Excel serial dates

Every date column across every sheet is a **bare integer**, not a date-formatted cell — ClosedXML will hand back
a number, not a `DateTime`, so automatic conversion cannot be relied on.

- Epoch is 1899-12-30. `DateOnly.FromDateTime(DateTime.FromOADate(serial))` is correct: `FromOADate` uses that
  epoch and already accommodates Excel's phantom 29 Feb 1900.
- Anchor test: `46217` must yield `2026-07-14`. This single assertion catches an off-by-one epoch, the most
  likely failure mode.
- Reject serials below `36526` (2000-01-01) or above `54789` (2050-01-01) as unparseable rather than importing a
  1904 date from a mistyped cell.
- Date columns become `date`, never `timestamptz`. A fill-up happened on a day, not at an instant.

### Trailing blank rows

The Expenses Log carries roughly 30 trailing blank rows holding a running-total formula. ClosedXML's
`LastRowUsed()` counts them as used, because a formula is content.

- Filter on a **populated date cell**, not on `LastRowUsed()` and not on emptiness of the row.
- A row with a running total but no date is not an expense; skip silently, no anomaly. It is expected structure,
  not bad data.
- Apply the same populated-date filter to every sheet. Only Expenses is known to have the problem; the others
  are unverified, and the filter costs nothing.

### The lumped fuel expense row

The Expenses Log carries **one lumped "fuel to date" row of £725.70** instead of per-fill entries. This is the
£163.16 gap against the Fuel Log's real £888.86.

- Detect it: an expense with `category = 'Fuel'` whose amount materially exceeds any plausible single fill and
  whose description matches a to-date pattern.
- **Skip it.** Do not import it.
- Flag it as `SupersededByMirror` with its amount recorded, so the decision is visible rather than silent.
- Fuel expense rows come exclusively from mirroring the 13 `fuel_entries`, totalling £888.86.

Importing both the lumped row and the mirrors would double-count fuel — the same defect class as the litres
figure, reintroduced by the migration meant to fix it. Importing the lumped row *instead* of mirroring would
preserve the £163.16 gap forever.

Detection is heuristic and must not be silent: if zero or more than one candidate is found, abort the import
with a message rather than guessing. This row is known to exist; not finding exactly one means the workbook is
not what this importer was written against.

### Mirroring fuel to expenses

Each `fuel_entry` produces one `expense_entry` with `category = 'Fuel'`, the fill's date, `amount = total_cost`
(the receipt figure, not `litres × price`), `vendor = station`, the fill's mileage, and `fuel_entry_id` set.

This is README §3.2's auto-mirroring applied retroactively. After import, fuel YTD computed from
`expense_entries` and fuel spend computed from `fuel_entries` return the same number *because they are the same
rows*, not because two code paths agree.

### Mileage readings are generated, not transcribed

The workbook has no mileage log — it is the entity that decouples current mileage from any single sheet.
Generate one `mileage_readings` row per log row carrying a mileage, tagged with its `origin`, plus one
`origin = 'manual'` row for Vehicle Info's stated 80,705.

The 83,000 mi service row generates a mileage reading of 83,000. It is wrong, it is flagged, and it imports.
The derived-metrics service decides what "current mileage" means in its presence — that is not the importer's
call.

De-duplicate identical `(date, mileage, origin)` triples; a fill and a wash on the same day at the same
odometer are one reading, not two.

### Anomaly detection

Anomalies are data, not console output. They go in a table, queryable after the fact.

| Kind | Trigger |
|---|---|
| `MileageNonMonotonic` | A reading whose mileage is below an earlier-dated reading. Catches the 83,000 mi row. |
| `FuelCostDiscrepancy` | `abs(total_cost - litres * price_per_litre) > 0.02`. Forecourt rounding is ~1p; more suggests a transcription error. |
| `SupersededByMirror` | The lumped fuel expense row, skipped in favour of per-fill mirrors. |
| `ImplausibleMpg` | A computed interval MPG outside 10–70 for this vehicle. Usually a missed fill or a mistyped odometer, not a real economy figure. |
| `UnparseableValue` | A cell that could not be read as its column type. Row imports with the field null. |
| `MissingReference` | A garage, category, or wash location referenced by a row but absent from the side-column list. Added to the reference table and flagged. |

`ImplausibleMpg` requires computing MPG, which belongs to the derived-metrics service. The importer therefore
depends on that service *for validation only* — it never stores what it computes. If the two specs are built in
roadmap order, this check lands last; sequence it so, rather than duplicating the MPG formula here.

**A never-logged check is not an anomaly.** *Spare tyre pressure* has 18-row presence and zero logs; that is a
legitimate state the Dashboard's 17-count loses. It is reported in the summary as a count, not flagged as a
defect.

### Import run tracking

Every run writes an `import_runs` row: file name, SHA-256 of the file, started/finished timestamps, per-sheet
counts, anomaly count, outcome.

- Refuse to run if a vehicle with the workbook's registration already exists, unless `--force` is passed
  (matched via the normalised unique index, so `BT53AKJ` and `bt53 akj` both count). Per-vehicle, not
  whole-database (DEC-007): a second car's workbook imports into a garage that already has one. The default
  still makes a second accidental run of the *same* workbook impossible.
- The file hash makes "did I import the version with the corrected row?" answerable later.

### Console summary

On completion, print per sheet: rows read, rows imported, rows skipped, anomalies. Then the four validation
figures with their expected values, so a human sees immediately whether the recompute landed. A silent success
is not a success — the whole point is that the numbers changed.

## External Dependencies (Conditional)

- **ClosedXML** (0.104+) - Read `.xlsx` sheets, rows, and cells.
  - **Justification:** MIT licensed and ergonomic for named-sheet and cell-range access. EPPlus is rejected —
    it moved to a commercial licence at v5 and this project has no licence. ExcelDataReader is lighter but
    read-only, and Phase 5 requires Excel *export*; ClosedXML serves both, so adopting it here avoids a second
    Excel library later.
