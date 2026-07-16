using CarTracker.Data;
using CarTracker.Shared;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// The issues watchlist — things wrong with the car that are not yet jobs.
/// </summary>
/// <remarks>
/// Distinct from <see cref="MaintenanceTask"/> on purpose: an issue is an observation being watched ("brake
/// pipe corrosion, advisory since 2024, not failure yet"), and a task is work someone intends to do. Collapsing
/// them loses the thing the watchlist is for — noticing that an observation has been getting worse for two
/// years — which is why <see cref="Issue.LastChecked"/> and <see cref="Issue.CurrentObservation"/> exist and a
/// task has no equivalent.
/// </remarks>
public static class IssueEndpoints
{
    public static IEndpointRouteBuilder MapIssueEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/issues").WithTags("Issues");

        group.MapGet("/", GetIssuesAsync).WithName("GetIssues")
            .WithSummary("The watchlist, worst first, with the derived worst-case cost of everything still monitored.");
        group.MapPost("/", AddIssueAsync).WithName("AddIssue");
        group.MapPatch("/{id:int}", UpdateIssueAsync).WithName("UpdateIssue");
        group.MapDelete("/{id:int}", DeleteIssueAsync).WithName("DeleteIssue");

        return app;
    }

    private static async Task<Results<Ok<IssueLog>, NotFound<ProblemDetails>>> GetIssuesAsync(
        string registration,
        CarTrackerDbContext context,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var issues = await context.Issues
            .Where(i => i.VehicleId == vehicleId.Value)
            .OrderBy(i => i.Status).ThenBy(i => i.Severity).ThenByDescending(i => i.FirstNoted)
            .Select(i => new IssueItem(
                i.Id, i.Title, i.Severity, i.FirstNoted, i.LastChecked, i.CurrentObservation,
                i.ActionIfWorsens, i.EstimatedFixCost, i.Status, i.ResolvedDate, i.Notes))
            .ToListAsync(cancellationToken);

        var monitoring = issues.Where(i => i.Status == IssueStatus.Monitoring).ToList();

        return TypedResults.Ok(new IssueLog(
            issues,
            MonitoringCount: monitoring.Count,
            ResolvedCount: issues.Count - monitoring.Count,
            // "Worst case £730" on the design's panel, hardcoded. This is the sum of what everything still
            // being watched would cost if it all had to be fixed.
            WorstCaseCost: monitoring.Sum(i => i.EstimatedFixCost ?? 0m)));
    }

    private static async Task<Results<Created<IssueItem>, NotFound<ProblemDetails>, ValidationProblem>> AddIssueAsync(
        string registration,
        AddIssueRequest request,
        CarTrackerDbContext context,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["An issue needs a title."],
            });
        }

        var issue = new Issue
        {
            VehicleId = vehicleId.Value,
            Title = request.Title.Trim(),
            Severity = request.Severity,
            FirstNoted = request.FirstNoted,
            LastChecked = request.LastChecked,
            CurrentObservation = request.CurrentObservation,
            ActionIfWorsens = request.ActionIfWorsens,
            EstimatedFixCost = request.EstimatedFixCost,
            Status = request.Status,
            Notes = request.Notes,
            Source = EntrySource.Web,
        };

        context.Issues.Add(issue);
        await context.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/vehicles/{registration}/issues/{issue.Id}", ToItem(issue));
    }

    private static async Task<Results<Ok<IssueItem>, NotFound<ProblemDetails>>> UpdateIssueAsync(
        string registration,
        int id,
        UpdateIssueRequest request,
        CarTrackerDbContext context,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var issue = await context.Issues
            .FirstOrDefaultAsync(i => i.Id == id && i.VehicleId == vehicleId.Value, cancellationToken);
        if (issue is null) return VehicleLookup.NotFound(registration);

        issue.Title = request.Title ?? issue.Title;
        issue.Severity = request.Severity ?? issue.Severity;
        issue.FirstNoted = request.FirstNoted ?? issue.FirstNoted;
        issue.LastChecked = request.LastChecked ?? issue.LastChecked;
        issue.CurrentObservation = request.CurrentObservation ?? issue.CurrentObservation;
        issue.ActionIfWorsens = request.ActionIfWorsens ?? issue.ActionIfWorsens;
        issue.EstimatedFixCost = request.EstimatedFixCost ?? issue.EstimatedFixCost;
        issue.Notes = request.Notes ?? issue.Notes;

        if (request.Status is { } status && status != issue.Status)
        {
            issue.Status = status;
            issue.ResolvedDate = status == IssueStatus.Resolved
                ? DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime)
                : null;
        }

        await context.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToItem(issue));
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>>> DeleteIssueAsync(
        string registration,
        int id,
        CarTrackerDbContext context,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var issue = await context.Issues
            .FirstOrDefaultAsync(i => i.Id == id && i.VehicleId == vehicleId.Value, cancellationToken);
        if (issue is null) return VehicleLookup.NotFound(registration);

        context.Issues.Remove(issue);
        await context.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static IssueItem ToItem(Issue i) => new(
        i.Id, i.Title, i.Severity, i.FirstNoted, i.LastChecked, i.CurrentObservation,
        i.ActionIfWorsens, i.EstimatedFixCost, i.Status, i.ResolvedDate, i.Notes);
}

/// <param name="ActionIfWorsens">The decision made in advance, so it is not made in a hurry at the roadside.</param>
public sealed record IssueItem(
    int Id,
    string Title,
    Severity Severity,
    DateOnly FirstNoted,
    DateOnly? LastChecked,
    string? CurrentObservation,
    string? ActionIfWorsens,
    decimal? EstimatedFixCost,
    IssueStatus Status,
    DateOnly? ResolvedDate,
    string? Notes);

public sealed record IssueLog(
    IReadOnlyList<IssueItem> Issues,
    int MonitoringCount,
    int ResolvedCount,
    decimal WorstCaseCost);

public sealed record AddIssueRequest(
    string Title,
    DateOnly FirstNoted,
    Severity Severity = Severity.Low,
    IssueStatus Status = IssueStatus.Monitoring,
    DateOnly? LastChecked = null,
    string? CurrentObservation = null,
    string? ActionIfWorsens = null,
    decimal? EstimatedFixCost = null,
    string? Notes = null);

public sealed record UpdateIssueRequest(
    string? Title = null,
    Severity? Severity = null,
    IssueStatus? Status = null,
    DateOnly? FirstNoted = null,
    DateOnly? LastChecked = null,
    string? CurrentObservation = null,
    string? ActionIfWorsens = null,
    decimal? EstimatedFixCost = null,
    string? Notes = null);
