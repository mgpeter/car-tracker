# Product Mission

## Pitch

Car Tracker is a self-hosted vehicle maintenance and cost-tracking application that helps a hands-on car owner keep their vehicles alive and affordable — one ageing Land Rover today, a garage of them tomorrow — by computing every figure live from the underlying logs and exposing that same domain to an AI assistant over MCP.

## Users

### Primary Customers

- **The owner-operator**: A single person who maintains their own vehicle, does much of the work themselves, and currently tracks everything in a spreadsheet that has drifted out of sync with reality.
- **The AI assistant (via MCP)**: A first-class consumer, not an afterthought. Reads live data and logs entries conversationally, so the phone-in-the-driveway case does not require a form.

### User Personas

**Owner-Mechanic** (30-55 years old)

- **Role:** Private vehicle owner, DIY maintainer
- **Context:** Runs a 2003 Land Rover Freelander 1 (BT53 AKJ, 1.8 K-series) bought at 76,632 mi on 14 Mar 2026. Two known frailties — the K-series head gasket and the VCU — mean early warning matters more than record-keeping for its own sake. Maintenance is tracked today in a 13-sheet Excel workbook.
- **Pain Points:** Derived figures in the spreadsheet go stale silently and are wrong today; data entry at the pump or on the drive is slow enough to get skipped; nothing flags a renewal until it is looked for.
- **Goals:** Know what needs attention without hunting for it; log a fill-up in seconds from a phone; trust the numbers.

## The Problem

### Stored derived values go stale and lie

The spreadsheet stores computed figures rather than deriving them, and four are provably wrong as of 2026-07-14. The MOT countdown reads 6 Aug 2026 (23 days) when the real expiry is 8 Jul 2027 (359 days) — a red warning for a renewal already done. Total litres pumped reads 1,112.94 against an actual 556.47, exactly 2.0000x, because the summary double-counts all 13 fills; anything downstream, such as range-per-tank, is out by half.

**Our Solution:** Every derived number is computed server-side on read from the log rows, never stored.

### The same number disagrees with itself across surfaces

Fuel YTD reads £725.70 on the Dashboard against £888.86 in the Fuel Log — a £163.16 gap caused by the Expenses Log carrying one lumped "fuel to date" row instead of per-fill entries. Current mileage is recorded manually as 80,705 while the latest logged reading is 80,712, so "miles since purchase" is computed from a figure the logs already superseded.

**Our Solution:** One derived-metrics service that both the web API and the MCP server call, so a metric cannot disagree with itself; fuel fills auto-mirror into expenses so the two can never diverge.

### Bad data is accepted silently

A Service History row dated 27 Jun 2026 logs 83,000 mi, above the current 80,712 — mileage is not monotonic, and the figure is likely 80,300 mistyped. The sheet accepts it without comment. Separately, one of 18 regular checks ("Spare tyre pressure") has never been logged and simply falls out of the OK/due-soon/overdue counts rather than surfacing as unknown.

**Our Solution:** Validate mileage monotonicity and flag anomalies rather than accepting them; treat never-logged as a real fourth state, not an absence.

### Data entry is too slow to actually happen

Logging a fill-up means opening a workbook on a laptop, finding the right sheet, and typing into the correct row. At a forecourt with a phone in one hand, this does not get done, and a missed fill corrupts the MPG figures either side of it.

**Our Solution:** Phone-first quick-add forms that compute MPG on the fly and warn on outliers, plus MCP write tools so the entry can be spoken to an assistant instead.

## Differentiators

### The assistant reads the live domain, not an export

Unlike bolting a chatbot onto a database or feeding an LLM a stale CSV, the MCP server is hosted in-process in the same ASP.NET Core app and calls the same domain service as the web UI. This means "what's my MPG" and the Dashboard can never disagree, and the assistant can log a fill-up that is immediately visible in the browser.

### Derived-never-stored is enforced by the architecture

Unlike the spreadsheet it replaces — and unlike most trackers, which cache totals for speed — no derived figure has a column to go stale in. The four defects above are not bugs to fix once but a class of bug the design forecloses. The old Dashboard becomes a test fixture to validate against, never an input.

### Built around two specific failure modes

Unlike generic maintenance trackers, the check schedule is shaped by what actually kills a K-series Freelander: the weekly oil-filler-cap and coolant-colour checks are head-gasket early warning, and the VCU is tracked because prolonged wheelspin can seize it and destroy the IRD. Reference data (OAT coolant spec, tyre pressures, part numbers) is a lookup the assistant can answer at the pump.

## Key Features

### Core Features

- **Garage:** One card per vehicle with lifecycle status (Active / Sold / SORN) and an attention summary; add a car with its checks started empty, from a generic set, or copied from another vehicle (DEC-007).
- **Live Dashboard:** Every figure from the old Dashboard sheet recomputed on read — mileage, renewals with day countdowns, spend rollups, MPG stats, action counts, check status. Per vehicle.
- **Fuel log with on-the-fly MPG:** Computes MPG and L/100km per fill, warns on outliers that suggest a missed fill or mistyped odometer, and auto-mirrors into expenses.
- **Spreadsheet import:** First-run importer reads all 13 sheets so nothing is retyped, recomputing from the logs and validating against the old Dashboard rather than trusting it.
- **Regular checks engine:** Status derived from last log plus interval, with "mark done today" and a batch action for the weekly walk-around.
- **Tasks (DIY + Workshop):** Grouped by status, with a "bundle for next garage visit" view that sums estimated cost, and one-click promotion of a completed task into a service record.
- **Budget and cost-per-mile:** Annual targets per category with YTD actual derived from expenses, variance highlighting, and period toggles.
- **Issues watchlist:** Severity-sorted observations with action-if-worsens, for tracking a symptom over months before it becomes a repair.
- **Documents:** Tagged uploads (V5C, MOT certs, insurance, receipts, condition photos) linked to a service record, expense, or issue.

### Assistant Features

- **MCP read tools:** `get_due_items` as the "what needs my attention" call, plus vehicle summary, fuel status, spend, tasks, issues, and reference lookups.
- **MCP write tools:** Log a fill-up, expense, wash, tyre reading, or completed check conversationally — guarded by a separate read-write token, with every write audited as `source = "mcp"`.
