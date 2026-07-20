# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-mcp-server/spec.md

## Technical Requirements

### Host

- In-process in the existing `CarTracker.WebApi`, with the tools in the `CarTracker.ModelContextProtocol`
  library (which the WebApi already references). **Streamable HTTP** transport (`AddMcpServer().WithHttpTransport()`
  + `MapMcp("/mcp")`) so the Claude app can reach it remotely once TLS exists. Routed through `CarTracker.Gateway`
  on the one origin, like everything else — no CORS, no second deployable (README §5, tech-stack notes).
- **Package settled (DEC-014):** `ModelContextProtocol.AspNetCore`, the official C# MCP SDK — not Microsoft Agent
  Framework, which is for building an agent that *consumes* tools (the future in-app chat), not for hosting an MCP
  server. The dependency is added to `Directory.Packages.props` and the scaffold `.csproj`.

### Read tools — call the shared brain, never re-query

- Every read tool resolves its optional `vehicle` (registration or id) to an id via the existing
  `VehicleLookup`, falling back to the default vehicle (DEC-007); an unknown or ambiguous name is an error,
  never a guess.
- The **derived** tools then call `IDerivedMetricsService` — the *same* instance the web API uses — so the
  assistant and the dashboard cannot diverge. `get_due_items` (via `ReminderEvaluator` over the summary),
  `get_vehicle_summary`, `get_fuel_status`, `get_spend_summary`, `get_open_tasks`, `get_issues`,
  `get_check_status`, `get_reference`, `list_vehicles`, **plus `get_budget`** (`GetBudgetSummaryAsync`, period
  param) and **`get_data_integrity`** (the anomaly queue — omitted from §5.2). `get_fuel_status`'s "estimated
  range remaining" is the dashboard's tank-range figure; `get_reference` reads the `VehicleDetail` the
  `GET /vehicles/{reg}` endpoint assembles.
- **Raw per-screen list tools** cover the owner's "read all data from all screens": `list_fuel_fillups`,
  `list_expenses`, `list_mileage`, `list_service_history`, `list_tyre_readings`, `list_wash_log`,
  `list_equipment`, `list_check_definitions`. These call the **extracted query services** (see the shared layer
  below), not a bespoke re-query — the same projections the REST endpoints return. `search_documents` stays
  deferred until Documents exists.
- **Return structured JSON plus a short human summary string** (README §5 design note), so the model has both a
  parseable object and a sentence to relay.

### The shared application layer

- Today the derived figures flow through `IDerivedMetricsService`, but the **raw list projections and half the
  write invariants live inline in the WebApi endpoint handlers** (row DTOs inside `*Endpoints.cs`; the Fuel-
  category refusal, mirrored-row refusal, `SyncOdometerShadowAsync`, only-`Manual`-editable rule, and
  `ReferenceWriter` calls). "Reuse the domain path" therefore requires *building* that path: lift the row DTOs
  into `CarTracker.Shared` and extract per-screen **query services** + the D-path **write services** into
  `CarTracker.Domain`, each write service taking `EntrySource`. The REST endpoints refactor to call them — a
  behaviour-preserving change the existing suite guards — and the MCP tools call the same services. The future
  in-app chat is a third caller of this layer.

### Write tools — reuse the domain path, not a new one

- **Add/log + safe-updates only** — the assistant creates entries and makes the safe updates
  (`update_mileage`, `mark_check_done`, `complete_task`); it does **not** edit or delete existing rows. Each tool
  calls the **same factory or service** the web write uses. Factory-backed: `log_fuel_fillup` → `FuelEntryFactory`
  (three rows, one transaction, execution strategy, anomaly scan), `add_service` → `ServiceRecordFactory`,
  `add_task` + `complete_task` (promote via `TaskPromoter`), `add_vehicle` → `VehicleFactory`. Service-backed
  (the extracted D-paths): `log_expense`, `update_mileage`, `mark_check_done`, `log_wash`, `log_tyre_reading`,
  `add_issue`, `add_issue_observation`, `add_equipment`. No write tool re-implements validation, mirroring or
  scanning — divergence there is the exact failure the single-domain design prevents.
- **`source = "mcp"` on every write**, via the existing `EntrySource` audit stamp (already `Mcp = 2`), so every
  row's provenance and the write-audit trail record which surface made the change.
- Mileage monotonicity is flagged, never rejected (§5.3, §5 design notes) — the same `AnomalyScanner` the web
  path runs — and the raised flags are returned in the tool response so the assistant can relay them.

### Token scopes

- Two bearer tokens: read-only reaches only the read tools; read-write reaches both. The gate is built on the
  **standard ASP.NET Core pipeline** — an `AssistantToken` authentication scheme mints `mcp:read` / `mcp:write`
  claims, and authorization **policies** `McpRead` / `McpWrite` check those *claims, not the scheme*. `MapMcp`
  requires `McpRead`; write tools carry `[Authorize(Policy="McpWrite")]` (via `AddAuthorizationFilters()`), so
  the write tools are **unreachable** without the write scope. Checking claims (not the scheme) is the
  pluggability seam: a future Auth0 `.AddJwtBearer().AddMcp()` maps its `permissions` to the same claims and the
  tools do not change.
- Tokens are created and revoked in Settings' ASSISTANT ACCESS panel, the **secret shown once** on creation
  ("copy it now"), stored hashed (`AssistantToken`). This is a separate mechanism from the static `X-Api-Key`
  that guards `/api` (DEC-009) — the API key is the web front-end's; these tokens are the assistant's.

### Write-audit trail

- Every assistant write is recorded (tool, vehicle, timestamp, a summary of the change) and listed verbatim in
  Settings; reads are counted, not listed (the design's wording). This is the observable half of `source =
  "mcp"`.

### Verification

- Read parity: assert a read tool's figures equal the web summary's for the same vehicle (both call the same
  service) — a unit/integration test, and a live check on BT53 that `get_due_items` matches the dashboard.
- Write parity: `log_fuel_fillup` through MCP and through the web endpoint produce identical rows (bar the
  `source`); the MCP write appears in the browser on refresh; the read-only token is rejected by a write tool.
- The five-defect fixture is untouched — no new arithmetic; the domain service is reused wholesale.

## External Dependencies

- **`ModelContextProtocol.AspNetCore`** (v1.4.1, the official C# MCP SDK) — pinned in `Directory.Packages.props`,
  referenced by `CarTracker.ModelContextProtocol`. **Justification:** hosting MCP over Streamable HTTP in ASP.NET
  Core needs a transport/protocol implementation; hand-rolling it is out of proportion. Chosen over Microsoft
  Agent Framework by DEC-014 (the Agent Framework hosts no MCP server — it is a future in-app-chat concern).
