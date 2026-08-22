using CarTracker.Domain.Accounts;
using CarTracker.Shared;

namespace CarTracker.Domain.Tests;

/// <summary>
/// What each tier allows, and what an operator is shown about the two of them.
/// </summary>
/// <remarks>
/// These moved out of <see cref="AccountEntitlements"/> when the diagnostics endpoint needed to render both
/// tiers without an account in hand. The numbers are the shipped defaults and the point of the class is that
/// naming one configuration key does not silently reset the other three.
/// </remarks>
public sealed class PlanAllowanceTests
{
    [Fact]
    public void The_shipped_defaults_are_what_DEC_022_specified()
    {
        var free = PlanResolver.Allowances(new PlanOptions(), AccountPlan.Free);
        var pro = PlanResolver.Allowances(new PlanOptions(), AccountPlan.Pro);

        Assert.False(free.ChatEnabled);
        Assert.Equal(0, free.DailyChatTokens);
        Assert.Equal(100, free.MaxDocuments);
        Assert.Equal(3, free.DailyVehicleLookups);

        Assert.True(pro.ChatEnabled);
        // Null on purpose: the paid tier names no ceiling of its own and defers to Chat:DailyTokensPerOwner,
        // so one ceiling is not written in two sections free to disagree.
        Assert.Null(pro.DailyChatTokens);
        Assert.Equal(2_000, pro.MaxDocuments);
        Assert.Equal(50, pro.DailyVehicleLookups);
    }

    [Fact]
    public void Naming_one_key_changes_one_number_and_inherits_the_rest()
    {
        // The failure this shape prevents: a whole-section replacement against a compose file that names only
        // the value somebody wanted to change, zeroing the three they did not.
        var options = new PlanOptions
        {
            Free = new PlanOptions.PlanLimits { MaxDocuments = 25 },
        };

        var free = PlanResolver.Allowances(options, AccountPlan.Free);

        Assert.Equal(25, free.MaxDocuments);
        Assert.Equal(3, free.DailyVehicleLookups);
        Assert.False(free.ChatEnabled);
        Assert.Equal(0, free.DailyChatTokens);
    }

    [Fact]
    public void Configuration_can_turn_the_assistant_on_for_the_free_tier()
    {
        // A private deployment comping nobody still wants its own assistant, and this is the key that does it
        // without the comp list. Asserted because it is the one override that changes what money is spent.
        var options = new PlanOptions
        {
            Free = new PlanOptions.PlanLimits { ChatEnabled = true, DailyChatTokens = 1_000 },
        };

        var free = PlanResolver.Allowances(options, AccountPlan.Free);

        Assert.True(free.ChatEnabled);
        Assert.Equal(1_000, free.DailyChatTokens);
    }
}
