using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Domain.Reminders;
using CarTracker.Shared.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// The fired reminders for one vehicle — the derived renewal and check state re-expressed, each with a reason.
/// </summary>
/// <remarks>
/// Reads <see cref="IDerivedMetricsService"/> and the pure <see cref="ReminderEvaluator"/>, never a second
/// query, so the list cannot disagree with the dashboard that computed the same due state. A dedicated endpoint
/// rather than the garage summary because the badge needs the reason strings and the renewal-plus-check
/// aggregation the garage counts do not carry.
/// </remarks>
public static class ReminderEndpoints
{
    public static IEndpointRouteBuilder MapReminderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/reminders").WithTags("Reminders");

        group.MapGet("/", GetRemindersAsync)
            .WithName("GetReminders")
            .WithSummary("Fired reminders with a reason each, and the badge count. includeQuiet also lists triggers evaluated but not firing.");

        return app;
    }

    /// <param name="includeQuiet">
    /// When true, also returns triggers that were evaluated but are not firing — the MOT at 359 days, the wash
    /// at day 14 — so a settings/dashboard view can show "would fire / quiet". Default false: only what fires.
    /// </param>
    private static async Task<Results<Ok<ReminderList>, NotFound<ProblemDetails>>> GetRemindersAsync(
        string registration,
        CarTrackerDbContext context,
        IDerivedMetricsService metrics,
        CancellationToken cancellationToken,
        bool includeQuiet = false)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var summary = await metrics.GetVehicleSummaryAsync(vehicleId.Value, cancellationToken);
        if (summary is null) return VehicleLookup.NotFound(registration);

        return TypedResults.Ok(ReminderEvaluator.Evaluate(summary, includeQuiet));
    }
}
