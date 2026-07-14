using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CarTracker.Data;

/// <summary>
/// Constructs the context for <c>dotnet ef</c> tooling only.
/// </summary>
/// <remarks>
/// Required because <see cref="CarTrackerDbContext"/> takes a <see cref="TimeProvider"/>, which EF's
/// default activator cannot supply. The tooling never saves entities, so the clock here is never read —
/// but it must be a real one, since the constructor does not accept null.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CarTrackerDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=cartracker;Username=postgres;Password=postgres";

    public CarTrackerDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CARTRACKER_CONNECTION") ?? FallbackConnectionString;

        var options = new DbContextOptionsBuilder<CarTrackerDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new CarTrackerDbContext(options, TimeProvider.System);
    }
}
