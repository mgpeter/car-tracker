namespace CarTracker.Shared.Logs;

/// <summary>
/// An integrity flag a write raised, as a surface renders it. Returned by the write paths so a caller — the web
/// sheet or an MCP tool — can relay "logged, but this reading is below the last odometer, flagged" rather than
/// staying silent. Lifted to Shared so the endpoints, the write services and the MCP tools share one shape.
/// </summary>
public sealed record AnomalyFlag(int Id, AnomalyKind Kind, AnomalySeverity Severity, string Message, string? Detail);
