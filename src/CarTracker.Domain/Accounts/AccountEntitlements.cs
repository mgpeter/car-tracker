using CarTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Accounts;

/// <summary>What the account behind this request may spend.</summary>
/// <remarks>
/// <para>
/// <b>One predicate, read by three surfaces</b> - the chat, the documents volume and the DVLA lookup. The
/// alternative, each surface deciding for itself who is entitled, is how one of them comes to think somebody is
/// paying while another thinks they are not; and the day checkout exists, three places to change is three
/// places to get a refund wrong.
/// </para>
/// <para>
/// An interface because the surfaces are tested without a database - the chat loop tests script the model and
/// must not need PostgreSQL to assert that an unentitled turn makes no request.
/// </para>
/// </remarks>
public interface IAccountEntitlements
{
    /// <summary>Which plan the current account is on, and why.</summary>
    /// <remarks>
    /// The reason travels with the plan rather than being a second call, because every caller that renders one
    /// renders the other, and resolving twice is how they come to disagree.
    /// </remarks>
    Task<PlanResolution> ResolveAsync(CancellationToken cancellationToken = default);

    /// <summary>What that plan allows. The same answer as <see cref="ResolveAsync"/>, in the form callers use.</summary>
    Task<PlanAllowances> AllowancesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the current account's plan from the comp list and its verified address.
/// </summary>
/// <remarks>
/// <para>
/// <b>Derived on read, stored nowhere</b> - the central constraint applied to entitlement. The obvious
/// alternative is a column a Stripe webhook flips, or a <c>permissions</c> claim in the access token, and both
/// are the same mistake in different clothes: a copy of a fact owned somewhere else, free to go stale in both
/// directions. A cancelled subscriber keeping access and a new subscriber unable to use what they just paid for
/// are the same bug, and it is the one surface where being wrong costs money (DEC-022).
/// </para>
/// <para>
/// <b>Scoped, and it reads <see cref="ICurrentUserAccessor"/></b> - the same accessor the vehicle query filter
/// and <c>ChatBudget</c> read. An account therefore cannot be billed one plan while reading another's data,
/// because there is one answer to "who is this" and everything asks it.
/// </para>
/// <para>
/// The lookup is cached for the life of the request. Three surfaces asking within one request is possible
/// (a chat turn calling a write tool that uploads nothing is not, but a future one might), and the answer
/// cannot change mid-request: the comp list is bound at boot and the user row is not edited by the request
/// that is reading it.
/// </para>
/// </remarks>
public sealed class AccountEntitlements(
    CarTrackerDbContext db,
    PlanOptions options,
    ICurrentUserAccessor currentUser) : IAccountEntitlements
{
    private readonly EmailAllowlist _comped = new(options.CompEmails, options.CompDomains);

    private PlanResolution? _resolved;

    public async Task<PlanResolution> ResolveAsync(CancellationToken cancellationToken = default) =>
        _resolved ??= await ResolveUncachedAsync(cancellationToken);

    public async Task<PlanAllowances> AllowancesAsync(CancellationToken cancellationToken = default) =>
        For((await ResolveAsync(cancellationToken)).Plan);

    /// <summary>The allowances of a named plan, with no account involved.</summary>
    /// <remarks>
    /// Public so a caller that already knows the plan - the meta endpoint rendering both tiers, a test - does
    /// not have to go back through a database to turn one into the other.
    /// </remarks>
    public PlanAllowances For(AccountPlan plan) =>
        plan is AccountPlan.Pro ? Limits(options.Pro, Defaults.Pro) : Limits(options.Free, Defaults.Free);

    /// <remarks>
    /// <b>The order of the refusals is the whole value of the reason.</b> Each one is a different thing for the
    /// reader to do next - fix the deployment, ask its owner, click a link in an inbox - so a check that fires
    /// before a more specific one would produce a true sentence that sends somebody the wrong way.
    /// </remarks>
    private async Task<PlanResolution> ResolveUncachedAsync(CancellationToken cancellationToken)
    {
        // Asked first, and about the deployment rather than the account. With no list at all, "you are not on
        // the list" is true and useless: there is nothing for anybody to be on, and no action the account
        // holder can take. This is the case cambelt.app shipped in and could not diagnose from the screen.
        if (_comped.IsEmpty) return new PlanResolution(AccountPlan.Free, PlanReason.NobodyIsComped);

        // No resolved owner - anonymous, an API-key principal, a refused sign-in - is Free rather than an
        // error. An unattributable request is nobody's allowance, which is the rule ChatBudget already applies
        // to the ledger, and Free is the direction that costs nothing to be wrong about.
        if (currentUser.OwnerId is not { } ownerId)
            return new PlanResolution(AccountPlan.Free, PlanReason.AddressUnknown);

        var account = await db.Users
            .Where(u => u.Id == ownerId)
            .Select(u => new { u.Email, u.EmailVerified, u.ExternalId })
            .SingleOrDefaultAsync(cancellationToken);

        if (account is null) return new PlanResolution(AccountPlan.Free, PlanReason.AddressUnknown);

        // An account provisioned with no readable address holds its own subject in Email - the sentinel
        // AccountProvisioner writes, and an equality no real address can satisfy. It was already Free by
        // failing every match below; naming it separately is what stops the screen telling somebody to ask for
        // an invitation when the deployment cannot read their address at all.
        if (account.Email == account.ExternalId)
            return new PlanResolution(AccountPlan.Free, PlanReason.AddressUnknown);

        // Verification is what makes the comp list mean something, and the domain form is why. A list written
        // as `usualexpat.com` would otherwise hand the paid tier to anyone willing to register as
        // `anything@usualexpat.com` - an allowlist that can be satisfied by typing is not an allowlist, the
        // same sentence SignupPolicy carries about the door it used to guard.
        //
        // Reported ahead of the list check even though both end in Free, because they are opposite
        // instructions: one says ask for an invitation, the other says you already have one and need to click
        // the link in your inbox.
        if (!account.EmailVerified)
            return new PlanResolution(AccountPlan.Free, PlanReason.AddressNotVerified);

        return _comped.Contains(account.Email)
            ? new PlanResolution(AccountPlan.Pro, PlanReason.Comped)
            : new PlanResolution(AccountPlan.Free, PlanReason.NotOnCompList);
    }

    /// <remarks>
    /// Configuration wins where it names a value; the shipped defaults fill the rest. Written per-field rather
    /// than as an object fallback so that setting one key does not silently reset the other three to zero -
    /// which is what a whole-section replacement does to a compose file that names only the number somebody
    /// wanted to change.
    /// </remarks>
    private static PlanAllowances Limits(PlanOptions.PlanLimits configured, PlanAllowances fallback) =>
        new(
            ChatEnabled: configured.ChatEnabled ?? fallback.ChatEnabled,
            DailyChatTokens: configured.DailyChatTokens ?? fallback.DailyChatTokens,
            MaxDocuments: configured.MaxDocuments ?? fallback.MaxDocuments,
            DailyVehicleLookups: configured.DailyVehicleLookups ?? fallback.DailyVehicleLookups);

    /// <summary>
    /// What each plan allows when nothing is configured.
    /// </summary>
    /// <remarks>
    /// <b>The only place these numbers are written.</b> <see cref="PlanOptions"/> carries overrides and starts
    /// entirely null, so an operator who sets one key changes one number and inherits the rest.
    /// </remarks>
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
