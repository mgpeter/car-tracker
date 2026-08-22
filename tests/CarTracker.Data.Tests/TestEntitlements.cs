using CarTracker.Domain.Accounts;

namespace CarTracker.Data.Tests;

/// <summary>
/// A plan stated in one line, for the many tests that need a service to construct and do not care what it
/// allows.
/// </summary>
/// <remarks>
/// <para>
/// <b>The default is the paid tier, deliberately.</b> Every test that existed before plans did was written
/// against an app with no ceilings, and the useful reading of those tests is "this works when nothing is in the
/// way". Defaulting to the free tier would have them all fail on an allowance they were never about, and the
/// noise would bury the two or three where the allowance is the point.
/// </para>
/// <para>
/// The tests that <i>are</i> about a ceiling say so with <see cref="Free"/> or an explicit number, and the
/// resolution itself - who is Free and who is Pro - is proved against a real database by
/// <c>AccountEntitlementsTests</c> using the real <see cref="AccountEntitlements"/>.
/// </para>
/// </remarks>
internal sealed class TestEntitlements(AccountPlan plan, PlanAllowances allowances) : IAccountEntitlements
{
    /// <summary>The paid tier: the assistant, and headroom on the other two.</summary>
    public static TestEntitlements Pro { get; } = new(
        AccountPlan.Pro,
        new PlanAllowances(ChatEnabled: true, DailyChatTokens: null, MaxDocuments: 2_000, DailyVehicleLookups: 50));

    /// <summary>The free tier, with the shipped numbers.</summary>
    public static TestEntitlements Free { get; } = new(
        AccountPlan.Free,
        new PlanAllowances(ChatEnabled: false, DailyChatTokens: 0, MaxDocuments: 100, DailyVehicleLookups: 3));

    /// <summary>A tier with the numbers a test wants, so a ceiling can be reached without seeding 100 rows.</summary>
    public static TestEntitlements With(
        bool chatEnabled = true,
        long? dailyChatTokens = null,
        int maxDocuments = 2_000,
        int dailyVehicleLookups = 50) =>
        new(
            chatEnabled ? AccountPlan.Pro : AccountPlan.Free,
            new PlanAllowances(chatEnabled, dailyChatTokens, maxDocuments, dailyVehicleLookups));

    public Task<AccountPlan> PlanAsync(CancellationToken cancellationToken = default) => Task.FromResult(plan);

    public Task<PlanAllowances> AllowancesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(allowances);
}
