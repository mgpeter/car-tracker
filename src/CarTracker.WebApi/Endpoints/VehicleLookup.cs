using CarTracker.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// Resolves a registration in the URL to a vehicle id.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the API's job, not the domain's. <see cref="CarTracker.Domain.IDerivedMetricsService"/> takes
/// a vehicle id and stays a pure id-keyed API — the MCP server will resolve registrations its own way
/// (README §5.2), and pushing lookup into the domain would give two callers two different ideas of what a
/// registration means.
/// </para>
/// <para>
/// Shared rather than copied per endpoint file: every vehicle-scoped route needs it, and a second normaliser
/// that drifted from the database's generated column would make "BT53 AKJ" and "bt53akj" different cars on
/// some screens and the same car on others.
/// </para>
/// </remarks>
public static class VehicleLookup
{
    public static Task<int?> FindIdAsync(
        CarTrackerDbContext context,
        string registration,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(registration);

        return context.Vehicles
            .AsNoTracking()
            // The shadow generated column the unique index is built on, so this matches exactly what the
            // database considers a duplicate: "bt53akj" and "BT53 AKJ" are the same vehicle.
            .Where(v => EF.Property<string>(v, "RegistrationNormalized") == normalized)
            .Select(v => (int?)v.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// The whole vehicle, for the one screen that reads stored facts rather than derived ones.
    /// </summary>
    /// <remarks>
    /// Tracked, unlike <see cref="FindIdAsync"/>: the caller may be about to PATCH it. Everything else resolves
    /// an id and goes through the domain, which is why this is the exception rather than the pattern.
    /// </remarks>
    public static Task<Vehicle?> FindAsync(
        CarTrackerDbContext context,
        string registration,
        CancellationToken cancellationToken)
    {
        var normalized = Normalize(registration);

        return context.Vehicles
            .Where(v => EF.Property<string>(v, "RegistrationNormalized") == normalized)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Mirrors the database's <c>upper(replace(registration, ' ', ''))</c> generated column.</summary>
    public static string Normalize(string registration) =>
        registration.Replace(" ", string.Empty).ToUpperInvariant();

    /// <remarks>
    /// ProblemDetails rather than an anonymous <c>{ message }</c>: an anonymous type has no schema, so it
    /// generates as <c>unknown</c> and the front end cannot read the reason it was refused.
    /// </remarks>
    public static NotFound<ProblemDetails> NotFound(string registration) =>
        TypedResults.NotFound(new ProblemDetails
        {
            Title = "Vehicle not found",
            Detail = $"No vehicle with registration '{registration}'.",
            Status = StatusCodes.Status404NotFound,
        });
}
