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
        services.AddScoped<VehicleFactory>();
        // A fill is never one row: the entry, its odometer reading, and its mirrored expense (§3.2).
        services.AddScoped<FuelEntryFactory>();
        services.AddScoped<ServiceRecordFactory>();
        services.AddScoped<ReferenceWriter>();
        // The production caller AnomalyDetector never had. Every write path runs it.
        services.AddScoped<AnomalyScanner>();
        services.AddScoped<Clock>();

        // Reminders: the dispatcher reads the shared brain and fans out to whatever channels are registered.
        // The channels themselves (the in-app badge now, email/push later) are registered by the host.
        services.AddScoped<Reminders.ReminderDispatcher>();

        return services;
    }
}
