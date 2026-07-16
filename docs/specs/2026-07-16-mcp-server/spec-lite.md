# Spec Summary (Lite)

Expose the domain as in-process MCP tools so the assistant reads the same live figures the web UI shows (via the
one `IDerivedMetricsService`) and can log on the owner's behalf through the same factories the web writes use —
so an MCP-logged fill is indistinguishable from a typed one except that the audit trail stamps `source = "mcp"`.
The mission's differentiator.

Read tools first (§5.2, `get_due_items` first, safe behind the existing scheme), then guarded write tools (§5.3)
behind a read-write bearer token; two token scopes created/revoked in Settings with a one-time secret reveal,
and a write-audit trail. The package-family choice (Microsoft Agent Framework vs `ModelContextProtocol
.AspNetCore`) is task 1, not a given; remote HTTPS is Phase 5, document tools await the Documents feature.
