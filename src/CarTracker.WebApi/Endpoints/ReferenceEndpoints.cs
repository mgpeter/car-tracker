using CarTracker.Data;
using CarTracker.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// Global reference data — the lists that are seeded once and shared by every vehicle.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a bug it would have prevented. The expenses sheet shipped with its category list
/// hardcoded in TypeScript, hand-guessed from the workbook: "Repairs", "Road tax", "Cleaning", "Other",
/// "Tools", "Tyres". The seeded names are <c>Repair</c>, <c>Tax</c>, <c>Wash</c>, <c>Misc</c> and
/// <c>Tools/Equipment</c>, and two of the guesses are not categories at all — so eight of the twelve options
/// the user could pick were rejected by the endpoint's own validation, which checks the name against this
/// table.
/// </para>
/// <para>
/// A seeded list is data. Duplicating it as a constant in another language is a copy that cannot be kept in
/// step, and the copy being wrong was invisible until someone tried to save. The front-end reads it from here.
/// </para>
/// </remarks>
public static class ReferenceEndpoints
{
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reference").WithTags("Reference");

        group.MapGet("/expense-categories", GetExpenseCategoriesAsync)
            .WithName("GetExpenseCategories")
            .WithSummary("The seeded expense categories, in display order. The same list the write path validates against.");

        return app;
    }

    private static async Task<Ok<List<ExpenseCategoryItem>>> GetExpenseCategoriesAsync(
        CarTrackerDbContext context,
        CancellationToken cancellationToken)
    {
        var categories = await context.ExpenseCategories
            .OrderBy(c => c.DisplayOrder)
            .Select(c => c.Name)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(
            categories.Select(name => new ExpenseCategoryItem(name, IsMirrorOnly: name == FuelEntryFactory.FuelCategory)).ToList());

    }
}

/// <param name="IsMirrorOnly">
/// True where the write path refuses hand-entry because the domain owns the row. Only <c>Fuel</c> today: a
/// typed fuel expense is the workbook's lumped "fuel to date" row, and that is the £163.16 gap.
///
/// Deliberately <b>not</b> <c>ExpenseCategory.IsSystem</c>, which is true for all thirteen and means "seeded,
/// undeletable" — a different question with a tempting name. Sourced from
/// <see cref="FuelEntryFactory.FuelCategory"/>, so the UI hides exactly what the API rejects.
///
/// A service cost mirrors too, but <c>Service</c> stays enterable: not every service has a record, and the
/// endpoint does not refuse it.
/// </param>
public sealed record ExpenseCategoryItem(string Name, bool IsMirrorOnly);
