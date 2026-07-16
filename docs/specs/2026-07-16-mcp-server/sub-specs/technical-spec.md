# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-mcp-server/spec.md

## Technical Requirements

### Host

- In-process in the existing `CarTracker.WebApi` (or a library it references from the empty
  `CarTracker.ModelContextProtocol` project, which today references only `CarTracker.Domain`). HTTP/SSE
  transport so the Claude app can reach it remotely once TLS exists. Routed through `CarTracker.Gateway` on the
  one origin, like everything else — no CORS, no second deployable (README §5, tech-stack notes).
- **Package family is task 1.** Evaluate Microsoft Agent Framework (named in `tech-stack.md`) against
  `ModelContextProtocol.AspNetCore` (the ecosystem package) and record the choice as a decision (DEC). The
  project deliberately carries no MCP dependency until this is settled.

### Read tools — call the shared brain, never re-query

- Every read tool resolves its optional `vehicle` (registration or id) to an id via the existing
  `VehicleLookup`, falling back to the default vehicle (DEC-007); an unknown or ambiguous name is an error,
  never a guess.
- The tools then call `IDerivedMetricsService` — the *same* instance the web API uses — so the assistant and
  the dashboard cannot diverge. `get_due_items`, `get_vehicle_summary`, `get_fuel_status`, `get_spend_summary`,
  `get_open_tasks`, `get_issues`, `get_check_status`, `get_reference`, `list_vehicles` (README §5.2).
  `get_fuel_status`'s "estimated range remaining" is a derived figure shared with the dashboard tank-range
  idea; `get_reference` reads the `VehicleDetail` the `GET /vehicles/{reg}` endpoint already assembles.
- **Return structured JSON plus a short human summary string** (README §5 design note), so the model has both a
  parseable object and a sentence to relay.

### Write tools — reuse the domain path, not a new one

- Each write tool calls the **same factory or endpoint logic** the web write uses:
  `log_fuel_fillup` → `FuelEntryFactory` (three rows, one transaction, execution strategy, anomaly scan);
  `log_expense`, `update_mileage`, `mark_check_done`, `add_task`, `complete_task` (with promote-to-service, once
  that spec lands), `log_wash`, `log_tyre_reading`, `add_issue_observation`. No write tool re-implements
  validation, mirroring or scanning — divergence there is the exact failure the single-domain design prevents.
- **`source = "mcp"` on every write**, via the existing `EntrySource` audit stamp, so the write-audit trail and
  every row's provenance record which surface made the change.
- Mileage monotonicity is flagged, never rejected (§5.3, §5 design notes) — the same `AnomalyScanner` the web
  path runs, so an MCP-logged bad reading raises the same flag.

### Token scopes

- Two bearer tokens: read-only reaches only the read tools; read-write reaches both. The write tools are
  **unreachable** without the write scope — an authorisation gate on the tool group, not a soft convention.
- Tokens are created and revoked in Settings' ASSISTANT ACCESS panel (the design shows this), the **secret shown
  once** on creation ("copy it now"), stored hashed. This is a separate mechanism from the static `X-Api-Key`
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

## External Dependencies (Conditional)

- **An MCP server package** — either **Microsoft Agent Framework** (per `tech-stack.md`) or
  **`ModelContextProtocol.AspNetCore`** (the ecosystem package). **Justification:** hosting MCP over HTTP/SSE in
  ASP.NET Core needs a transport/protocol implementation; hand-rolling the protocol is out of proportion. The
  choice between the two is task 1 and is recorded as a DEC before any dependency is added.
