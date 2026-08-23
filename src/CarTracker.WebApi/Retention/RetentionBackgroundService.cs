using CarTracker.Domain.Retention;

namespace CarTracker.WebApi.Retention;

/// <summary>
/// Prunes the operational ledgers on a schedule, so the retention period the privacy page states is one the
/// deployment actually keeps (DEC-024).
/// </summary>
/// <remarks>
/// <para>
/// Built on <c>RemindersBackgroundService</c>'s shape: a <see cref="PeriodicTimer"/> over
/// <see cref="TimeProvider"/> so a test can advance the clock, a scope resolved inside each tick via
/// <see cref="IServiceScopeFactory"/> because capturing a scoped <c>DbContext</c> in a singleton's constructor
/// is the classic hosted-service leak, and a try/catch that keeps one bad tick from killing the job.
/// </para>
/// <para>
/// <b>It logs what it removed, per table, rather than that it ran.</b> A prune that silently removes nothing
/// and a prune that silently removes everything produce the same line otherwise, and this is the one job in
/// the application whose entire purpose is deleting other people's rows unattended.
/// </para>
/// <para>
/// <b>It runs on every deployment, published documents or not.</b> Retention is a property of the data rather
/// than of whether a policy page renders, and tying the two would mean a self-hoster's ledgers grew for ever.
/// </para>
/// </remarks>
public sealed class RetentionBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    RetentionOptions options,
    ILogger<RetentionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.IsEnabled)
        {
            // Said once, at boot, rather than every interval: an operator who set Retention:LedgerDays=0 chose
            // this, and a nightly reminder of a deliberate choice is noise. But it is said, because a policy
            // page promising a window on a deployment that prunes nothing is the failure this job exists to
            // prevent, and silence would be indistinguishable from working.
            logger.LogWarning(
                "Retention pruning is OFF (Retention:LedgerDays=0). The ledgers will grow without bound, and "
                + "any published privacy policy stating a retention period is not being enforced here.");
            return;
        }

        // Prune once at startup, then on the interval. Waiting a whole day before the first pass would leave a
        // freshly-restarted container carrying rows it has already promised to have deleted.
        using var timer = new PeriodicTimer(options.ResolvedInterval, timeProvider);
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
                logger.LogError(ex, "Retention sweep failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<RetentionService>();
        var swept = await service.SweepAsync(cancellationToken);

        if (swept.Total == 0)
        {
            logger.LogInformation(
                "Retention sweep complete: nothing older than {Days} days. Ledgers are within the window.",
                options.ResolvedLedgerDays);
            return;
        }

        logger.LogInformation(
            "Retention sweep complete: removed {Chat} chat-usage, {Lookups} lookup-usage and {Audits} "
            + "assistant-write-audit row(s) older than {Days} days.",
            swept.ChatUsage, swept.VehicleLookupUsage, swept.AssistantWriteAudits, options.ResolvedLedgerDays);
    }
}
