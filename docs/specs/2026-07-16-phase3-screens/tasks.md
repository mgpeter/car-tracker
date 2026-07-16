# Spec Tasks

> All complete on creation — this spec documents shipped, tested work. Boxes are ticked to reflect reality, not
> to plan execution.

## Tasks

- [x] 1. Tasks and issues API + screens
  - [x] 1.1 `TaskEndpoints` (GET/POST/PATCH/DELETE) with derived bundle total
  - [x] 1.2 `IssueEndpoints` (GET/POST/PATCH/DELETE) with derived worst-case cost
  - [x] 1.3 `TasksPage` — DIY/Workshop board, High priority rendered, add/edit sheet
  - [x] 1.4 `IssuesPage` — watchlist, derived duration, `ActionIfWorsens`, add/edit sheet
  - [x] 1.5 Tests + axe sweep pass

- [x] 2. The three logs
  - [x] 2.1 `LogEndpoints` — tyres, washes, equipment (one file, one shape)
  - [x] 2.2 `ReferenceWriter` — garages and wash locations created on first use (FK trap fix)
  - [x] 2.3 `TyresPage` (by corner, spare nullable), `WashPage` (derived cadence), `EquipmentPage` (stock axis)
  - [x] 2.4 Tests + axe sweep pass

- [x] 3. Budget and vehicle info
  - [x] 3.1 `BudgetPage` — editable targets, derived variance, period toggle, capped over-budget bars
  - [x] 3.2 `GET /api/vehicles/{reg}` (`VehicleDetail`) exposing the stored specs
  - [x] 3.3 `VehicleInfoPage` — the reference card; policies as inputs, countdowns left on the dashboard
  - [x] 3.4 Tests + axe sweep pass

- [x] 4. Cross-cutting fixes and verification
  - [x] 4.1 `usePlate()` + `coverage.test.ts` guard against `plate={reg}` (the twelve-times bug)
  - [x] 4.2 Reference endpoint for expense categories; `MileageOrigin` map off the wire type
  - [x] 4.3 Fuel `DELETE` — a fill can be removed like an expense or a service record
  - [x] 4.4 All 16 routes wired; live verification on BT53; full suite green (236 .NET, 297 front-end)
