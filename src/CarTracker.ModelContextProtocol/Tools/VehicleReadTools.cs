using System.ComponentModel;
using CarTracker.Domain;
using ModelContextProtocol.Server;

namespace CarTracker.ModelContextProtocol.Tools;

/// <summary>
/// Read tools that answer "which cars are there / what is this car". They call <see cref="IDerivedMetricsService"/>
/// — the same instance the web API uses — so the assistant and the dashboard cannot diverge.
/// </summary>
[McpServerToolType]
public sealed class VehicleReadTools
{
    /// <summary>
    /// The transport-proving tool (spec task 1.3) and the natural first call in any conversation: the assistant
    /// lists the garage to disambiguate which vehicle the owner means.
    /// </summary>
    [McpServerTool(Name = "list_vehicles")]
    [Description(
        "List every vehicle in the garage — registration, name, status, whether it is the default vehicle, and " +
        "current mileage — so you can tell which car the owner means. Takes no arguments. Call this first when a " +
        "request could apply to more than one vehicle.")]
    public static async Task<McpResult<IReadOnlyList<VehicleListItem>>> ListVehicles(
        IDerivedMetricsService metrics,
        CancellationToken cancellationToken)
    {
        var garage = await metrics.GetGarageAsync(cancellationToken);

        var items = garage
            .Select(v => new VehicleListItem(
                v.Registration,
                v.Name,
                v.Status.ToString(),
                v.IsDefault,
                v.CurrentMileage))
            .ToList();

        var summary = items.Count switch
        {
            0 => "No vehicles in the garage yet.",
            1 => $"1 vehicle: {items[0].Registration} ({items[0].Name}).",
            _ => $"{items.Count} vehicles: {string.Join(", ", items.Select(i => i.Registration))}.",
        };

        return new McpResult<IReadOnlyList<VehicleListItem>>(summary, items);
    }
}

/// <summary>One garage row, slimmed to what disambiguation needs (spec §5.2 <c>list_vehicles</c>).</summary>
public sealed record VehicleListItem(
    string Registration,
    string Name,
    string Status,
    bool IsDefault,
    int? CurrentMileage);
