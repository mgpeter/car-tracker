using CarTracker.Domain.Accounts;
using CarTracker.Shared;

namespace CarTracker.Domain.Tests;

/// <summary>
/// The plan ladder, tested where it lives now that two callers climb it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The order of the rungs is the whole value of the reason</b>, which is the sentence
/// <see cref="AccountEntitlements"/> has carried since 0.24.1 and the reason this was extracted rather than
/// copied: the admin list resolves a plan for every account and entitlement resolves one for the request's
/// own, and a second copy of an ordering that matters is a second chance to get it wrong.
/// </para>
/// <para>
/// The two caller-shaped cases - no resolved owner, no user row - deliberately stay in
/// <see cref="AccountEntitlements"/> and are not tested here. "There is nobody to ask about" is not a fact
/// about a user, and a function taking a user's fields cannot express it.
/// </para>
/// <para>
/// <see cref="AccountEntitlementsTests"/> still proves the same ladder through a real database. These are the
/// unit tests; those are the ones that prove the resolver is actually reached.
/// </para>
/// </remarks>
public sealed class PlanResolverTests
{
    private static readonly EmailAllowlist Comped = new("someone@example.com", null);
    private static readonly EmailAllowlist NobodyComped = EmailAllowlist.Empty;

    [Fact]
    public void An_override_of_Pro_beats_an_empty_comp_list()
    {
        // The case the override exists for: a deployment that comps nobody, and one account that should have
        // the assistant anyway. Before this, the answer required a config key and a container recreate.
        var resolution = PlanResolver.Resolve(
            NobodyComped, AccountPlan.Pro, "nobody@example.com", "auth0|x", emailVerified: true);

        Assert.Equal(AccountPlan.Pro, resolution.Plan);
        Assert.Equal(PlanReason.AdminGranted, resolution.Reason);
    }

    [Fact]
    public void An_override_of_Pro_beats_a_list_the_address_does_not_match()
    {
        var resolution = PlanResolver.Resolve(
            Comped, AccountPlan.Pro, "other@example.com", "auth0|x", emailVerified: true);

        Assert.Equal(AccountPlan.Pro, resolution.Plan);
        Assert.Equal(PlanReason.AdminGranted, resolution.Reason);
    }

    [Fact]
    public void An_override_of_Free_beats_a_comp_match()
    {
        // Why the column is a nullable plan and not a boolean. Pinning an account *below* a list it matches is
        // the only way to answer a domain comp entry that has caught somebody it should not have, and it is an
        // answer available without editing configuration and restarting.
        var resolution = PlanResolver.Resolve(
            Comped, AccountPlan.Free, "someone@example.com", "auth0|x", emailVerified: true);

        Assert.Equal(AccountPlan.Free, resolution.Plan);
        Assert.Equal(PlanReason.AdminGranted, resolution.Reason);
    }

    [Fact]
    public void An_override_is_read_before_the_address_is_even_looked_at()
    {
        // An unverified address, and the sentinel, both of which would otherwise return Free with a different
        // reason. An administrator's decision is about this account specifically and outranks every statement
        // about the shape of its address.
        Assert.Equal(
            PlanReason.AdminGranted,
            PlanResolver.Resolve(Comped, AccountPlan.Pro, "someone@example.com", "auth0|x", emailVerified: false).Reason);

        Assert.Equal(
            PlanReason.AdminGranted,
            PlanResolver.Resolve(Comped, AccountPlan.Pro, "auth0|x", "auth0|x", emailVerified: false).Reason);
    }

    [Fact]
    public void With_no_override_an_empty_comp_list_says_so()
    {
        var resolution = PlanResolver.Resolve(
            NobodyComped, null, "someone@example.com", "auth0|x", emailVerified: true);

        Assert.Equal(AccountPlan.Free, resolution.Plan);
        Assert.Equal(PlanReason.NobodyIsComped, resolution.Reason);
    }

    [Fact]
    public void With_no_override_the_sentinel_address_is_unknown_rather_than_unlisted()
    {
        // An account provisioned with no readable address holds its own subject in Email - an equality no real
        // address can satisfy. It would be Free anyway by failing every match; naming it is what stops the
        // screen telling somebody to ask for an invitation when the deployment cannot read their address.
        var resolution = PlanResolver.Resolve(
            Comped, null, "auth0|abc123", "auth0|abc123", emailVerified: true);

        Assert.Equal(AccountPlan.Free, resolution.Plan);
        Assert.Equal(PlanReason.AddressUnknown, resolution.Reason);
    }

    [Fact]
    public void With_no_override_an_unverified_address_is_reported_ahead_of_the_list_check()
    {
        // Both end in Free and they are opposite instructions: one says ask for an invitation, the other says
        // you already have one and need to click the link in your inbox. Asserted on an address that *would*
        // match, so it is the ordering being tested rather than the outcome.
        var resolution = PlanResolver.Resolve(
            Comped, null, "someone@example.com", "auth0|x", emailVerified: false);

        Assert.Equal(AccountPlan.Free, resolution.Plan);
        Assert.Equal(PlanReason.AddressNotVerified, resolution.Reason);
    }

    [Fact]
    public void With_no_override_a_verified_comp_match_is_Pro()
    {
        var resolution = PlanResolver.Resolve(
            Comped, null, "someone@example.com", "auth0|x", emailVerified: true);

        Assert.Equal(AccountPlan.Pro, resolution.Plan);
        Assert.Equal(PlanReason.Comped, resolution.Reason);
    }

    [Fact]
    public void With_no_override_a_verified_address_off_the_list_is_told_it_is_off_the_list()
    {
        var resolution = PlanResolver.Resolve(
            Comped, null, "other@example.com", "auth0|x", emailVerified: true);

        Assert.Equal(AccountPlan.Free, resolution.Plan);
        Assert.Equal(PlanReason.NotOnCompList, resolution.Reason);
    }

    [Fact]
    public void An_empty_comp_list_outranks_the_sentinel_and_the_verification_check()
    {
        // The deployment-level answer stays first among the non-override rungs, which is where 0.24.1 put it:
        // with no list at all there is nothing for anybody to be on, and telling an account holder to act is
        // sending them somewhere they cannot help.
        Assert.Equal(
            PlanReason.NobodyIsComped,
            PlanResolver.Resolve(NobodyComped, null, "auth0|x", "auth0|x", emailVerified: false).Reason);
    }
}
