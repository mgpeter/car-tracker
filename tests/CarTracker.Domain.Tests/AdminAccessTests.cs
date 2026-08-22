using CarTracker.Domain.Admin;

namespace CarTracker.Domain.Tests;

/// <summary>
/// What an Auth0 <c>permissions</c> claim has to say before the admin surface opens.
/// </summary>
/// <remarks>
/// <para>
/// <b>This class is the only thing in the repository that can be sure about any of it.</b> DEC-022 refused an
/// Auth0 permission on the grounds that tenant state is something nothing here can assert, test or restore,
/// and DEC-023 concedes that half rather than answering it. What is testable is the *meaning* of a permission
/// once it arrives, so that half is pinned here: who holds one is Auth0's business, what holding one entitles
/// you to is this file's.
/// </para>
/// <para>
/// The predicate is a domain type at all because there is no <c>CarTracker.WebApi.Tests</c> project, so a
/// policy expressed inline in <c>Program.cs</c> would be a security decision no test could reach. Same reason
/// <see cref="Accounts.SignupPolicy"/> and <see cref="Accounts.EmailAllowlist"/> live where they do.
/// </para>
/// </remarks>
public sealed class AdminAccessTests
{
    [Fact]
    public void A_principal_holding_the_permission_is_granted()
    {
        Assert.True(AdminAccess.Grants([AdminAccess.ReadPermission], AdminAccess.ReadPermission));
    }

    [Fact]
    public void The_permission_is_found_among_several()
    {
        // The realistic shape: an operator holding both, and later more. A claim collection is not ordered and
        // nothing may assume the wanted one arrives first.
        string[] held = ["something:else", AdminAccess.PlanWritePermission, AdminAccess.ReadPermission];

        Assert.True(AdminAccess.Grants(held, AdminAccess.ReadPermission));
        Assert.True(AdminAccess.Grants(held, AdminAccess.PlanWritePermission));
    }

    [Fact]
    public void A_principal_holding_a_different_permission_is_refused()
    {
        Assert.False(AdminAccess.Grants(["mcp:read"], AdminAccess.ReadPermission));
    }

    [Fact]
    public void The_write_permission_does_not_confer_the_read_permission()
    {
        // No implication between the two, in either direction. The endpoints require both for a write, which
        // only means something if neither implies the other - and the day admin:account:delete exists, the
        // same property is what stops it arriving already granted.
        Assert.False(AdminAccess.Grants([AdminAccess.PlanWritePermission], AdminAccess.ReadPermission));
        Assert.False(AdminAccess.Grants([AdminAccess.ReadPermission], AdminAccess.PlanWritePermission));
    }

    [Fact]
    public void No_permissions_at_all_is_refused()
    {
        // The state every ordinary account is in, and the state a deployment is in when RBAC has not been
        // enabled on the API - the token simply carries no such claim. Both must refuse, and refusing is the
        // safe direction for a misconfiguration nothing here can observe.
        Assert.False(AdminAccess.Grants([], AdminAccess.ReadPermission));
        Assert.False(AdminAccess.Grants(null, AdminAccess.ReadPermission));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("admin:")]
    [InlineData("admin:read:extra")]
    [InlineData("read")]
    [InlineData(":read")]
    public void Matching_is_exact_and_not_a_prefix_or_a_segment(string held)
    {
        // No wildcard, no hierarchy, no "admin covers everything below it". A scheme where a shorter string
        // implies a longer one is how a permission somebody assigned as a convenience turns out to have
        // granted the surface it was carefully kept away from.
        Assert.False(AdminAccess.Grants([held], AdminAccess.ReadPermission));
    }

    [Theory]
    [InlineData("Admin:Read")]
    [InlineData("ADMIN:READ")]
    [InlineData(" admin:read")]
    [InlineData("admin:read ")]
    public void Matching_is_ordinal_and_untrimmed(string held)
    {
        // Deliberate. The permission strings are defined by hand in the Auth0 dashboard and matched against
        // constants here; accepting near-misses would mean a typo in the tenant silently works, and the next
        // person to read the dashboard would see a permission name that is not the one in this file.
        Assert.False(AdminAccess.Grants([held], AdminAccess.ReadPermission));
    }

    [Fact]
    public void A_collection_carrying_blanks_matches_nothing_by_accident()
    {
        // Not a shape Auth0 produces, but the failure it would cause is the one worth foreclosing: a required
        // permission of "" matching an entry of "" would open the surface to anybody whose token carried an
        // empty claim value.
        Assert.False(AdminAccess.Grants(["", "  "], AdminAccess.ReadPermission));
        Assert.False(AdminAccess.Grants([""], ""));
    }

    [Fact]
    public void The_permission_names_are_the_ones_documented_in_the_readme_and_the_dec()
    {
        // Pinned as text because these strings are typed by a human into the Auth0 dashboard, and a rename
        // here that nobody carries over there locks the operator out of their own deployment with no error
        // that says so.
        Assert.Equal("admin:read", AdminAccess.ReadPermission);
        Assert.Equal("admin:plan:write", AdminAccess.PlanWritePermission);
        Assert.Equal("permissions", AdminAccess.PermissionClaim);
    }
}
