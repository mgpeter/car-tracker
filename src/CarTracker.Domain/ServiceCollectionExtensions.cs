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
        services.AddScoped<Clock>();

        return services;
    }
}
