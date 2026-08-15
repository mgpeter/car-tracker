# Product Mission (Lite)

Car Tracker is a self-hosted vehicle maintenance and cost-tracking application that helps a hands-on car owner keep their vehicles alive and affordable by computing every figure live from the underlying logs and exposing that same domain to an AI assistant over MCP.

It serves owner-operators - accounts are real, and a vehicle belongs to the person who added it (Auth0, DEC-016) - the founding one being a 2003 Land Rover Freelander, currently tracked in a 13-sheet spreadsheet whose stored derived figures have drifted out of sync with reality; each owner's garage holds any number of vehicles, each fully scoped (DEC-007). Unlike that spreadsheet - and unlike trackers that cache totals for speed - no derived number has a column to go stale in: one domain service computes them on read, and both the web UI and the in-process MCP server call it, so a metric can never disagree with itself across surfaces.
