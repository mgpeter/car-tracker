using CarTracker.Data;
using CarTracker.Shared;
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
    public PlanAllowances For(AccountPlan plan) => PlanResolver.Allowances(options, plan);

    /// <remarks>
    /// <para>
    /// <b>The ladder itself lives in <see cref="PlanResolver"/></b> since DEC-023, because the admin surface
    /// resolves a plan for every account and this class can only ever answer for the request's own. What stays
    /// here is the pair of cases that are about the <i>caller</i> rather than about a user, plus loading the
    /// row the resolver needs.
    /// </para>
    /// <para>
    /// <b>The empty-comp-list short circuit is gone, and that is a real change.</b> This method used to answer
    /// <see cref="PlanReason.NobodyIsComped"/> before touching the database at all. It cannot any more: an
    /// account may carry a plan override and only its row knows. The cost is one extra single-row
    /// primary-key lookup per request on a deployment that comps nobody, already cached for the life of the
    /// request by <c>_resolved</c>.
    /// </para>
    /// </remarks>
    private async Task<PlanResolution> ResolveUncachedAsync(CancellationToken cancellationToken)
    {
        // No resolved owner - anonymous, an API-key principal, a refused sign-in - is Free rather than an
        // error. An unattributable request is nobody's allowance, which is the rule ChatBudget already applies
        // to the ledger, and Free is the direction that costs nothing to be wrong about.
        if (currentUser.OwnerId is not { } ownerId)
            return new PlanResolution(AccountPlan.Free, PlanReason.AddressUnknown);

        var account = await db.Users
            .Where(u => u.Id == ownerId)
            .Select(u => new { u.Email, u.EmailVerified, u.ExternalId, u.PlanOverride })
            .SingleOrDefaultAsync(cancellationToken);

        // A pinned owner whose row has gone is the same unattributable state as no owner at all.
        if (account is null) return new PlanResolution(AccountPlan.Free, PlanReason.AddressUnknown);

        return PlanResolver.Resolve(
            _comped, account.PlanOverride, account.Email, account.ExternalId, account.EmailVerified);
    }
}
