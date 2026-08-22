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

    public DbSet<BudgetGroup> BudgetGroups => Set<BudgetGroup>();
    public DbSet<BudgetGroupCategory> BudgetGroupCategories => Set<BudgetGroupCategory>();

    public DbSet<Issue> Issues => Set<Issue>();

    /// <summary>The checks an issue watches as its early-warning — see <see cref="IssueWatchCheck"/>.</summary>
    public DbSet<IssueWatchCheck> IssueWatchChecks => Set<IssueWatchCheck>();

    public DbSet<EquipmentItem> EquipmentItems => Set<EquipmentItem>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DataAnomaly> DataAnomalies => Set<DataAnomaly>();

    public DbSet<AssistantToken> AssistantTokens => Set<AssistantToken>();

    public DbSet<AssistantWriteAudit> AssistantWriteAudits => Set<AssistantWriteAudit>();

    /// <summary>Identities whose local account is gone and whose login is not — see <see cref="PendingIdentityDeletion"/>.</summary>
    public DbSet<PendingIdentityDeletion> PendingIdentityDeletions => Set<PendingIdentityDeletion>();

    /// <summary>
    /// What the in-app chat has spent, per account per day. <b>Unfiltered on purpose</b> — the global daily
    /// ceiling asks about every account at once, and an ownership filter would answer it with one account's
    /// usage while looking entirely correct. See <see cref="ChatUsage"/>.
    /// </summary>
    public DbSet<ChatUsage> ChatUsage => Set<ChatUsage>();

    /// <summary>
    /// How many DVLA lookups each account has made today. Unfiltered, matching <see cref="ChatUsage"/> - the
    /// per-owner read scopes itself explicitly, and one style across the two ledgers beats a filter that would
    /// have to be bypassed the first time somebody wants a deployment-wide total.
    /// </summary>
    public DbSet<VehicleLookupUsage> VehicleLookupUsage => Set<VehicleLookupUsage>();

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

        // The three reference lists are the one family of rows a vehicle does not lead to — they are keyed
        // (OwnerId, Name) and reached by name, so the chain above does not reach them and they need the filter
        // in their own right. With it, every read in ReferenceWriter, ReferenceListEditor and ReferenceEndpoints
        // becomes owner-scoped with no call-site change: `Garages.AnyAsync(g => g.Name == name)` now asks
        // "does *this account* have one", and another account's name simply does not resolve, so the editor's
        // existing NotFound path already produces the right answer.
        modelBuilder.Entity<Garage>().HasQueryFilter(g => BypassOwnership || g.OwnerId == CurrentOwnerId);
        modelBuilder.Entity<WashLocation>().HasQueryFilter(w => BypassOwnership || w.OwnerId == CurrentOwnerId);
        modelBuilder.Entity<ExpenseCategory>().HasQueryFilter(c => BypassOwnership || c.OwnerId == CurrentOwnerId);

        base.OnModelCreating(modelBuilder);
    }
}
