using CarTracker.Domain.Accounts;

namespace CarTracker.WebApi.Accounts;

/// <summary>
/// Keeps asking the identity provider about logins whose accounts are already gone.
/// </summary>
/// <remarks>
/// <para>
/// This is what turns a best effort into a promise. Deleting an account deletes the local data first and calls
/// Auth0 second, because every other ordering can strand a person's data behind a login they no longer have; the
/// cost of that choice is that a failed call leaves a live login with nothing behind it. Harmless is not erased,
/// and the difference is precisely what a regulator would ask about — so the failure is written to
/// <c>pending_identity_deletions</c> and this pass works the queue until it is empty.
/// </para>
/// <para>
/// Registered and shaped like <see cref="Reminders.RemindersBackgroundService"/>: a
/// <see cref="TimeProvider"/>-driven <see cref="PeriodicTimer"/> so a test can advance the clock, and the scoped
/// service resolved inside each tick through <see cref="IServiceScopeFactory"/> — capturing a scoped service in
/// a singleton's constructor is the classic hosted-service leak.
/// </para>
/// </remarks>
public sealed class IdentityDeletionRetryService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IConfiguration configuration,
    ILogger<IdentityDeletionRetryService> logger) : BackgroundService
{
    /// <summary>
    /// How often the queue is worked. Hourly by default — the failures this retries are outages and
    /// misconfigured grants, neither of which clears in seconds — overridable via
    /// <c>IdentityDeletion:RetryInterval</c>.
    /// </summary>
    private TimeSpan Interval =>
        configuration.GetValue<TimeSpan?>("IdentityDeletion:RetryInterval") ?? TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Once at startup, then on the interval: a restart is the commonest way a deployment gets its
        // Management credential, and waiting an hour to notice would be an hour of a login that should not exist.
        using var timer = new PeriodicTimer(Interval, timeProvider);
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var accounts = scope.ServiceProvider.GetRequiredService<AccountDeletionService>();

                var cleared = await accounts.RetryPendingAsync(stoppingToken);
                if (cleared > 0)
                    logger.LogInformation("Removed {Cleared} identity(ies) queued from earlier deletions.", cleared);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // One bad pass must not kill the job — the rows are still queued and the next tick tries again.
                logger.LogError(ex, "Identity-deletion retry pass failed; will retry next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
