# Spec Tasks

> Revised 2026-07-20 per DEC-014 and the approved plan. Package + transport are settled (task 1 was "choose");
> the read set now covers all screens, writes are add/log + safe-updates only, and the endpoint-inlined logic
> is extracted into a shared application layer both the REST API and the MCP tools call.

## Tasks

- [x] 0. Docs — decision and spec alignment
  - [x] 0.1 Record DEC-014: `ModelContextProtocol.AspNetCore` + Streamable HTTP (amends DEC-004)
  - [x] 0.2 Amend `tech-stack.md`, the scaffold `.csproj`, and add the package to `Directory.Packages.props`
  - [x] 0.3 Align `spec.md` / `technical-spec.md` with the expanded read/write set and pluggable auth

- [x] 1. Package + in-process host
  - [x] 1.1 `AddMcpServer().WithHttpTransport()` in `CarTracker.WebApi`; tools live in `CarTracker.ModelContextProtocol`
  - [x] 1.2 `MapMcp("/mcp")` on the shared DI container; add the gateway `/mcp` route (mirrors `/api`)
  - [x] 1.3 A trivial `list_vehicles` tool proves the transport end to end
  - [x] 1.4 Verify it responds through the gateway origin

- [x] 2. Extract the shared application layer (behaviour-preserving)
  - [x] 2.1 Lift the row DTOs out of `*Endpoints.cs` into `CarTracker.Shared`
  - [x] 2.2 Extract per-screen query services (raw lists) into `CarTracker.Domain`; endpoints call them
  - [x] 2.3 Extract the D-path write services (expense, mileage, tyre, wash, issue, check-log, equipment) into
        `CarTracker.Domain`, each taking `EntrySource`; endpoints call them
  - [x] 2.4 The full existing suite (260 .NET, 395 FE) passes unchanged — the refactor is the safety net

- [x] 3. Token scopes and audit
  - [x] 3.1 Write tests: read-only token reaches read tools only; read-write reaches both; secret stored hashed
  - [x] 3.2 `AssistantToken` / `AssistantWriteAudit` entities + migration; `AssistantToken` auth scheme minting
        `mcp:read` / `mcp:write` claims; `McpRead` / `McpWrite` policies (claims, not schemes — the pluggability seam)
  - [x] 3.3 Token creation/revocation (secret shown once) + write-audit list in Settings' ASSISTANT ACCESS panel;
        `/api/assistant/tokens` + `/api/assistant/audit` endpoints
  - [x] 3.4 Verify tests pass

- [x] 4. Read tools — all screens
  - [x] 4.1 Write tests: each tool resolves `vehicle` (default fallback; unknown = error) and its figures equal
        the web summary's
  - [x] 4.2 `get_due_items` first, then the rest of the derived set (incl. `get_budget`, `get_data_integrity`)
        calling `IDerivedMetricsService` + `ReminderEvaluator` — never a re-query
  - [x] 4.3 Raw per-screen list tools (`list_fuel_fillups`, `list_expenses`, `list_mileage`,
        `list_service_history`, `list_tyre_readings`, `list_wash_log`, `list_equipment`, `list_check_definitions`)
        calling the extracted query services
  - [x] 4.4 Structured JSON + a short human summary per tool
  - [x] 4.5 Verify tests pass; live-check `get_due_items` against BT53's dashboard

- [x] 5. Write tools — add/log + safe updates only
  - [x] 5.1 Write tests: each tool reuses the web domain path (identical rows bar `source`); monotonicity flagged
        not rejected; read-only token rejected by a write tool
  - [x] 5.2 Factory-backed (`log_fuel_fillup`, `add_service`, `add_task`, `complete_task`+promote, `add_vehicle`)
        and service-backed (`log_expense`, `update_mileage`, `mark_check_done`, `log_wash`, `log_tyre_reading`,
        `add_issue`, `add_issue_observation`, `add_equipment`); `source = "mcp"` on every write; anomaly scan;
        an `AssistantWriteAudit` row per write; raised flags returned in the response
  - [x] 5.3 Verify tests pass

- [x] 6. Prove it end to end on BT53
  - [x] 6.1 With a read-only token, `get_due_items` matches the dashboard; the token cannot log a fill
  - [x] 6.2 With a read-write token, `log_fuel_fillup` appears in the browser, computed/mirrored/scanned,
        `source = "mcp"`, and shows in the audit trail
  - [x] 6.3 Connect Claude Desktop live (streamable HTTP via the gateway `/mcp`); ship the connection note in `docs/`
  - [x] 6.4 Full suite, both builds, codegen gate; update roadmap/CLAUDE.md/spec docs
