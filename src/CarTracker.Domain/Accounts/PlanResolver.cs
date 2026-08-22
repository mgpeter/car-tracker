using CarTracker.Shared;

namespace CarTracker.Domain.Accounts;

/// <summary>Which plan a set of account facts resolves to, and why.</summary>
/// <remarks>
/// <para>
/// <b>Extracted from <see cref="AccountEntitlements"/> when a second caller appeared</b> (DEC-023). That class
/// is scoped and reads <see cref="ICurrentUserAccessor"/>, so it can only ever answer for the account behind
/// the current request; the admin surface answers for all of them at once. A copy of this ladder would have
/// been a second chance to get an ordering wrong that the class it came from already describes as "the whole
/// value of the reason".
/// </para>
/// <para>
/// <b>Pure, and it takes the account's fields rather than a row.</b> Nothing here touches a database, so the
/// ordering is provable without one - which matters because the ordering is the part with consequences, and
/// the two cases that are about the <i>caller</i> rather than the account (no resolved owner, no user row)
/// deliberately stay in <see cref="AccountEntitlements"/>. "There is nobody to ask about" is not a fact about
/// a user and a function taking a user's fields should not be able to express it.
/// </para>
/// </remarks>
public static class PlanResolver
{
    /// <summary>Resolve a plan and its reason.</summary>
    /// <param name="comped">The deployment's comp list, from <c>Plans:CompEmails</c> / <c>Plans:CompDomains</c>.</param>
    /// <param name="planOverride">
    /// The account's stored override, or null when no administrator has decided anything about it. Null is not
    /// the same as <see cref="AccountPlan.Free"/>: an account with no override falls through to the comp list
    /// and can be promoted by a configuration change, while one overridden to Free is pinned below whatever
    /// the list says.
    /// </param>
    /// <param name="email">The stored address, which may be the subject sentinel.</param>
    /// <param name="externalId">The Auth0 subject, needed only to recognise that sentinel.</param>
    /// <param name="emailVerified">Whether the tenant has confirmed the person controls the address.</param>
    /// <remarks>
    /// <b>The order of the refusals is the whole value of the reason.</b> Each one is a different thing for the
    /// reader to do next - fix the deployment, ask its owner, click a link in an inbox, or nothing at all - so
    /// a check that fires before a more specific one would produce a true sentence that sends somebody the
    /// wrong way.
    /// </remarks>
    public static PlanResolution Resolve(
        EmailAllowlist comped,
        AccountPlan? planOverride,
        string? email,
        string? externalId,
        bool emailVerified)
    {
        // First, and ahead of everything including the deployment-level question below. An override is a
        // decision a human took about this account specifically; a comp list is a rule about a class of
        // addresses. Where they disagree the specific decision is the newer information and the deliberate
        // one. Reading the list first would make an override that agrees with it invisible and one that
        // contradicts it inert - two different kinds of confusing - and would make AdminGranted unreachable.
        if (planOverride is { } granted) return new PlanResolution(granted, PlanReason.AdminGranted);

        // Asked about the deployment rather than the account, and still ahead of every account-level check.
        // With no list at all, "you are not on the list" is true and useless: there is nothing for anybody to
        // be on, and no action the account holder can take. This is the case cambelt.app shipped in and could
        // not diagnose from the screen.
        //
        // It is no longer the *first* question, because the override above is about this account and outranks
        // a statement about the deployment. Note the consequence for the caller: entitlement can no longer
        // short-circuit here before touching the database, since only the user row knows about an override.
        if (comped.IsEmpty) return new PlanResolution(AccountPlan.Free, PlanReason.NobodyIsComped);

        // An account provisioned with no readable address holds its own subject in Email - the sentinel
        // AccountProvisioner writes, and an equality no real address can satisfy. It was already Free by
        // failing every match below; naming it separately is what stops the screen telling somebody to ask for
        // an invitation when the deployment cannot read their address at all.
        if (email is null || email == externalId)
            return new PlanResolution(AccountPlan.Free, PlanReason.AddressUnknown);

        // Verification is what makes the comp list mean something, and the domain form is why. A list written
        // as `usualexpat.com` would otherwise hand the paid tier to anyone willing to register as
        // `anything@usualexpat.com` - an allowlist that can be satisfied by typing is not an allowlist, the
        // same sentence SignupPolicy carries about the door it used to guard.
        //
        // Reported ahead of the list check even though both end in Free, because they are opposite
        // instructions: one says ask for an invitation, the other says you already have one and need to click
        // the link in your inbox.
        if (!emailVerified) return new PlanResolution(AccountPlan.Free, PlanReason.AddressNotVerified);

        return comped.Contains(email)
            ? new PlanResolution(AccountPlan.Pro, PlanReason.Comped)
            : new PlanResolution(AccountPlan.Free, PlanReason.NotOnCompList);
    }

    /// <summary>What a named plan allows, with configuration overriding the shipped defaults per field.</summary>
    /// <remarks>
    /// <para>
    /// Here rather than only on <see cref="AccountEntitlements"/> because the diagnostics endpoint renders both
    /// tiers without an account in hand, and reaching them through a scoped service that needs a database to
    /// answer a question about configuration would be the wrong shape. One definition, two callers - the same
    /// reason <see cref="Resolve"/> moved.
    /// </para>
    /// <para>
    /// Written per-field rather than as an object fallback so that setting one key does not silently reset the
    /// other three to zero, which is what a whole-section replacement does to a compose file naming only the
    /// number somebody wanted to change.
    /// </para>
    /// </remarks>
    public static PlanAllowances Allowances(PlanOptions options, AccountPlan plan)
    {
        var (configured, fallback) = plan is AccountPlan.Pro
            ? (options.Pro, Defaults.Pro)
            : (options.Free, Defaults.Free);

        return new PlanAllowances(
            ChatEnabled: configured.ChatEnabled ?? fallback.ChatEnabled,
            DailyChatTokens: configured.DailyChatTokens ?? fallback.DailyChatTokens,
            MaxDocuments: configured.MaxDocuments ?? fallback.MaxDocuments,
            DailyVehicleLookups: configured.DailyVehicleLookups ?? fallback.DailyVehicleLookups);
    }

    /// <summary>
    /// What each plan allows when nothing is configured. <b>The only place these numbers are written.</b>
    /// </summary>
    private static class Defaults
    {
        public static readonly PlanAllowances Free = new(
            ChatEnabled: false,
            DailyChatTokens: 0,
            MaxDocuments: 100,
            DailyVehicleLookups: 3);

        // Null chat tokens: the paid tier sets no ceiling of its own and defers to Chat:DailyTokensPerOwner,
        // which is the key a deployment already uses to bound its model spend. The deployment-wide
        // Chat:DailyTokensGlobal still applies on top, as it does to every plan.
        public static readonly PlanAllowances Pro = new(
            ChatEnabled: true,
            DailyChatTokens: null,
            MaxDocuments: 2_000,
            DailyVehicleLookups: 50);
    }
}
