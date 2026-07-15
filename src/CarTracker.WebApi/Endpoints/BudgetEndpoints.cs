using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// Budget targets, and the variance computed against them.
/// </summary>
/// <remarks>
/// The third table with no path to existence: <see cref="BudgetCategory"/> is vehicle-scoped, unseeded, and
/// was constructed nowhere — so the budget screen was structurally empty and <c>GetBudgetSummaryAsync</c> had
/// no production caller and no route. This is both.
/// </remarks>
public static class BudgetEndpoints
{
    public static IEndpointRouteBuilder MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vehicles/{registration}/budget").WithTags("Budget");

        group.MapGet("/", GetBudgetAsync)
            .WithName("GetBudget")
            .WithSummary("Targets against actuals for a period. Actuals are computed from the expenses; only the targets are stored.");

        group.MapPut("/targets", SetTargetsAsync)
            .WithName("SetBudgetTargets")
            .WithSummary("Sets the annual targets. Send the full set — this replaces them.");

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
    /// PUT, not PATCH: the targets are one document. Setting them one at a time invites a half-applied budget,
    /// and the screen edits them as a set anyway.
    /// </para>
    /// <para>
    /// A category absent from the body has its target <b>removed</b>, not zeroed — and those are different
    /// answers. Zero means "spend nothing on this and I want to know when you do"; absent means "I have not
    /// budgeted for this", which the design renders as "no budget · set one" and never hides. A category with
    /// spend and no target still appears; it is the honest state, not an omission.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<BudgetSummary>, NotFound<ProblemDetails>, ValidationProblem>> SetTargetsAsync(
        string registration,
        SetBudgetTargetsRequest request,
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

        var existing = await context.BudgetCategories
            .Where(b => b.VehicleId == vehicleId.Value)
            .ToListAsync(cancellationToken);

        var wanted = request.Targets.ToDictionary(t => t.Category, t => t.AnnualBudget);

        foreach (var row in existing)
        {
            if (wanted.TryGetValue(row.Category, out var amount))
            {
                row.AnnualBudget = amount;
                wanted.Remove(row.Category);
            }
            else
            {
                // Not in the body: the owner removed this target. See the note above on absent vs zero.
                context.BudgetCategories.Remove(row);
            }
        }

        foreach (var (category, amount) in wanted)
        {
            context.BudgetCategories.Add(new BudgetCategory
            {
                VehicleId = vehicleId.Value,
                Category = category,
                AnnualBudget = amount,
                Source = EntrySource.Web,
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        // The recomputed variance, since that is the only reason to set a target.
        var summary = await metrics.GetBudgetSummaryAsync(vehicleId.Value, request.Period, cancellationToken);
        return summary is null ? VehicleLookup.NotFound(registration) : TypedResults.Ok(summary);
    }

    private static async Task<Dictionary<string, string[]>> ValidateAsync(
        SetBudgetTargetsRequest request,
        CarTrackerDbContext context,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        var duplicates = request.Targets
            .GroupBy(t => t.Category)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            // The database's unique index on (VehicleId, Category) would refuse this anyway; saying so plainly
            // beats a 500 from a constraint the caller cannot see.
            errors[nameof(request.Targets)] = [$"One target per category. Repeated: {string.Join(", ", duplicates)}."];
        }

        if (request.Targets.Any(t => t.AnnualBudget < 0))
        {
            // Zero is meaningful — "I intend to spend nothing here, tell me when I do". Negative is not.
            errors["AnnualBudget"] = ["A target cannot be negative. Zero is allowed and means what it says."];
        }

        var known = await context.ExpenseCategories.Select(c => c.Name).ToListAsync(cancellationToken);
        var unknown = request.Targets.Select(t => t.Category).Distinct().Except(known).ToList();

        if (unknown.Count > 0)
        {
            errors["Category"] = [$"Not expense categories: {string.Join(", ", unknown)}. Add them in Settings first."];
        }

        return errors;
    }
}

public sealed record BudgetTarget(string Category, decimal AnnualBudget);

/// <param name="Targets">The full set. A category left out has its target removed — see the endpoint's note.</param>
/// <param name="Period">Which period to compute the returned variance over. Does not affect what is stored.</param>
public sealed record SetBudgetTargetsRequest(
    IReadOnlyList<BudgetTarget> Targets,
    BudgetPeriod Period = BudgetPeriod.CalendarYear);
