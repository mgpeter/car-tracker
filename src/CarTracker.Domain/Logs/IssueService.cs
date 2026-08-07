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
public sealed class IssueService(CarTrackerDbContext context, Clock clock)
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
            // Stamped here, not left null. `ck_issues_resolved_date_iff_resolved` requires a resolved date iff
            // the status is Resolved, so adding an issue already Resolved — which is exactly how the
            // head-gasket item arrives, resolved off the May compression test — used to fail on the constraint
            // with a bare DbUpdateException. The PATCH path has always done this on a status change; the add
            // path never did, because nothing had yet posted a Resolved issue.
            ResolvedDate = input.Status == IssueStatus.Resolved ? clock.Today() : null,
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

    /// <summary>
    /// Replaces the set of checks an issue watches as its early warning. The list is authoritative: what is
    /// passed becomes the watch, an empty list clears it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The same-vehicle guard lives here.</b> The join table carries no vehicle column of its own, and a
    /// cross-table CHECK is not something Postgres can express without a trigger — so this is the honest place,
    /// and the caller already knows the vehicle. A link across vehicles would make one car's dashboard name a
    /// watch over another car's checks; it is a bug, not a feature, and it is refused rather than filtered so a
    /// caller passing a wrong id is told rather than quietly given a shorter watch.
    /// </para>
    /// <para>
    /// Retired definitions are accepted. A watch may legitimately name a check that is later retired — the read
    /// side already drops it from the contingency — and refusing here would make retiring a check fail an
    /// unrelated issue's next edit.
    /// </para>
    /// </remarks>
    public async Task<WriteResult<IssueItem>> SetWatchAsync(
        int vehicleId, int issueId, IReadOnlyList<int> checkDefinitionIds, CancellationToken cancellationToken = default)
    {
        var issue = await context.Issues
            .FirstOrDefaultAsync(i => i.Id == issueId && i.VehicleId == vehicleId, cancellationToken);
        if (issue is null) return WriteResult<IssueItem>.NotFound();

        var wanted = checkDefinitionIds.Distinct().ToList();

        if (wanted.Count > 0)
        {
            var mine = await context.CheckDefinitions
                .Where(d => d.VehicleId == vehicleId && wanted.Contains(d.Id))
                .Select(d => d.Id)
                .ToListAsync(cancellationToken);

            if (mine.Count != wanted.Count)
            {
                var stray = wanted.Except(mine).ToList();
                return WriteResult<IssueItem>.Invalid(
                    "WatchCheckDefinitionIds",
                    $"Check definition{(stray.Count == 1 ? "" : "s")} {string.Join(", ", stray)} "
                    + "do not belong to this vehicle. A watch can only name this car's own checks.");
            }
        }

        var existing = await context.IssueWatchChecks
            .Where(w => w.IssueId == issueId)
            .ToListAsync(cancellationToken);

        // Diff rather than delete-all-and-reinsert: the composite key means a re-added row is the same row, and
        // churning every link on every save would be writes for nothing.
        foreach (var gone in existing.Where(e => !wanted.Contains(e.CheckDefinitionId)))
            context.IssueWatchChecks.Remove(gone);

        var already = existing.Select(e => e.CheckDefinitionId).ToHashSet();
        foreach (var added in wanted.Where(id => !already.Contains(id)))
            context.IssueWatchChecks.Add(new IssueWatchCheck { IssueId = issueId, CheckDefinitionId = added });

        await context.SaveChangesAsync(cancellationToken);
        return WriteResult<IssueItem>.Updated(ToItem(issue));
    }

    private static IssueItem ToItem(Issue i) => new(
        i.Id, i.Title, i.Severity, i.FirstNoted, i.LastChecked, i.CurrentObservation,
        i.ActionIfWorsens, i.EstimatedFixCost, i.Status, i.ResolvedDate, i.Notes);
}
