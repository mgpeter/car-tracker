# Spec Summary (Lite)

Add an explicit `clear` list of `VehicleField` values to the vehicle PATCH, so the Vehicle screen can clear a
stored value instead of only overwriting it. A blank field that had a value is sent as a clear; null elsewhere
still means "leave unchanged", which keeps the MCP tools working as they do now.
