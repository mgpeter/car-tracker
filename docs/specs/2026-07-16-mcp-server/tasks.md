# Spec Tasks

## Tasks

- [ ] 1. Choose the package and stand up the host
  - [ ] 1.1 Evaluate Microsoft Agent Framework vs `ModelContextProtocol.AspNetCore`; record the choice as a DEC
  - [ ] 1.2 Add the dependency and an in-process HTTP/SSE MCP host, routed through the gateway
  - [ ] 1.3 A trivial `list_vehicles` tool proves the transport end to end
  - [ ] 1.4 Verify it responds through the gateway origin

- [ ] 2. Read tools
  - [ ] 2.1 Write tests: each tool resolves `vehicle` (default fallback; unknown = error) and its figures equal the web summary's
  - [ ] 2.2 `get_due_items` first, then the rest of §5.2, each calling `IDerivedMetricsService` — never a re-query
  - [ ] 2.3 Structured JSON + a short human summary per tool
  - [ ] 2.4 Verify tests pass; live-check `get_due_items` against BT53's dashboard

- [ ] 3. Token scopes and audit
  - [ ] 3.1 Write tests: read-only token reaches read tools only; read-write reaches both; secret stored hashed
  - [ ] 3.2 Token creation/revocation, secret shown once, in Settings' ASSISTANT ACCESS panel
  - [ ] 3.3 Write-audit trail — every write recorded and listed; reads counted
  - [ ] 3.4 Verify tests pass

- [ ] 4. Write tools
  - [ ] 4.1 Write tests: each tool reuses the web domain path (identical rows bar `source`); monotonicity flagged not rejected
  - [ ] 4.2 The §5.3 catalogue, each calling the existing factory/endpoint logic; `source = "mcp"` on every write
  - [ ] 4.3 Verify tests pass

- [ ] 5. Prove it end to end on BT53
  - [ ] 5.1 With a read-only token, `get_due_items` matches the dashboard; the token cannot log a fill
  - [ ] 5.2 With a read-write token, `log_fuel_fillup` appears in the browser, computed/mirrored/scanned, `source = "mcp"`, and shows in the audit trail
  - [ ] 5.3 Full suite, both builds, codegen gate; update roadmap/CLAUDE.md
