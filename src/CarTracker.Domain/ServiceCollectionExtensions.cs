using Microsoft.Extensions.DependencyInjection;

namespace CarTracker.Domain;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shared brain. Call from both the web API and the MCP host — the same instance of the
    /// same logic, which is the point of README §4.
    /// </summary>
    public static IServiceCollection AddCarTrackerDomain(this IServiceCollection services)
    {
        services.AddScoped<IVehicleMetricsLoader, VehicleMetricsLoader>();
        services.AddScoped<IDerivedMetricsService, DerivedMetricsService>();
        // Resolves an MCP tool's optional vehicle arg (registration or id) to a vehicle, default-first (DEC-007).
        services.AddScoped<VehicleResolver>();
        services.AddScoped<VehicleFactory>();
        // A fill is never one row: the entry, its odometer reading, and its mirrored expense (§3.2).
        services.AddScoped<FuelEntryFactory>();
        services.AddScoped<ServiceRecordFactory>();
        // README §3.3: turn a done Workshop task into a service record through the factory.
        services.AddScoped<TaskPromoter>();
        services.AddScoped<ReferenceWriter>();
        // The edit/remove half of the reference lists — rename with cascade, guarded delete, re-home.
        services.AddScoped<ReferenceListEditor>();
        // The production caller AnomalyDetector never had. Every write path runs it.
        services.AddScoped<AnomalyScanner>();
        services.AddScoped<Clock>();

        // Shared application services — the read + add paths the REST endpoints and the MCP tools both call, so
        // a screen's list projection and its write invariants live in one place (spec §5, DEC-014).
        services.AddScoped<Expenses.ExpenseService>();
        services.AddScoped<Logs.LogQueryService>();
        services.AddScoped<Logs.LogWriteService>();
        services.AddScoped<Logs.TaskService>();
        services.AddScoped<Logs.IssueService>();
        services.AddScoped<Logs.CheckService>();
        services.AddScoped<Vehicles.VehicleUpdateService>();

        // The audit sink defaults to a no-op (tests, non-MCP callers); the WebApi host replaces it with the real
        // one that attributes a write to the token that made it.
        services.AddScoped<Writes.IAssistantAudit, Writes.NullAssistantAudit>();

        // Reminders: the dispatcher reads the shared brain and fans out to whatever channels are registered.
        // The channels themselves (the in-app badge now, email/push later) are registered by the host.
        services.AddScoped<Reminders.ReminderDispatcher>();

        return services;
    }
}
