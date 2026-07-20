using CarTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain;

/// <summary>
/// Resolves an MCP tool's optional <c>vehicle</c> argument (a registration or an id) to a vehicle, falling back
/// to the default vehicle when it is omitted (DEC-007). An unknown value is <c>null</c> — a tool turns that into
/// an error, never a guess (README §5.2).
/// </summary>
/// <remarks>
/// The web API resolves registrations its own way in <c>VehicleLookup</c>, which lives in the WebApi and cannot
/// be reached from here. This is the domain-side equivalent the MCP tools and the query services share, using
/// the same <c>upper(replace(registration,' ',''))</c> normalisation the database's generated column applies, so
/// "BT53 AKJ" and "bt53akj" resolve to one car exactly as every screen does.
/// </remarks>
public sealed class VehicleResolver(CarTrackerDbContext context, IVehicleMetricsLoader loader)
{
    public async Task<VehicleRef?> ResolveAsync(string? vehicle, CancellationToken cancellationToken = default)
    {
        // Omitted → the default vehicle. ListVehicleIdsAsync yields the default first (DEC-007).
        if (string.IsNullOrWhiteSpace(vehicle))
        {
            var ids = await loader.ListVehicleIdsAsync(cancellationToken);
            return ids.Count == 0 ? null : await FindAsync(v => v.Id == ids[0], cancellationToken);
        }

        // A UK plate is never a bare integer, so a parseable value is an id; otherwise it is a registration.
        if (int.TryParse(vehicle, out var id) &&
            await FindAsync(v => v.Id == id, cancellationToken) is { } byId)
        {
            return byId;
        }

        var normalized = Normalize(vehicle);
        return await FindAsync(v => EF.Property<string>(v, "RegistrationNormalized") == normalized, cancellationToken);
    }

    private Task<VehicleRef?> FindAsync(
        System.Linq.Expressions.Expression<Func<Vehicle, bool>> predicate,
        CancellationToken cancellationToken) =>
        context.Vehicles
            .AsNoTracking()
            .Where(predicate)
            // Name mirrors VehicleSummary.Name — "Make Model" — so a tool's human summary reads the same as the
            // dashboard's title rather than inventing a second label.
            .Select(v => new VehicleRef(v.Id, v.Registration, (v.Make + " " + v.Model).Trim()))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Mirrors the database's <c>upper(replace(registration, ' ', ''))</c> generated column.</summary>
    public static string Normalize(string registration) =>
        registration.Replace(" ", string.Empty).ToUpperInvariant();
}

/// <summary>A resolved vehicle: its id and the display facts a tool's human summary needs.</summary>
public sealed record VehicleRef(int VehicleId, string Registration, string Name);
