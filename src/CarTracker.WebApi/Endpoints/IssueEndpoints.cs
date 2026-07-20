using CarTracker.Data;
using CarTracker.Domain.Logs;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
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
        LogQueryService queries,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        return TypedResults.Ok(await queries.GetIssueLogAsync(vehicleId.Value, cancellationToken));
    }

    private static async Task<Results<Created<IssueItem>, NotFound<ProblemDetails>, ValidationProblem>> AddIssueAsync(
        string registration,
        AddIssueRequest request,
        CarTrackerDbContext context,
        IssueService issues,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var input = new IssueInput(
            request.Title, request.FirstNoted, request.Severity, request.Status, request.LastChecked,
            request.CurrentObservation, request.ActionIfWorsens, request.EstimatedFixCost, request.Notes);

        var result = await issues.AddAsync(vehicleId.Value, input, EntrySource.Web, cancellationToken);
        if (result is { Status: WriteStatus.Validation, Errors: { } errors })
            return TypedResults.ValidationProblem(errors);

        return TypedResults.Created($"/api/vehicles/{registration}/issues/{result.Value!.Id}", result.Value);
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
