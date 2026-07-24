using Microsoft.EntityFrameworkCore;

namespace CarTracker.Data;

public class CarTrackerDbContext(
    DbContextOptions<CarTrackerDbContext> options,
    TimeProvider timeProvider,
    ICurrentUserAccessor? currentUser = null)
    : DbContext(options)
{
    // Read by the vehicle query filter below. Instance members, deliberately: EF re-evaluates a query filter's
    // reference to a context member on every query using the live context, so the filter tracks the current
    // request's user instead of freezing the first one it saw. A null accessor (tests, design-time, background
    // jobs) bypasses — see ICurrentUserAccessor. Kept private; the filter lambda is defined in this class.
    private bool BypassOwnership => currentUser?.BypassOwnership ?? true;
    private int? CurrentOwnerId => currentUser?.OwnerId;

    public DbSet<User> Users => Set<User>();

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

    public DbSet<CheckDefinition> CheckDefinitions => Set<CheckDefinition>();

    public DbSet<CheckLog> CheckLogs => Set<CheckLog>();

    public DbSet<MaintenanceTask> MaintenanceTasks => Set<MaintenanceTask>();

    public DbSet<BudgetCategory> BudgetCategories => Set<BudgetCategory>();

    public DbSet<Issue> Issues => Set<Issue>();

    public DbSet<EquipmentItem> EquipmentItems => Set<EquipmentItem>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DataAnomaly> DataAnomalies => Set<DataAnomaly>();

    public DbSet<AssistantToken> AssistantTokens => Set<AssistantToken>();

    public DbSet<AssistantWriteAudit> AssistantWriteAudits => Set<AssistantWriteAudit>();

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

        // Multi-user isolation, enforced in one place. Every vehicle read is scoped to the signed-in owner;
        // a system/background context bypasses (BypassOwnership). Because every other entity is reached only
        // through a vehicle id that was itself resolved through this filter, scoping the vehicle scopes the
        // whole chain — a new endpoint cannot forget to filter. A cross-user or unowned vehicle simply does
        // not resolve, so the endpoint 404s rather than leaking that it exists. The first-login claim and any
        // system move use IgnoreQueryFilters() deliberately.
        modelBuilder.Entity<Vehicle>().HasQueryFilter(v => BypassOwnership || v.OwnerId == CurrentOwnerId);

        base.OnModelCreating(modelBuilder);
    }
}
