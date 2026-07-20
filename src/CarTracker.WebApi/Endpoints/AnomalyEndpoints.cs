using CarTracker.Data;
using CarTracker.Domain.Logs;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// The integrity queue — the first reader of a flag's content.
/// </summary>
/// <remarks>
/// <para>
/// <c>AnomalyScanner</c> has written flags on every write path since M1a, and
/// <c>CountOpenAnomaliesAsync</c> already reads the count for the garage card. So the gap this closes is
/// narrower than "never read" and worse in kind: the app could say <b>how many</b> flags a vehicle had and
/// nothing whatever about <b>what any of them was</b> — a badge with no page behind it.
/// </para>
/// <para>
/// Flag, never block (§5.3). Nothing here rejects data; it records what was decided about data already saved.
/// </para>
/// </remarks>
public static class AnomalyEndpoints
{
    public static IEndpointRouteBuilder MapAnomalyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/anomalies").WithTags("Data integrity");

        group.MapGet("/", GetAnomaliesAsync)
            .WithName("GetAnomalies")
            .WithSummary("The integrity queue. Open flags by default; ?status=all includes resolved ones.");

        group.MapPatch("/{id:int}", ResolveAnomalyAsync)
            .WithName("ResolveAnomaly")
            .WithSummary("Resolves a flag as Corrected, Accepted or Dismissed, with a note.");

        return app;
    }

    private static async Task<Results<Ok<List<AnomalyItem>>, NotFound<ProblemDetails>>> GetAnomaliesAsync(
        string registration,
        CarTrackerDbContext context,
        LogQueryService queries,
        CancellationToken cancellationToken,
        string? status = null)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        // Open by default: the queue's job is work to do, not history. `?status=all` is how you audit what was
        // decided, which is a different question asked deliberately.
        var includeResolved = string.Equals(status, "all", StringComparison.OrdinalIgnoreCase);

        return TypedResults.Ok(await queries.ListAnomaliesAsync(vehicleId.Value, includeResolved, cancellationToken));
    }

    private static async Task<Results<Ok<AnomalyItem>, NotFound<ProblemDetails>, ValidationProblem>> ResolveAnomalyAsync(
        string registration,
        int id,
        ResolveAnomalyRequest request,
        CarTrackerDbContext context,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var anomaly = await context.DataAnomalies
            .FirstOrDefaultAsync(a => a.Id == id && a.VehicleId == vehicleId.Value, cancellationToken);
        if (anomaly is null) return VehicleLookup.NotFound(registration);

        // Open is not a resolution. The check constraint `ck_anomalies_resolved_iff_terminal` would reject the
        // row anyway; saying so here is the difference between a 400 that explains itself and a 500.
        if (request.Status == AnomalyStatus.Open)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["status"] = ["Open is not a resolution. Use Corrected, Accepted or Dismissed."],
            });
        }

        anomaly.Status = request.Status;
        anomaly.ResolutionNote = request.ResolutionNote;
        // From the clock, never the caller: two surfaces supplying their own "now" is how they come to
        // disagree about when something happened.
        anomaly.ResolvedAt = timeProvider.GetUtcNow();

        await context.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new AnomalyItem(
            anomaly.Id, anomaly.Kind, anomaly.Severity, anomaly.EntityType, anomaly.EntityId, anomaly.Message,
            anomaly.Detail, anomaly.Status, anomaly.ResolvedAt, anomaly.ResolutionNote, anomaly.CreatedAt));
    }
}

/// <param name="Status">
/// <b>Corrected re-raises; Accepted and Dismissed do not.</b> The distinction is the whole lifecycle:
/// "I fixed it" is a claim the detector re-checks, and if the condition returns the fix did not hold. "I know,
/// and it is fine" is a decision, and re-asking would make the queue a nag. That rule lives in
/// <c>AnomalyDetector</c> and is deliberately not re-implemented here.
/// </param>
public sealed record ResolveAnomalyRequest(AnomalyStatus Status, string? ResolutionNote = null);
