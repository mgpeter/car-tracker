using CarTracker.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// Global reference data — the lists seeded once and shared by every vehicle (DEC-007), so these routes are
/// <b>not</b> vehicle-scoped. Rows are keyed by name.
/// </summary>
/// <remarks>
/// This began as one read (the expense-category list the expenses screen validates against, after a hardcoded
/// TypeScript copy of it rejected eight of twelve options). It now carries the edit/remove half of the lists:
/// a garage/wash-location/category is pointed at by foreign keys that look like free text, so a rename cascades
/// and a delete is guarded — see <see cref="ReferenceListEditor"/>, which does the FK-aware work.
/// </remarks>
public static class ReferenceEndpoints
{
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reference").WithTags("Reference");

        group.MapGet("/garages", GetGaragesAsync)
            .WithName("GetGarages").WithSummary("Garages with the count of records that reference each.");
        group.MapPost("/garages", CreateGarageAsync).WithName("CreateGarage");
        group.MapPatch("/garages/{name}", UpdateGarageAsync).WithName("UpdateGarage");
        group.MapDelete("/garages/{name}", DeleteGarageAsync).WithName("DeleteGarage");

        group.MapGet("/wash-locations", GetWashLocationsAsync)
            .WithName("GetWashLocations").WithSummary("Wash locations with the count of wash entries that reference each.");
        group.MapPost("/wash-locations", CreateWashLocationAsync).WithName("CreateWashLocation");
        group.MapPatch("/wash-locations/{name}", UpdateWashLocationAsync).WithName("UpdateWashLocation");
        group.MapDelete("/wash-locations/{name}", DeleteWashLocationAsync).WithName("DeleteWashLocation");

        group.MapGet("/expense-categories", GetExpenseCategoriesAsync)
            .WithName("GetExpenseCategories")
            .WithSummary("The expense categories in display order — the same list the write path validates against.");
        group.MapPatch("/expense-categories/{name}", UpdateCategoryAsync).WithName("UpdateExpenseCategory");
        group.MapDelete("/expense-categories/{name}", DeleteCategoryAsync).WithName("DeleteExpenseCategory");

        group.MapGet("/starter-checks", GetStarterChecks)
            .WithName("GetStarterChecks")
            .WithSummary("The generic starter set, in template order — the checks the add-vehicle sheet offers for selection.");

        return app;
    }

    // ---- Starter checks -----------------------------------------------------------------------------------

    /// <summary>
    /// Projects <see cref="CheckTemplate.Generic"/> straight to the wire. The same list <c>VehicleFactory</c>
    /// applies on create, so the add-vehicle picker cannot drift from what create actually does — the one source
    /// of truth, read twice. Vehicle-independent and static, so no editor and no database.
    /// </summary>
    private static Ok<IReadOnlyList<StarterCheckItem>> GetStarterChecks() =>
        TypedResults.Ok<IReadOnlyList<StarterCheckItem>>(
            [.. CheckTemplate.Generic.Select(i => new StarterCheckItem(i.Name, i.CadenceLabel, i.IntervalDays, i.Guidance))]);

    // ---- Garages ------------------------------------------------------------------------------------------

    private static async Task<Ok<IReadOnlyList<GarageRef>>> GetGaragesAsync(ReferenceListEditor editor, CancellationToken ct) =>
        TypedResults.Ok(await editor.ListGaragesAsync(ct));

    private static async Task<Results<Created, ValidationProblem, Conflict<ProblemDetails>>> CreateGarageAsync(
        CreateGarageRequest request, ReferenceListEditor editor, CancellationToken ct)
    {
        if (Blank(request.Name)) return NameRequired();
        var result = await editor.CreateGarageAsync(request.Name, request.Contact, request.Address, request.Notes, ct);
        return result.Status == ReferenceOpStatus.NameCollision
            ? Conflict($"A garage named '{request.Name}' already exists.")
            : TypedResults.Created($"/api/reference/garages/{Uri.EscapeDataString(request.Name)}");
    }

    private static async Task<Results<Ok, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> UpdateGarageAsync(
        string name, UpdateGarageRequest request, ReferenceListEditor editor, CancellationToken ct)
    {
        var result = await editor.UpdateGarageAsync(name, NullIfBlank(request.Name), request.Contact, request.Address, request.Notes, ct);
        return result.Status switch
        {
            ReferenceOpStatus.NotFound => NotFoundProblem("Garage not found", $"No garage named '{name}'."),
            ReferenceOpStatus.NameCollision => Conflict($"A garage named '{request.Name}' already exists."),
            _ => TypedResults.Ok(),
        };
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>, Conflict<ProblemDetails>, ValidationProblem>> DeleteGarageAsync(
        string name, ReferenceListEditor editor, CancellationToken ct, string? rehomeTo = null) =>
        DeleteResult(await editor.DeleteGarageAsync(name, NullIfBlank(rehomeTo), ct), name, "garage");

    // ---- Wash locations -----------------------------------------------------------------------------------

    private static async Task<Ok<IReadOnlyList<WashLocationRef>>> GetWashLocationsAsync(ReferenceListEditor editor, CancellationToken ct) =>
        TypedResults.Ok(await editor.ListWashLocationsAsync(ct));

    private static async Task<Results<Created, ValidationProblem, Conflict<ProblemDetails>>> CreateWashLocationAsync(
        CreateWashLocationRequest request, ReferenceListEditor editor, CancellationToken ct)
    {
        if (Blank(request.Name)) return NameRequired();
        var result = await editor.CreateWashLocationAsync(request.Name, request.Notes, ct);
        return result.Status == ReferenceOpStatus.NameCollision
            ? Conflict($"A wash location named '{request.Name}' already exists.")
            : TypedResults.Created($"/api/reference/wash-locations/{Uri.EscapeDataString(request.Name)}");
    }

    private static async Task<Results<Ok, NotFound<ProblemDetails>, Conflict<ProblemDetails>>> UpdateWashLocationAsync(
        string name, UpdateWashLocationRequest request, ReferenceListEditor editor, CancellationToken ct)
    {
        var result = await editor.UpdateWashLocationAsync(name, NullIfBlank(request.Name), request.Notes, ct);
        return result.Status switch
        {
            ReferenceOpStatus.NotFound => NotFoundProblem("Wash location not found", $"No wash location named '{name}'."),
            ReferenceOpStatus.NameCollision => Conflict($"A wash location named '{request.Name}' already exists."),
            _ => TypedResults.Ok(),
        };
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>, Conflict<ProblemDetails>, ValidationProblem>> DeleteWashLocationAsync(
        string name, ReferenceListEditor editor, CancellationToken ct, string? rehomeTo = null) =>
        DeleteResult(await editor.DeleteWashLocationAsync(name, NullIfBlank(rehomeTo), ct), name, "wash location");

    // ---- Expense categories -------------------------------------------------------------------------------

    private static async Task<Ok<List<ExpenseCategoryItem>>> GetExpenseCategoriesAsync(ReferenceListEditor editor, CancellationToken ct)
    {
        var categories = await editor.ListCategoriesAsync(ct);
        return TypedResults.Ok(categories
            .Select(c => new ExpenseCategoryItem(c.Name, c.IsMirrorOnly, c.IsSystem, c.ReferenceCount))
            .ToList());
    }

    private static async Task<Results<Ok, NotFound<ProblemDetails>, Conflict<ProblemDetails>, ValidationProblem>> UpdateCategoryAsync(
        string name, UpdateCategoryRequest request, ReferenceListEditor editor, CancellationToken ct)
    {
        var result = await editor.UpdateCategoryAsync(name, NullIfBlank(request.Name), request.DisplayOrder, ct);
        return result.Status switch
        {
            ReferenceOpStatus.NotFound => NotFoundProblem("Category not found", $"No expense category named '{name}'."),
            ReferenceOpStatus.NameCollision => Conflict($"An expense category named '{request.Name}' already exists."),
            ReferenceOpStatus.FuelRenameLocked => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Name"] = ["The Fuel category cannot be renamed — the fuel-to-expense mirror resolves it by name."],
            }),
            _ => TypedResults.Ok(),
        };
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>, Conflict<ProblemDetails>, ValidationProblem>> DeleteCategoryAsync(
        string name, ReferenceListEditor editor, CancellationToken ct, string? rehomeTo = null)
    {
        var result = await editor.DeleteCategoryAsync(name, NullIfBlank(rehomeTo), ct);
        if (result.Status == ReferenceOpStatus.SystemLocked)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Category"] = ["A system category is seeded and undeletable. Rename it for display instead."],
            });
        }

        return DeleteResult(result, name, "category");
    }

    // ---- Shared -------------------------------------------------------------------------------------------

    private static Results<NoContent, NotFound<ProblemDetails>, Conflict<ProblemDetails>, ValidationProblem> DeleteResult(
        ReferenceOpResult result, string name, string noun) =>
        result.Status switch
        {
            ReferenceOpStatus.NotFound => NotFoundProblem($"{Capitalise(noun)} not found", $"No {noun} named '{name}'."),
            ReferenceOpStatus.Referenced => Conflict(
                $"{result.ReferenceCount} record{(result.ReferenceCount == 1 ? "" : "s")} use this {noun}; re-home them first, or pass ?rehomeTo=."),
            ReferenceOpStatus.BadRehomeTarget => TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["rehomeTo"] = ["The re-home target must be a different, existing row."],
            }),
            _ => TypedResults.NoContent(),
        };

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static ValidationProblem NameRequired() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Name"] = ["A name is required."] });

    private static NotFound<ProblemDetails> NotFoundProblem(string title, string detail) =>
        TypedResults.NotFound(new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
            Title = title,
            Status = StatusCodes.Status404NotFound,
            Detail = detail,
        });

    private static Conflict<ProblemDetails> Conflict(string detail) =>
        TypedResults.Conflict(new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
            Title = "Conflict",
            Status = StatusCodes.Status409Conflict,
            Detail = detail,
        });

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}

public sealed record CreateGarageRequest(string Name, string? Contact = null, string? Address = null, string? Notes = null);
public sealed record UpdateGarageRequest(string? Name = null, string? Contact = null, string? Address = null, string? Notes = null);
public sealed record CreateWashLocationRequest(string Name, string? Notes = null);
public sealed record UpdateWashLocationRequest(string? Name = null, string? Notes = null);
public sealed record UpdateCategoryRequest(string? Name = null, int? DisplayOrder = null);

/// <param name="IsMirrorOnly">
/// True where the write path refuses hand-entry because the domain owns the row — only <c>Fuel</c>, sourced
/// from <see cref="FuelEntryFactory.FuelCategory"/>. A different question from <paramref name="IsSystem"/>.
/// </param>
/// <param name="IsSystem">Seeded, and therefore undeletable. May be renamed for display (except Fuel).</param>
/// <param name="ReferenceCount">Expense and budget rows pointing at this category — what a delete would strand.</param>
public sealed record ExpenseCategoryItem(string Name, bool IsMirrorOnly, bool IsSystem, int ReferenceCount);

/// <summary>One generic starter check as the add-vehicle sheet shows it — cadence is display-only there.</summary>
public sealed record StarterCheckItem(string Name, string CadenceLabel, int IntervalDays, string? Guidance);
