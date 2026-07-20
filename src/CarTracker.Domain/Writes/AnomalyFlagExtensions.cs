using CarTracker.Data;
using CarTracker.Shared.Logs;

namespace CarTracker.Domain.Writes;

/// <summary>
/// Maps the <see cref="DataAnomaly"/> rows <c>AnomalyScanner.ScanAsync</c> returns to the <see cref="AnomalyFlag"/>
/// wire shape. One mapper the endpoints, the write services and the MCP tools share, replacing the copy that sat
/// in four endpoint files.
/// </summary>
public static class AnomalyFlagExtensions
{
    public static AnomalyFlag ToFlag(this DataAnomaly a) => new(a.Id, a.Kind, a.Severity, a.Message, a.Detail);

    public static IReadOnlyList<AnomalyFlag> ToFlags(this IEnumerable<DataAnomaly> anomalies) =>
        [.. anomalies.Select(ToFlag)];
}
