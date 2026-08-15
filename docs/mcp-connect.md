# Connecting the MCP server

The MCP server is hosted in-process in the WebApi and reached through the gateway at **`/mcp`** over Streamable
HTTP (DEC-004, DEC-014). It is gated by a **scoped bearer token**, not the web front-end's `X-Api-Key`.

## 1. Mint a token

Settings → **Assistant access** → *Add token…* - give it a name and a scope:

- **Read-only** - reaches the read tools (`get_due_items`, `get_fuel_status`, `list_expenses`, …). Cannot mutate.
- **Read-write** - also reaches the write tools (`log_fuel_fillup`, `log_expense`, `mark_check_done`, …).

The secret is shown **once**. Copy it then; only its hash is stored. Revoke a token from the same panel; every
write a token made is listed in the write-audit trail beneath it (reads are counted on the token, not listed).

## 2. Point Claude Desktop at it

Claude Desktop speaks to remote MCP servers over stdio, so bridge the HTTP endpoint with
[`mcp-remote`](https://www.npmjs.com/package/mcp-remote). Edit `claude_desktop_config.json` - the config differs
by platform.

**macOS / Linux** - `npx` runs directly and the space in the header is fine:

```jsonc
{
  "mcpServers": {
    "car-tracker": {
      "command": "npx",
      "args": [
        "-y", "mcp-remote",
        "http://localhost:5080/mcp",
        "--header", "Authorization: Bearer ${CAR_TRACKER_TOKEN}"
      ],
      "env": { "CAR_TRACKER_TOKEN": "ct_...the secret you copied..." }
    }
  }
}
```

**Windows** - launch through `cmd /c`, and keep the space out of the header arg:

```jsonc
{
  "mcpServers": {
    "car-tracker": {
      "command": "cmd",
      "args": [
        "/c", "npx", "-y", "mcp-remote",
        "http://localhost:5080/mcp",
        "--header", "Authorization:${AUTH_HEADER}"
      ],
      "env": { "AUTH_HEADER": "Bearer ct_...the secret you copied..." }
    }
  }
}
```

Two Windows-only gotchas, both of which fail silently with "Server disconnected":
- Spawn **`cmd /c npx`**, not `npx` directly - `npx` is a `.cmd` shim and spawning it straight fails to resolve on
  a path with spaces (`'C:\Program' is not recognized`).
- Write the header as **`Authorization:${AUTH_HEADER}` with no literal space**, and put the `Bearer …` value in
  `env`. A bare `--header "Authorization: Bearer …"` is split on the space by cmd and mangled; mcp-remote expands
  `${AUTH_HEADER}` and splits the header on the first colon, so the value is reassembled as
  `Authorization: Bearer …` intact.

Use the gateway origin (`http://localhost:5080/mcp`) on either platform - it routes `/mcp` → the WebApi and
forwards the header. Remote (non-localhost) use needs HTTPS, because the token crosses the network (DEC-004).

Restart Claude Desktop; the tools appear under a **car-tracker** connector. Ask *"what needs attention on BT53?"*
to confirm reads, and (with a read-write token) *"log a fill: 47 litres at 80,900 miles, £1.45/litre"* or
*"insurance is Admiral comprehensive, renews 31 Jan 2027"* to confirm writes - the change appears in the browser
on refresh, computed and audited, stamped `source = mcp`. Renewal dates (insurance/road tax) set this way drive
the dashboard's countdowns just as the web Settings would.

> Claude Desktop's native "custom connector" flow expects OAuth; with a static token the `mcp-remote` bridge is
> the reliable path. If a future release accepts a bearer header directly, point the connector at `/mcp` with the
> token instead.

## 3. The tool catalogue

49 tools - 19 read, 30 write. **A connected client's own `tools/list` is authoritative**; this table is the
convenience copy, and if the two disagree, the server is right. Every tool takes an optional `vehicle`
(registration or id); omit it and the default vehicle is used.

### Read (19) - any token

| Group | Tools |
|---|---|
| Derived summaries | `get_due_items` (ask this first), `get_vehicle_summary`, `get_fuel_status`, `get_spend_summary`, `get_check_status`, `get_budget` |
| Logs | `list_expenses`, `list_fuel_fillups`, `list_mileage`, `list_service_history`, `list_tyre_readings`, `list_wash_log`, `list_equipment` |
| Everything else | `list_vehicles`, `list_check_definitions`, `get_open_tasks`, `get_issues`, `get_data_integrity`, `get_reference` |

The summaries call the same `IDerivedMetricsService` the dashboard does, so an answer here and a figure on
screen cannot disagree.

### Write (30) - read-write token only

| Group | Tools |
|---|---|
| Log something | `log_fuel_fillup`, `log_expense`, `log_wash`, `log_tyre_reading`, `update_mileage`, `mark_check_done` |
| Records and tasks | `add_service`, `add_task`, `complete_task`, `add_issue`, `add_issue_observation`, `add_equipment`, `add_vehicle` |
| Vehicle settings | `set_insurance`, `set_road_tax`, `set_fluids`, `set_tyre_specs`, `update_vehicle_profile` |
| Correct a row | `update_fuel_fillup`, `update_service`, `update_mileage_reading`, `update_tyre_reading`, `update_wash`, `update_equipment` |
| Remove a row | `delete_fuel_fillup`, `delete_service`, `delete_mileage_reading`, `delete_tyre_reading`, `delete_wash`, `delete_equipment` |

Notes worth knowing before asking for one:

- **There is no MOT-expiry setter, deliberately.** MOT expiry is derived from the logged pass - log the pass
  with `add_service` and `type = "MOT"` (matched exactly) and the countdown follows.
- **A `Fuel`-category expense is refused.** Fuel figures come from `log_fuel_fillup`, which mirrors into
  expenses by itself; a typed fuel expense is the workbook's lumped "fuel to date" row and that is the
  £163.16 gap the app exists to close. `Purchase` is refused for the same reason.
- **Implausible mileage is flagged, never rejected.** A reading below the current odometer still writes, and
  comes back with the anomaly note attached.
- Every write is stamped `source = mcp` and recorded in the audit trail against the token that made it.

## 4. The in-app chat

The tools are thin adapters over the shared application layer in `CarTracker.Domain` (the query/write services
and the derived-metrics service). The in-app chat consumes the **same** methods in-process, as one shared
`AIFunction` catalogue - a second consumer of one brain, not a second copy of the logic - so a tool added here
appears there automatically, and a drift test fails the build if that ever stops being true (DEC-019). It is
specced in `docs/specs/2026-08-06-in-app-chat-assistant/` and not yet built; it needs a `Chat:ApiKey`.
