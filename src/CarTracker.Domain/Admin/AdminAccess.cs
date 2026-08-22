namespace CarTracker.Domain.Admin;

/// <summary>What an access token has to carry before the operator surface opens.</summary>
/// <remarks>
/// <para>
/// <b>A domain type rather than a <c>RequireClaim</c> in <c>Program.cs</c>, and that is the whole point of the
/// file.</b> There is no <c>CarTracker.WebApi.Tests</c> project, so a policy expressed inline in the pipeline
/// is a security decision no test can reach. Written here, what a permission means is proved by
/// <c>AdminAccessTests</c>; the policies in <c>Program.cs</c> become two lines that call it. Same argument
/// <see cref="Accounts.SignupPolicy"/> and <see cref="Accounts.AccountProvisioner"/> already make about
/// themselves.
/// </para>
/// <para>
/// <b>DEC-022 refused a <c>permissions</c> claim and DEC-023 distinguishes this from what it refused.</b> That
/// decision was about *entitlement*: a plan carried in a token is a copy of a fact this application owns, free
/// to go stale in both directions, on the one surface where being wrong costs money. Who administers a
/// deployment is a fact the identity tenant owns, so the claim is the original rather than a copy, and a
/// revoked administrator keeping read access until their token rotates costs nothing and is fixed by rotating
/// it. The other half of DEC-022's objection - that nothing in this repository can assert, test or restore an
/// Auth0 role - is conceded rather than answered. This file is the mitigation, and it is a partial one: it
/// proves what a permission means, never who holds it.
/// </para>
/// <para>
/// <b>Two permissions and deliberately no generic <c>admin:write</c>.</b> Account deletion and token
/// revocation are both wanted eventually. A permission meaning "any admin mutation" would confer them on
/// whoever already holds it, so the destructive capability would arrive pre-granted and nobody would
/// re-decide at the moment the decision mattered. One permission per capability makes the next dangerous
/// thing an explicit assignment.
/// </para>
/// </remarks>
public static class AdminAccess
{
    /// <summary>
    /// The claim Auth0 adds to an access token when <b>RBAC</b> and <b>Add Permissions in the Access Token</b>
    /// are both enabled on the API. Enabling only the first adds nothing, which is the misconfiguration most
    /// likely to look like a broken feature.
    /// </summary>
    /// <remarks>
    /// Read with <c>FindAll</c>, never <c>FindFirst</c>: a JSON array claim arrives as repeated claims of the
    /// same name rather than one joined value, so <c>FindFirst</c> works perfectly while the operator holds a
    /// single permission and starts refusing the moment a second is assigned. That is a bug that surfaces on
    /// the day you expand, which is the day it is least expected.
    /// </remarks>
    public const string PermissionClaim = "permissions";

    /// <summary>Read the operator surface: the account list, deployment usage and configuration posture.</summary>
    public const string ReadPermission = "admin:read";

    /// <summary>Set or clear an account's plan override. Required <i>in addition to</i> <see cref="ReadPermission"/>.</summary>
    public const string PlanWritePermission = "admin:plan:write";

    /// <summary>Whether <paramref name="held"/> contains <paramref name="required"/>.</summary>
    /// <param name="held">The values of every <see cref="PermissionClaim"/> claim on the principal. May be null.</param>
    /// <param name="required">One of the constants above.</param>
    /// <remarks>
    /// <b>Ordinal, exact, untrimmed, and with no hierarchy.</b> No wildcard, no prefix rule and no sense in
    /// which <c>admin</c> covers <c>admin:read</c>: a scheme where a shorter string implies a longer one is
    /// how a permission assigned as a convenience turns out to have granted the surface it was kept away
    /// from. Case matters for the same reason a typo in the dashboard should fail loudly rather than work.
    /// <para>
    /// A blank <paramref name="required"/> matches nothing, so a constant that somehow arrives empty closes
    /// the door rather than opening it to every principal carrying an empty claim value.
    /// </para>
    /// </remarks>
    public static bool Grants(IEnumerable<string>? held, string required)
    {
        if (string.IsNullOrEmpty(required) || held is null) return false;

        foreach (var permission in held)
        {
            if (string.Equals(permission, required, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
