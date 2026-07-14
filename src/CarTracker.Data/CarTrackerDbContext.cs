using Microsoft.EntityFrameworkCore;

namespace CarTracker.Data;

public class CarTrackerDbContext(DbContextOptions<CarTrackerDbContext> options, TimeProvider timeProvider)
    : DbContext(options)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();

    public DbSet<Garage> Garages => Set<Garage>();

    public DbSet<WashLocation> WashLocations => Set<WashLocation>();

    public DbSet<MileageReading> MileageReadings => Set<MileageReading>();

    public DbSet<FuelEntry> FuelEntries => Set<FuelEntry>();

    public DbSet<ExpenseEntry> ExpenseEntries => Set<ExpenseEntry>();

    public DbSet<ServiceRecord> ServiceRecords => Set<ServiceRecord>();

    public DbSet<TyreReading> TyreReadings => Set<TyreReading>();

    public DbSet<WashEntry> WashEntries => Set<WashEntry>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Both of these live here rather than at the composition root so they cannot be forgotten by a
        // caller. Omitting the naming convention would silently produce quoted PascalCase tables, and
        // omitting the interceptor would silently produce unaudited writes — neither fails loudly.
        optionsBuilder.UseSnakeCaseNamingConvention();
        optionsBuilder.AddInterceptors(new AuditStampingInterceptor(timeProvider));

        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CarTrackerDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
