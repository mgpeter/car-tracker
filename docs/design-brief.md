# Design brief — Car Tracker front-end

> Paste everything below the line into Claude Design. **Attach `archive/dashboard-design-idea/dashboard.html`
> to the chat** — it is the reference and the design system lives inside it.
>
> This brief covers 16 screens. Consider running it in three passes (see *Batching* at the end) rather than
> asking for all 16 at once.

---

## What you are designing

**Car Tracker** — a self-hosted maintenance and cost tracker for the cars you own (one today), replacing a
13-sheet Excel workbook. Single user, self-hosted, no marketing surface, no onboarding. It is a working tool
for one person who maintains their own vehicles. See the *Addendum* at the end: the app is multi-vehicle and
the home screen is a garage.

The vehicle is **BT53 AKJ** — a 2003 Land Rover Freelander 1, 1.8 SE Station Wagon, Rover K-series petrol,
manual 5-speed, AWD via viscous coupling (VCU), navy blue. Bought 14 March 2026 at 76,632 miles. Two known
frailties shape the whole product: the K-series head gasket (the weekly oil-filler-cap and coolant-colour checks
are its early-warning system) and the VCU. Coolant must be OAT (red/pink), never IAT.

**The reference date for every screen is 14 July 2026. Current odometer 80,712 mi.**

### The one architectural fact that shapes the UI

Every number in this app is **computed live from the underlying logs, never stored**. The spreadsheet it
replaces stored its derived values and four of them are provably wrong. That is not trivia — it is why the
Dashboard exists in the shape it does, and why the app has a data-integrity axis at all. When a figure is
uncertain, the UI says so rather than showing a confident wrong number.

### The primary use case, above all others

**Fast data entry from a phone.** Standing at a petrol pump, one-handed, logging a fill-up in under 30 seconds.
If a screen is beautiful on desktop and awkward on a phone, it has failed. Design mobile-first and let desktop
be the roomier case.

---

## The design system — inherit it, do not invent one

The attached `dashboard.html` **is** the design system. Read its `<style>` block and reuse it. Do not restyle,
re-palette, or "modernise" it. Every rule below is already implemented there.

### Tokens — two layers, and this matters

There is a raw palette **and** a semantic layer on top of it. Components reference the **semantic layer only**.
This separation is load-bearing: it is what keeps the structural accent from colliding with status colour.

```css
:root{
  --bg:#E8E2CF;  --surface:#F1ECDD;  --surface-2:#DFD8BF;  --fg:#1E241B;
  --muted:rgba(30,36,27,.62);  --faint:rgba(30,36,27,.40);
  --line:rgba(30,36,27,.16);   --line-strong:rgba(30,36,27,.32);

  --head-bg:#2F3D2C;  --head-fg:#EDE9D8;  --head-dim:#C6CDB4;  --sand:#C9B588;

  --accent:#B85C29;          /* structure only — never status */
  --ok:#5E7A34;  --soon:#C79A22;  --due:#A23B2E;  --info:#3E6187;

  --ok-wash:rgba(94,122,52,.12);    --soon-wash:rgba(199,154,34,.14);
  --due-wash:rgba(162,59,46,.10);   --info-wash:rgba(62,97,135,.09);

  --shadow:0 1px 0 rgba(255,255,255,.4) inset, 0 2px 10px rgba(30,36,27,.10);

  --disp:'Oswald','Arial Narrow',sans-serif;
  --body:'Inter',system-ui,-apple-system,'Segoe UI',sans-serif;
  --mono:'JetBrains Mono',ui-monospace,'SF Mono',Menlo,monospace;
}
@media (prefers-color-scheme:dark){
  :root{
    --bg:#141811;  --surface:#1E2419;  --surface-2:#272E20;  --fg:#DDD8C4;
    --muted:rgba(221,216,196,.60);  --faint:rgba(221,216,196,.38);
    --line:rgba(221,216,196,.14);   --line-strong:rgba(221,216,196,.26);
    --head-bg:#0F120D;  --head-fg:#EDE9D8;  --head-dim:#A9B295;
    --accent:#D98650;  --ok:#8FAE5C;  --soon:#E0B944;  --due:#D2685A;  --info:#7DA3CE;
    --ok-wash:rgba(143,174,92,.14);  --soon-wash:rgba(224,185,68,.14);
    --due-wash:rgba(210,104,90,.13); --info-wash:rgba(125,163,206,.12);
    --shadow:0 1px 0 rgba(255,255,255,.03) inset, 0 2px 12px rgba(0,0,0,.4);
  }
}
```

Also honour `:root[data-theme="light"]` and `:root[data-theme="dark"]` overrides, as the attached file does —
the app has a Light/Dark/System toggle.

### The rules that are easy to break without noticing

1. **Orange (`--accent`) is structural only — never status.** It is for rules, eyebrows, section marks, focus
   rings. A status must never be orange. The status axis is `--ok` green, `--soon` yellow, `--due` rust, and
   they are a different axis from the accent.
2. **`--info` (blue) is reserved for data-integrity flags** — a third axis again, distinct from both accent and
   due-status. A blue thing means "this data is questionable", never "this is due soon".
3. **State must survive greyscale.** Every status is a **severity stripe + an uppercase mono label first**, and
   colour second. Never colour alone. Print the page in black and white: an overdue row must still read as
   overdue. The attached file's `.rrow > .stripe` + `<span class="pill ok">OK</span>` is the pattern — copy it.
4. **Type:** Oswald for display (uppercase, condensed), Inter for body, JetBrains Mono for **all data and
   labels**. Any column of digits gets `font-variant-numeric: tabular-nums`.
5. **Do not number the sections.** The green-lane field manual numbers its sections 01–05 because a document has
   a reading order. A dashboard is scanned, not read. Numbering app UI is decoration posing as information.
6. **No CDN fonts, no CDN anything.** Self-contained files only.

---

## Screens

Sixteen screens. Each is a page in the app; the twelve log/data screens correspond to sheets in the workbook
being replaced.

### 1. Dashboard — **redesign, do not just reuse the attached file**

The attached `dashboard.html` is a strong starting point but is missing two things this design must add:
**navigation** and **quick-add**. Keep its sections (Needs attention, Renewals & due dates, Spend & running
cost, Regular checks, Action items) and its dossier header. Then add:

- **Navigation to every data screen.** The attached file is a single scrolling page with no nav at all. Every
  section should let you get to its underlying screen.
- **The quick-add bar** (see *Quick-add* below).

Keep the dossier header: GB reg plate, the odometer drum, the chips (year, colour, drivetrain). It is the
identity of the app. On a phone it must compress without becoming a generic app bar.

### 2. Fuel log — **the most important screen after the Dashboard**

Table of 13 fills: date, mileage, litres, price/L, total, station, fill level, **computed MPG**, miles since
last. Filterable, sortable.

- MPG is computed per fill, **and is only valid full-to-full**. A fill that was partial at either end shows its
  MPG as unreliable — visibly marked, not silently shown. Design that state.
- Outlier warning: a suspiciously high or low MPG means a missed fill or a mistyped odometer, not good news.
- Fleet stats header: average / best / worst MPG, total litres (556.47), average price/L, last fill date.

### 3. Expenses log

30 rows: date, category, sub-category, vendor, amount, mileage, payment method, notes. Filter by category and
date range. Running total is computed, shown in the header, never a column.

Fuel expenses are **mirrored automatically from the fuel log** — design how a mirrored row reads differently
from a hand-entered one. It should be obvious you edit the fill, not the expense.

### 4. Service history

6 records: date, mileage, type, garage, work done, parts replaced, cost, next due (date), next due (miles).

One row is dated 27 Jun 2026 and logs **83,000 mi** — above the current 80,712, almost certainly 80,300
mistyped. It must display **flagged on the `--info` integrity axis**, not silently. Design that flag inline.

### 5. Regular checks

18 checks, each with a cadence and interval. Four states — and the fourth is the point:

- **7 Overdue**, **3 Due soon**, **7 OK**, **1 Never logged** (= 18)
- "Never logged" is *Spare tyre pressure*. It is a real fourth state, not an error and not "OK". The old
  spreadsheet's dashboard counted 17 because it silently dropped this one.

Needs: "Mark done today" per check, and a **batch "mark all weekly checks done"** for the walk-around routine.
The two K-series head-gasket checks (oil filler cap, coolant colour) are the highest-stakes items here.

### 6. Mileage readings

Simple log: date, mileage, origin (manual / fuel / tyre / wash / service). Current mileage derives from the most
recent reading. This screen is where a non-monotonic history is visible — show the 83,000 outlier in context.

### 7. Tasks — DIY + Workshop

Kanban or grouped-by-status. Filter by kind, priority, status. Fields: priority, title, description, estimated
cost, status (Open / In Progress / Scheduled / Done), target date or target service, assigned garage (Workshop
only).

Two specific views:

- **"Bundle for next garage visit"** — open workshop tasks with a summed estimated cost, ready to hand to the
  garage. Design it to be readable *by the garage*, on a phone screen held out at a counter.
- **Convert a completed workshop task into a service record** in one click.

### 8. Tyre log

Date, mileage, 5 pressures (FL/FR/RL/RR/**spare**), 4 tread depths (no spare tread), location, tool. The
five-pressure / four-tread asymmetry is real — design it, don't normalise it away. A car diagram would beat a
table on mobile.

### 9. Wash log

Date, location, type, cost, mileage. Target cadence is every 3–4 weeks; show days since last wash against it.

### 10. Budget

Category table: annual budget (editable), YTD actual (computed), remaining, % used, over-budget highlighting.
Per-mile budgeted cost. **Period toggle: calendar year / rolling 12 months / since purchase.**

A category with spend but no budget must be visible, not filtered out.

### 11. Issues watchlist

11 items, severity-sorted (Critical / Medium / Low): issue, first noted, last checked, current observation,
action if worsens, estimated fix cost, status (Monitoring / Resolved). Edit observation and last-checked
inline — this is a screen you update in the driveway. Worst-case total cost across open issues.

### 12. Equipment inventory

19 items: item, category, purchased, source, cost, where stored, status (Owned / On order / To order).
"To order" feeds a shopping shortlist.

### 13. Documents

Upload and tag PDFs and photos: V5C, insurance certificate, MOT certificates, receipts, condition photo sets.
Link a document to a service record, expense, or issue. Simple viewer/download. Design the grid for photo sets
and the list for PDFs — they are not the same thing.

### 14. Vehicle info / reference — **the "at the pump" screen**

Static reference, and it must be **fast to read one-handed in bad light**:

- **Fluids:** oil spec + capacity, **coolant OAT (red/pink) — never IAT** (this warning must be visible, it is a
  real failure mode for this engine), brake fluid, transmission oil.
- **Tyres:** size, pressures — normal *and* full-load, front *and* rear — and minimum tread.
- **Parts:** spark plugs, oil/air/fuel/cabin filter part numbers.
- Identity: reg, VIN, year, colour, purchase date and mileage.

This is the screen you open at a tyre bay. Optimise for scanning, not for editing.

### 15. Data integrity

The queue of flagged data. Every flag on the `--info` axis, each with a lifecycle: **Open → Corrected /
Accepted / Dismissed**, with a resolution note.

Three kinds of flag, all raised when data is *written* rather than by any import step: **mileage not
monotonic** (a reading below an earlier one — the 83,000 mi service record is the live example), **fuel cost
discrepancy** (the receipt total disagreeing with litres × price by more than 2p), and **implausible MPG**
(outside 10–70 — usually a missed fill or a mistyped odometer, not good news).

The attached file's "Import check" panel is the tone to aim for — but that panel is a one-off summary framed
around an import that no longer exists; this is a working queue you act on.

### 16. Settings

Multi-section. The sections needed:

1. **Vehicle & reference** — everything on screen 14, editable. Fluid specs, tyre pressures, part numbers.
2. **Statutory & policies** — VED cost and expiry, ULEZ status, insurance block (insurer, policy number,
   period, cover type, premium, excesses, NCB), breakdown cover. **MOT expiry is derived and read-only here** —
   it comes from the latest MOT pass record. Show it with its source, and make it clear why it cannot be typed.
3. **Reference lists** — expense categories, garages, wash locations. Editable. Some categories are
   system-protected and cannot be deleted (deleting "Fuel" would break fuel-to-expense mirroring) — design that
   locked state.
4. **Check definitions** — manage the 18 checks: name, cadence label, interval in days, guidance text, active
   or retired, display order.
5. **Budget targets** — annual budget per category.
6. **Quick-add shortcuts** — which actions appear on the Dashboard, and in what order. See below.
7. **Reminders** — what triggers a reminder (renewal within N days, check overdue, wash cadence exceeded, tyre
   check overdue) and where it goes. The delivery channel is undecided — design it as a pluggable choice.
8. **Appearance** — theme (Light / Dark / System). **Units: MPG or L/100km** — the app computes both, so this is
   a display preference.
9. **Assistant access** — this app exposes its data to an AI assistant over MCP. Two token scopes: **read-only**
   and **read-write**. Design token creation, display-once, scope selection, and revoke. Every assistant write
   is audited, so show that audit trail.
10. **Data** — export to Excel/CSV, backup status, import history.

---

## Quick-add — a core requirement, not a nicety

This is the feature the whole product lives or dies on. Standing at a pump with a receipt, the user opens the
Dashboard and logs a fill-up in seconds.

- A row of **configurable quick-add buttons on the Dashboard**, thumb-reachable on a phone. The user chooses
  which appear and in what order (configured in Settings → Quick-add shortcuts).
- Candidates: **Add fuel**, Add expense, Mark check done, Update mileage, Log wash, Log tyre check, Add task,
  Add issue observation.
- Pressing one opens a **modal on desktop / bottom sheet on mobile** — thumb-zone, not top-of-screen. It must
  not be a full page navigation that loses the Dashboard.
- **Add fuel is the flagship.** Fields: date (defaults today), mileage, litres, price/L, total, station, fill
  level. It computes **MPG live as you type** and warns immediately if the result is implausible. Design the
  keyboard flow: numeric inputs, sensible tab order, minimal taps. Assume one hand and a cold morning.
- Design the success state. After saving, what does the user see? The Dashboard figure that just changed should
  visibly change.

---

## Navigation

- **Mobile:** every screen reachable, thumb-first. Sixteen screens will not fit a bottom bar — design the
  hierarchy. The Dashboard is home; the daily screens (fuel, checks, expenses) deserve to be closer than
  Equipment.
- **Desktop:** roomier. Sidebar or top nav.
- From the Dashboard, each section should lead to its screen — the checks tiles to Regular checks, the renewals
  rows to the relevant record, the spend panel to Expenses.
- **Do not number the nav.**

---

## Responsive

Mobile-first, seriously rather than nominally.

- Design phone (390px), tablet (768px), desktop (1280px). The 1180px `.wrap` max-width in the attached file is
  the desktop container.
- **Tables are the hard problem.** The fuel log has 9 columns. Do not shrink the type and scroll horizontally —
  design what a fuel entry looks like as a card on a phone, and which fields survive. Same for expenses, service
  history, tyres.
- Touch targets 44px minimum. The dossier header must compress gracefully.
- Anything that scrolls sideways does so in its own container — the page body never scrolls horizontally.

---

## Real data — use it, do not invent placeholders

Every figure below is real, from the imported spreadsheet at the 14 July 2026 reference date.

- **Vehicle:** BT53 AKJ · 2003 Land Rover Freelander 1 · 1.8 SE Station Wagon · K-series · navy blue · AWD, VCU, manual 5-spd
- **Odometer:** 80,712 mi · bought 14 Mar 2026 at 76,632 mi · **4,080 miles since purchase**
- **Row counts:** 13 fuel entries · 30 expenses · 6 service records · 18 check definitions · 11 watchlist items · 19 equipment items
- **Checks:** 7 overdue · 3 due soon · 7 OK · 1 never logged
- **Fuel:** 556.47 L total · £888.86 fuel spend YTD
- **MOT:** expires 8 Jul 2027 · **359 days** · passed 8 Jul 2026 at 80,705 mi at K & P Motors · advisories: headlamp lens, rear tyres
- **Insurance:** Admiral · P77904683 · comprehensive · expires 15 Mar 2027 · £517.14/yr · £250 excess · 0 yrs NCB
- **Flagged:** service record 27 Jun 2026 logs 83,000 mi (above current — likely 80,300 mistyped)
- **Overdue checks include:** oil filler cap and coolant reservoir colour, last done 18 June, weekly cadence, **19 days overdue** — these are the K-series head-gasket early warning
- **Colour thresholds:** red under 30 days, amber under 60

---

## Output

- **One self-contained HTML file per screen.** Inline CSS. No CDN links, no external requests.
- Each file stands alone and can be opened directly in a browser.
- Name them plainly: `dashboard.html`, `fuel-log.html`, `settings.html`, and so on.
- Static HTML and CSS. JavaScript only where a state genuinely cannot be shown without it (the theme toggle, an
  open bottom sheet). These are design concepts, not an application — they will be rebuilt as React.
- **Show states, not just the happy path.** For each screen include the interesting ones: empty, a flagged row,
  an unreliable MPG, an overdue check, an over-budget category. The states are the design.
- Fonts: reference the family names (`Oswald`, `Inter`, `JetBrains Mono`) via the `--disp` / `--body` / `--mono`
  stacks. Do not add CDN font links — the fallbacks in those stacks are acceptable for a concept.

## Do not

- Do not invent a new palette, restyle the attached file, or "modernise" the identity.
- Do not use orange for status, or blue for anything but data integrity.
- Do not convey status by colour alone.
- Do not number sections or nav items.
- Do not show a derived figure as editable (MOT expiry is the trap — it is derived from the MOT pass record).
- Do not design a login, signup, onboarding, or marketing page. Single user, self-hosted, already inside.
- Do not use placeholder data. The real figures are above.

## Batching

Sixteen screens in one pass will be thin. Suggested three passes, each with this brief plus the attached
reference:

1. **Dashboard + quick-add + navigation + fuel log** — the daily loop, and the hardest screens. Get these right
   first; they set the patterns everything else follows.
2. **Expenses, service history, regular checks, mileage, tasks** — the rest of the daily and weekly screens.
3. **Tyres, wash, budget, issues, equipment, documents, vehicle info, data integrity, settings** — the long
   tail, reusing the patterns from passes 1 and 2.

For passes 2 and 3, attach the output of the previous pass as well, so patterns carry forward instead of being
reinvented.

---

## Addendum (2026-07-14) — screen 17: Garage, the actual home

Added after the original brief: the app is **multi-vehicle** (DEC-007). The garage is the home screen; the
Dashboard (screen 1) is the *per-vehicle* home reached from it. Everything else in this brief is unchanged —
all sixteen screens live under a selected vehicle.

### 17. Garage (home)

- **One card per vehicle.** Each card: the GB reg plate treatment, vehicle name and spec line, a lifecycle
  status badge, current mileage, and an **attention summary** — overdue/due-soon check counts and the next
  renewal with its day count, using the same stripe + uppercase mono label + colour treatment as everywhere
  else. The card is the `get_due_items` headline, not a menu entry.
- **Lifecycle status:** Active / Sold / SORN. Sold and SORN cards keep their history and stay browsable but
  read as parked — designed distinct from Active without relying on colour alone, and their attention counts
  are silenced.
- **Add-car flow.** The vehicle form (identity, purchase, engine, fluids, tyres — the screen 14 fields), plus
  one decision the flow must present clearly: **where do this car's regular checks come from?** Three options:
  start empty / a generic starter set (tyre pressures, oil level, coolant level, lights, wipers, wash cadence)
  / copy from an existing vehicle. Design the three-way choice; it is not buried in settings.
- **With one car** the garage still exists (it is where add-car lives) — design it so a single card doesn't
  look lost. A "jump back into BT53 AKJ" affordance is welcome; an automatic bypass of the garage is not.
- Reference data for a second card, if a dummy is needed for the multi-car state: use a plainly fictional
  reg ("AB12 CDE") and mark it Sold, so the two-state card design shows in one screen.
- Navigation consequence for all other screens: the vehicle is in the URL (`/:reg/…`), and the nav shows which
  car you're in with a compact reg-plate chip that returns to the garage.
