using CarTracker.Domain.Reminders;

namespace CarTracker.WebApi.Reminders;

/// <summary>
/// The hosted job README §4 names: it wakes on an interval, evaluates every vehicle's reminders through the
/// shared brain, and dispatches what fired to every enabled channel.
/// </summary>
/// <remarks>
/// With only the in-app badge registered this is effectively a no-op push — the pass still runs, still
/// evaluates, and is where email or ntfy plug in once DEC-006 decides. It uses <see cref="TimeProvider"/> so a
/// test can advance the clock, and resolves the scoped dispatcher (and its <c>DbContext</c>) inside each tick
/// via <see cref="IServiceScopeFactory"/> — capturing a scoped service in this singleton's constructor is the
/// classic hosted-service leak.
/// </remarks>
public sealed class RemindersBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<RemindersBackgroundService> logger) : BackgroundService
{
    /// <summary>
    /// How often the digest runs. A day by default — the design's "daily digest" — overridable via
    /// <c>Reminders:Interval</c> (e.g. a short span in tests or dev).
    /// </summary>
    private TimeSpan Interval =>
        configuration.GetValue<TimeSpan?>("Reminders:Interval") ?? TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Evaluate once at startup, then on the interval. Waiting a whole day before the first pass would leave
        // a freshly-started app silent about something already overdue.
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad tick must not kill the job — the next interval tries again.
                logger.LogError(ex, "Reminders pass failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<ReminderDispatcher>();
        var fired = await dispatcher.RunOnceAsync(cancellationToken);
        logger.LogInformation("Reminders pass complete: {Fired} firing reminder(s) across the fleet.", fired);
    }
}
