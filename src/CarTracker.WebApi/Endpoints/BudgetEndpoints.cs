using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// Budget groups, and the variance computed against them.
/// </summary>
/// <remarks>
/// A budget group is a named target over one or more expense categories (a single-category budget is a group of
/// one). Only the target is stored; YTD actual, remaining and % used all derive from the member categories'
/// expense entries. New vehicles get four default groups (<see cref="BudgetGroupTemplate"/>) with no target set.
/// </remarks>
public static class BudgetEndpoints
{
    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/budget").WithTags("Budget");

        group.MapGet("/", GetBudgetAsync)
            .WithName("GetBudget")
            .WithSummary("Group targets against actuals for a period. Actuals are computed from the expenses; only the targets are stored.");

        group.MapPut("/groups", SetGroupsAsync)
            .WithName("SetBudgetGroups")
            .WithSummary("Sets the budget groups — name, optional target, and member categories. Send the full set — this replaces them.");

        return app;
    }

    /// <param name="period">
    /// Calendar year, rolling 12 months, or since purchase. The design's envelope toggle — the same expenses
    /// answered three ways, because "am I over for the year" and "what has this car cost me" are different
    /// questions.
    /// </param>
    private static async Task<Results<Ok<BudgetSummary>, NotFound<ProblemDetails>>> GetBudgetAsync(
        string registration,
        CarTrackerDbContext context,
        IDerivedMetricsService metrics,
        CancellationToken cancellationToken,
        BudgetPeriod period = BudgetPeriod.CalendarYear)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        var summary = await metrics.GetBudgetSummaryAsync(vehicleId.Value, period, cancellationToken);
        return summary is null ? VehicleLookup.NotFound(registration) : TypedResults.Ok(summary);
    }

    /// <remarks>
    /// <para>
    /// PUT, not PATCH: the groups are one document — their names, targets and memberships together. Editing them
    /// one at a time invites a category briefly in two groups (which the unique index would reject mid-edit), and
    /// the screen edits them as a set anyway.
    /// </para>
    /// <para>
    /// A group absent from the body is <b>removed</b>. A group with a null target is <b>tracked</b> — its spend is
    /// shown with no bar to fill; that is different from zero ("spend nothing here and tell me when you do") and
    /// from absent. Spend in a category that is in no group still appears, folded into "Everything else".
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<BudgetSummary>, NotFound<ProblemDetails>, ValidationProblem>> SetGroupsAsync(
        string registration,
        SetBudgetGroupsRequest request,
        CarTrackerDbContext context,
        IDerivedMetricsService metrics,
        CancellationToken cancellationToken)
    {
        var vehicleId = await VehicleLookup.FindIdAsync(context, registration, cancellationToken);
        if (vehicleId is null) return VehicleLookup.NotFound(registration);

        if (await ValidateAsync(request, context, cancellationToken) is { Count: > 0 } errors)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var existing = await context.BudgetGroups
            .Include(g => g.Categories)
            .Where(g => g.VehicleId == vehicleId.Value)
            .ToListAsync(cancellationToken);

        var wanted = request.Groups
            .Select((g, index) => (g, order: index + 1))
            .ToDictionary(x => x.g.Name, x => x, StringComparer.Ordinal);

        foreach (var row in existing)
        {
            if (wanted.TryGetValue(row.Name, out var match))
            {
                row.AnnualBudget = match.g.AnnualBudget;
                row.DisplayOrder = match.order;
                // Replace the membership set wholesale — the simplest correct diff, and the set is tiny.
                context.BudgetGroupCategories.RemoveRange(row.Categories);
                row.Categories = MembershipsFor(match.g, vehicleId.Value);
                wanted.Remove(row.Name);
            }
            else
            {
                // Not in the body: the owner removed this group. Its memberships cascade.
                context.BudgetGroups.Remove(row);
            }
        }

        foreach (var (g, order) in wanted.Values)
        {
            context.BudgetGroups.Add(new BudgetGroup
            {
                VehicleId = vehicleId.Value,
                Name = g.Name,
                AnnualBudget = g.AnnualBudget,
                DisplayOrder = order,
                Source = EntrySource.Web,
                Categories = MembershipsFor(g, vehicleId.Value),
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        var summary = await metrics.GetBudgetSummaryAsync(vehicleId.Value, request.Period, cancellationToken);
        return summary is null ? VehicleLookup.NotFound(registration) : TypedResults.Ok(summary);
    }

    private static List<BudgetGroupCategory> MembershipsFor(BudgetGroupInput input, int vehicleId) =>
        [.. input.Categories.Select(c => new BudgetGroupCategory { VehicleId = vehicleId, Category = c })];

    private static async Task<Dictionary<string, string[]>> ValidateAsync(
        SetBudgetGroupsRequest request,
        CarTrackerDbContext context,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        var duplicateNames = request.Groups
            .GroupBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateNames.Count > 0)
            errors[nameof(request.Groups)] = [$"Group names must be unique. Repeated: {string.Join(", ", duplicateNames)}."];

        if (request.Groups.Any(g => string.IsNullOrWhiteSpace(g.Name) || g.Name.Length > 40))
            errors["Name"] = ["A group needs a name of 40 characters or fewer."];

        if (request.Groups.Any(g => g.Categories.Count == 0))
            errors["Categories"] = ["Every group needs at least one category."];

        if (request.Groups.Any(g => g.AnnualBudget is < 0))
            errors["AnnualBudget"] = ["A target cannot be negative. Leave it empty for a tracked group, or zero to mean 'spend nothing here'."];

        // A category may belong to at most one group. The DB unique index would 500 otherwise; say so plainly.
        var categoryInTwoGroups = request.Groups
            .SelectMany(g => g.Categories)
            .GroupBy(c => c, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (categoryInTwoGroups.Count > 0)
            errors["Categories"] = [$"A category can be in only one group. In more than one: {string.Join(", ", categoryInTwoGroups)}."];

        var known = await context.ExpenseCategories.Select(c => c.Name).ToListAsync(cancellationToken);
        var unknown = request.Groups.SelectMany(g => g.Categories).Distinct().Except(known).ToList();
        if (unknown.Count > 0)
            errors["Category"] = [$"Not expense categories: {string.Join(", ", unknown)}. Add them in Settings first."];

        return errors;
    }
}

/// <param name="AnnualBudget">The target, or null for a tracked group (spend shown, no bar).</param>
/// <param name="Categories">The member categories. At least one, and none shared with another group.</param>
public sealed record BudgetGroupInput(string Name, decimal? AnnualBudget, IReadOnlyList<string> Categories);

/// <param name="Groups">The full set. A group left out is removed — see the endpoint's note.</param>
/// <param name="Period">Which period to compute the returned variance over. Does not affect what is stored.</param>
public sealed record SetBudgetGroupsRequest(
    IReadOnlyList<BudgetGroupInput> Groups,
    BudgetPeriod Period = BudgetPeriod.CalendarYear);
