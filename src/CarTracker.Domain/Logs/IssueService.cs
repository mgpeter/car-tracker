using CarTracker.Data;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Logs;

/// <summary>
/// The issue add + observation paths the REST endpoint and the MCP tools share. Recording an observation
/// (last-checked + current note) is the watchlist's whole point — noticing something has been worsening — and is
/// the one issue "safe update" the assistant makes; general edit and delete stay in the endpoint.
/// </summary>
public sealed class IssueService(CarTrackerDbContext context)
{
    public async Task<WriteResult<IssueItem>> AddAsync(
        int vehicleId, IssueInput input, EntrySource source, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.Title))
            return WriteResult<IssueItem>.Invalid("Title", "An issue needs a title.");

        var issue = new Issue
        {
            VehicleId = vehicleId,
            Title = input.Title.Trim(),
            Severity = input.Severity,
            FirstNoted = input.FirstNoted,
            LastChecked = input.LastChecked,
            CurrentObservation = input.CurrentObservation,
            ActionIfWorsens = input.ActionIfWorsens,
            EstimatedFixCost = input.EstimatedFixCost,
            Status = input.Status,
            Notes = input.Notes,
            Source = source,
        };

        context.Issues.Add(issue);
        await context.SaveChangesAsync(cancellationToken);
        return WriteResult<IssueItem>.Created(ToItem(issue));
    }

    /// <summary>Updates an issue's last-checked date and current observation — the watchlist's recurring note.</summary>
    public async Task<WriteResult<IssueItem>> AddObservationAsync(
        int vehicleId, int issueId, DateOnly? lastChecked, string? currentObservation,
        EntrySource source, CancellationToken cancellationToken = default)
    {
        var issue = await context.Issues
            .FirstOrDefaultAsync(i => i.Id == issueId && i.VehicleId == vehicleId, cancellationToken);
        if (issue is null) return WriteResult<IssueItem>.NotFound();

        issue.LastChecked = lastChecked ?? issue.LastChecked;
        issue.CurrentObservation = currentObservation ?? issue.CurrentObservation;
        await context.SaveChangesAsync(cancellationToken);
        return WriteResult<IssueItem>.Updated(ToItem(issue));
    }

    private static IssueItem ToItem(Issue i) => new(
        i.Id, i.Title, i.Severity, i.FirstNoted, i.LastChecked, i.CurrentObservation,
        i.ActionIfWorsens, i.EstimatedFixCost, i.Status, i.ResolvedDate, i.Notes);
}
