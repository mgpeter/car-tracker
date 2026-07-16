namespace CarTracker.Shared.Metrics;

/// <summary>
/// The headline of the data-integrity queue: how many flags are open, and how bad the worst one is.
/// </summary>
/// <remarks>
/// <para>
/// A count and a severity, deliberately <b>not</b> the flags themselves. The dashboard's integrity panel needs
/// only "are there any, and should I hurry"; the queue at <c>GET /anomalies</c> carries the full list with each
/// flag's detail. Putting that list on the summary would make every reader of a headline figure — the garage
/// card, <c>get_vehicle_summary</c> over MCP — pay to load flags it will not show.
/// </para>
/// <para>
/// This is the field that lets the dashboard render an integrity panel at all: until it existed, the summary
/// could say a vehicle's mileage and MPG but not whether the app believed them.
/// </para>
/// </remarks>
/// <param name="OpenCount">Open flags only. Resolved ones are history, and the panel is about work to do.</param>
/// <param name="HighestSeverity">
/// The worst open flag's severity, or null when there are none. Error outranks Warning outranks Info — so the
/// panel can lead with "an error" rather than making the reader open the queue to find out.
/// </param>
public sealed record IntegritySummary(int OpenCount, AnomalySeverity? HighestSeverity);
