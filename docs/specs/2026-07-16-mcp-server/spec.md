# Spec Requirements Document

> Spec: MCP Server — the assistant as a first-class client
> Created: 2026-07-16
> Status: In progress (revised 2026-07-20)

> **Revision 2026-07-20 (DEC-014 + approved plan).** Three things are now settled that this document originally
> left open or narrower: (1) the package is **`ModelContextProtocol.AspNetCore`** over **Streamable HTTP** (not
> "HTTP/SSE"), so the "package decision" below is resolved, not a task; (2) the **read tools cover every screen** —
> the derived summaries *plus* a raw per-screen list tool each, and `get_budget` / `get_data_integrity` which were
> missing; (3) **write tools are add/log + safe-updates only** (no edit/delete of existing rows), and the scoped
> tokens are built on ASP.NET Core auth schemes + policies so a future Auth0/JWT multi-user path drops in without
> touching the tools. Half the write paths and all the raw lists live inline in the REST endpoints today; they are
> **extracted into a shared application layer** the REST API and the MCP tools both call.

## Overview

Expose the domain as MCP tools, hosted in-process in the same ASP.NET Core app, so the assistant reads the same
live figures the web UI shows and can log on the owner's behalf — the differentiator the mission is built
around. Read tools first (safe, behind the existing scheme), then guarded write tools behind a read-write
token, every write audited as `source = "mcp"`.

## User Stories

### One answer, two surfaces

As the owner, I want to ask the assistant "what needs my attention?" and get the same answer the dashboard
shows, because both called the same service.

The mission's whole claim over a spreadsheet is that a figure cannot disagree with itself across surfaces:
`IDerivedMetricsService` computes every derived value once, and the web API already calls it. The MCP read
tools call the *same* service — not a parallel query — so `get_due_items` and the dashboard's attention panel
are the same computation with two renderings.

### Log by voice, see it in the browser

As the owner, I want to tell the assistant "filled up, 47 litres at 80,900" and have the fill appear in the
browser immediately, computed and audited.

A write tool runs the same `FuelEntryFactory` the web sheet does — same transaction, same mileage reading, same
expense mirror, same anomaly scan — and stamps `source = "mcp"`. The fill the assistant logs is
indistinguishable from one typed in, except that the audit trail knows which surface made it.

### Casual access can't mutate

As the owner, I want a read-only token for "what's my MPG" and a separate read-write token for logging, so
that a token I paste somewhere casual cannot change my data.

Two scopes: a read-only token that reaches only the read tools, and a read-write token that also reaches the
write tools. The write tools are unreachable without the write scope, not merely discouraged.

## Spec Scope

1. **MCP host** — in-process over HTTP/SSE in the existing ASP.NET Core app, reachable through the gateway,
   never exposed unauthenticated.
2. **Read tools** — the §5.2 catalogue, calling `IDerivedMetricsService` directly, each taking an optional
   `vehicle` with the default-vehicle fallback (DEC-007); `get_due_items` first.
3. **Write tools** — the §5.3 catalogue, reusing the existing factories/endpoints' domain path so validation,
   mirroring and anomaly-scanning are identical to the web writes; `source = "mcp"` on every one.
4. **Token scopes** — read-only and read-write bearer tokens, created and revoked in Settings, the secret shown
   once.
5. **Write audit trail** — every assistant write listed verbatim in Settings ("reads are counted, not listed"),
   as the design's ASSISTANT ACCESS panel shows.

## Out of Scope

- ~~**The package-family decision made blindly.**~~ **Resolved by DEC-014:** `ModelContextProtocol.AspNetCore`
  over Streamable HTTP. Microsoft Agent Framework is not an MCP *server* host — it is a candidate for the future
  in-app chat that *consumes* these tools, a later phase.
- **Entering the spreadsheet history via the agent** (roadmap Phase 4). That is a *use* of these tools once they
  exist, supervised against the workbook (DEC-008), and belongs after the write tools land — not in this spec's
  build.
- **Remote HTTPS termination / mTLS.** The read tools can land behind the existing API key locally; exposing the
  token-carrying endpoint over the network is Phase 5 hardening. This spec builds the tools and the scoped
  tokens; it does not stand up public TLS.
- **`search_documents` / document tools.** They depend on the Documents feature (its own spec); the read-tool
  set here excludes them until documents exist.
- **The MCP reminder channel.** Settings' design offers "Assistant · MCP" as a notification channel; that is the
  reminders spec's concern, not this one.

## Expected Deliverable

1. With a read-only token, `get_due_items` returns the same overdue checks, upcoming renewals and open
   high-priority tasks the dashboard shows for BT53 — because both called `IDerivedMetricsService`.
2. With a read-write token, `log_fuel_fillup` records a fill that appears in the browser on refresh, computed
   (MPG returned), mirrored into expenses, mileage-reading written, anomaly-scanned, and stamped
   `source = "mcp"`; the read-only token cannot reach it.
3. A read-write token is created and revoked in Settings with the secret shown once, and every write it made is
   listed in the write-audit trail.
