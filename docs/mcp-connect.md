# Connecting the MCP server

The MCP server is hosted in-process in the WebApi and reached through the gateway at **`/mcp`** over Streamable
HTTP (DEC-004, DEC-014). It is gated by a **scoped bearer token**, not the web front-end's `X-Api-Key`.

## 1. Mint a token

Settings → **Assistant access** → *Add token…* — give it a name and a scope:

- **Read-only** — reaches the read tools (`get_due_items`, `get_fuel_status`, `list_expenses`, …). Cannot mutate.
- **Read-write** — also reaches the write tools (`log_fuel_fillup`, `log_expense`, `mark_check_done`, …).

The secret is shown **once**. Copy it then; only its hash is stored. Revoke a token from the same panel; every
write a token made is listed in the write-audit trail beneath it (reads are counted on the token, not listed).

## 2. Point Claude Desktop at it

Claude Desktop speaks to remote MCP servers over stdio, so bridge the HTTP endpoint with
[`mcp-remote`](https://www.npmjs.com/package/mcp-remote). Edit `claude_desktop_config.json` — the config differs
by platform.

**macOS / Linux** — `npx` runs directly and the space in the header is fine:

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

**Windows** — launch through `cmd /c`, and keep the space out of the header arg:

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
- Spawn **`cmd /c npx`**, not `npx` directly — `npx` is a `.cmd` shim and spawning it straight fails to resolve on
  a path with spaces (`'C:\Program' is not recognized`).
- Write the header as **`Authorization:${AUTH_HEADER}` with no literal space**, and put the `Bearer …` value in
  `env`. A bare `--header "Authorization: Bearer …"` is split on the space by cmd and mangled; mcp-remote expands
  `${AUTH_HEADER}` and splits the header on the first colon, so the value is reassembled as
  `Authorization: Bearer …` intact.

Use the gateway origin (`http://localhost:5080/mcp`) on either platform — it routes `/mcp` → the WebApi and
forwards the header. Remote (non-localhost) use needs HTTPS, because the token crosses the network (DEC-004).

Restart Claude Desktop; the tools appear under a **car-tracker** connector. Ask *"what needs attention on BT53?"*
to confirm reads, and (with a read-write token) *"log a fill: 47 litres at 80,900 miles, £1.45/litre"* or
*"insurance is Admiral comprehensive, renews 31 Jan 2027"* to confirm writes — the change appears in the browser
on refresh, computed and audited, stamped `source = mcp`. Renewal dates (insurance/road tax) set this way drive
the dashboard's countdowns just as the web Settings would.

> Claude Desktop's native "custom connector" flow expects OAuth; with a static token the `mcp-remote` bridge is
> the reliable path. If a future release accepts a bearer header directly, point the connector at `/mcp` with the
> token instead.

## 3. A future in-app chat

The tools are thin adapters over the shared application layer in `CarTracker.Domain` (the query/write services
and the derived-metrics service). An in-app chat driving a cloud or self-hosted model would bind the **same**
services as function/tool definitions — a second consumer of one brain, not a second copy of the logic.
