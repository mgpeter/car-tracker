using CarTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Accounts;

/// <summary>Which pre-multi-user vehicles may be adopted, and by whom.</summary>
/// <remarks>
/// <para>
/// The replacement for DEC-016's "the first user to ever sign in claims every unowned vehicle". That rule was
/// written for a single-user deployment being retrofitted with accounts, where the first sign-in was certain to
/// be the owner of the car already in the database. On a deployment anyone can reach, it is a trap: whoever
/// happens to arrive first inherits somebody else's vehicle, its history and its documents, and nothing in the
/// app looks wrong afterwards.
/// </para>
/// <para>
/// So adoption stopped being a race and became a statement. It happens when the provisioning subject matches
/// this external id exactly and never otherwise; unset — the default, and the value on every deployment that
/// has not been through the retrofit — means no adoption, ever.
/// </para>
/// </remarks>
public sealed class OwnershipOptions
{
    /// <summary>
    /// The one Auth0 <c>sub</c> permitted to adopt vehicles with no owner, e.g. <c>auth0|68a…</c>. Null means
    /// nobody, which is what a fresh deployment wants.
    /// </summary>
    public string? ClaimUnownedVehiclesFor { get; set; }
}

/// <summary>How resolving a subject to a local account ended.</summary>
public enum AccountOutcome
{
    /// <summary>An account exists for the subject — found, or just provisioned.</summary>
    Resolved = 1,

    /// <summary>The address is not on the invitation list. <b>No row was created.</b></summary>
    NotInvited = 2,
}

/// <param name="UserId">The local account id, when one was resolved.</param>
/// <param name="Detail">Why the subject was refused, in words a signed-out person can act on.</param>
public sealed record AccountResolution(AccountOutcome Outcome, int? UserId, string? Detail = null)
{
    public static AccountResolution Resolved(int userId) => new(AccountOutcome.Resolved, userId);

    public static AccountResolution NotInvited(string detail) => new(AccountOutcome.NotInvited, null, detail);
}

/// <summary>
/// Turns an authenticated Auth0 subject into a local <see cref="User"/> — the only place in the codebase where
/// an account comes into existence, which is why the invitation door and vehicle adoption both live here and
/// nowhere else.
/// </summary>
/// <remarks>
/// <para>
/// Lifted out of <c>CurrentUserMiddleware</c> rather than left inline, because this is the code that decides
/// whether a stranger gets an account and there is no <c>CarTracker.WebApi.Tests</c> project to test a
/// middleware in. Here the "a refused address creates no row" half is a plain Data test against a real
/// database, which is the assertion actually worth making — the 403 is only how the refusal is reported.
/// </para>
/// <para>
/// <b>Provisioning is two saves, not one, and cannot be otherwise.</b> <see cref="User.Id"/> is
/// store-generated and the expense categories' owner FK is navigation-less, so nothing fills the key in
/// before the insert; <see cref="ExpenseCategoryProvisioner"/> refuses an unsaved user rather than letting
/// thirteen rows key themselves to owner 0.
/// </para>
/// </remarks>
public sealed class AccountProvisioner(
    CarTrackerDbContext db,
    TimeProvider clock,
    SignupPolicy signup,
    IIdentityProviderClient identity,
    SignupRefusalCache refusals,
    OwnershipOptions ownership,
    Legal.LegalOptions legal)
{
    /// <summary>
    /// How stale <see cref="Data.User.LastSeenAt"/> may be before the next authenticated request rewrites it.
    /// One write per account per quarter hour rather than one per request.
    /// </summary>
    private static readonly TimeSpan LastSeenResolution = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Finds or provisions the account for <paramref name="externalId"/>.
    /// </summary>
    /// <param name="emailClaim">The token's <c>email</c> claim, when the tenant adds one. Usually null.</param>
    /// <param name="emailClaimVerified">
    /// The token's <c>email_verified</c> claim, read only when <paramref name="emailClaim"/> carried the
    /// address. A tenant that adds an <c>email</c> claim through an Action and not the verification beside it
    /// admits nobody — the address and the proof travel together or the address is only a claim.
    /// </param>
    /// <param name="nameClaim">The token's <c>name</c> claim, likewise.</param>
    public async Task<AccountResolution> ResolveAsync(
        string externalId,
        string? emailClaim,
        bool emailClaimVerified,
        string? nameClaim,
        CancellationToken cancellationToken = default)
    {
        var existing = await db.Users.SingleOrDefaultAsync(u => u.ExternalId == externalId, cancellationToken);
        if (existing is not null)
        {
            // Deliberately no allowlist check: an existing account is admitted by having been admitted. Shutting
            // the door later stops newcomers without evicting the people already inside - and neither does
            // tightening it, which is what made requiring a verified address safe to add to a running
            // deployment. What an existing account may *spend* is a separate question, re-asked on every
            // request by IAccountEntitlements; this one is asked once, ever.
            await BackfillEmailAsync(existing, emailClaim, emailClaimVerified, cancellationToken);
            await TouchLastSeenAsync(existing, cancellationToken);
            return AccountResolution.Resolved(existing.Id);
        }

        // Asked after the account lookup and before the identity provider, which is the only ordering that
        // works: a row wins over a remembered refusal, and the refusal exists precisely to save the lookup.
        if (refusals.Refusal(externalId) is { } remembered) return AccountResolution.NotInvited(remembered);

        // An unseen subject. Everything below this line happens once per account, ever — except on the refusal
        // path, which is why the cache above is there.
        var profile = emailClaim is null
            ? await identity.GetProfileAsync(externalId, cancellationToken)
            : null;

        var email = emailClaim ?? profile?.Email;
        var verified = emailClaim is not null ? emailClaimVerified : profile?.EmailVerified is true;
        var displayName = nameClaim ?? profile?.DisplayName;

        // Open sign-up admits everybody, including somebody whose address could not be read - see
        // SignupPolicy.Admits. The refusal below is InviteOnly's alone.
        if (!signup.Admits(email, verified))
        {
            // Refused *before* the row exists, not created-and-flagged: a rejected person leaves nothing to
            // clean up and no half-state for the ownership filter to reason about. (Auth0 still holds the
            // identity — see DEC-018. Turning off public sign-up in the dashboard is the belt to this braces.)
            //
            // Three refusals, three sentences, because they are three different things to do next: nobody can
            // read your address, nobody has proved it is yours, or it is yours and not invited. One generic
            // "not invited" would send someone who needs to click a link in their inbox to ask the deployment's
            // owner for an invitation they already have.
            var detail = string.IsNullOrWhiteSpace(email)
                ? "We could not read the email address behind this sign-in, so it cannot be checked against "
                  + "the invitation list. Ask whoever runs this deployment to invite you."
                : !verified
                    ? $"The address behind this sign-in ({email}) has not been verified, so it cannot be "
                      + "checked against the invitation list. Follow the confirmation link the sign-in provider "
                      + "emailed you, then sign in again."
                    : $"This deployment is invitation-only, and {email} is not on the list. Ask whoever runs it "
                      + "to add you.";

            refusals.Remember(externalId, detail);
            return AccountResolution.NotInvited(detail);
        }

        var user = new User
        {
            ExternalId = externalId,
            // The subject stands in when no address could be read, which under open sign-up is a state a
            // perfectly ordinary deployment reaches: no Auth0:Management: credential, and the access token
            // carries no email claim. `Email == ExternalId` is an equality no real address can satisfy, so
            // BackfillEmailAsync recognises the row later with certainty and repairs it. Under InviteOnly the
            // fallback is unreachable, because Admits refuses a null address outright.
            Email = string.IsNullOrWhiteSpace(email) ? externalId : email,
            // False whenever the address is unknown or unproven, and that is the fail-safe direction: the
            // account exists, and is on the free tier until the tenant says the address is theirs.
            EmailVerified = verified && !string.IsNullOrWhiteSpace(email),
            DisplayName = displayName,
            CreatedAt = clock.GetUtcNow(),
            // Stamped at creation as well as on the returning path, because an account created by a sign-in
            // was seen at that moment. Leaving it null until the second visit would report somebody who signed
            // up and never came back as never having been here at all, which is the opposite of the fact the
            // column exists to record - and it is the single most interesting row on the admin list.
            LastSeenAt = clock.GetUtcNow(),
            // Which text this account was shown when it signed up (DEC-024). Null on a deployment publishing
            // nothing, which is a true statement rather than a gap.
            //
            // ON THE CREATION PATH ONLY, and deliberately not inside BackfillEmailAsync: that method returns
            // early once the address is present and verified - the common case for every established account -
            // so anything folded into it is not written for exactly the accounts worth looking at. That is the
            // trap TouchLastSeenAsync was extracted to avoid, one column earlier.
            TermsVersion = legal.IsPublished ? Legal.LegalVersion.Current : null,
        };
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race to create the same subject — the other request's row wins; use it. The whole tracker
            // goes, not just this entity: provisioning is more than one row now, and a rule that names one
            // entity strands whatever is staged beside it the day something is.
            db.ChangeTracker.Clear();
            return AccountResolution.Resolved(
                (await db.Users.SingleAsync(u => u.ExternalId == externalId, cancellationToken)).Id);
        }

        // Cancels any identity removal still queued for this subject, and the judgement behind that is worth
        // stating. The obligation the queued row carries is to erase the *data*, and that was discharged when
        // the earlier account was deleted; what is still outstanding is the login. Signing in again is an
        // affirmative act about that login — the person is asking to keep using it — and honouring the queue
        // would let the retry pass delete the identity behind the account being created right here, stranding
        // everything put into it. So coming back cancels the removal. RetryPendingAsync refuses such a row too:
        // either half alone leaves a race, since whichever runs second undoes the first.
        await db.PendingIdentityDeletions
            .Where(p => p.ExternalId == externalId)
            .ExecuteDeleteAsync(cancellationToken);

        // An account is its user row *and* its reference lists — the 13 expense categories stopped being seed
        // data when they gained an owner. A failure here is left to surface: an account with no categories
        // refuses every expense write, and finding that out now is better than finding it out from the first
        // fill someone logs.
        db.ExpenseCategories.AddRange(ExpenseCategoryProvisioner.ForNewUser(user));
        await db.SaveChangesAsync(cancellationToken);

        await AdoptUnownedVehiclesAsync(user, cancellationToken);

        return AccountResolution.Resolved(user.Id);
    }

    /// <summary>
    /// Fills in a real address, and its verification, on an account that was provisioned without either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A missing address is recognisable with certainty because the fallback stores the subject itself, so
    /// <c>Email == ExternalId</c> - an equality no real address can satisfy. Once repaired that half is false
    /// forever and costs one comparison.
    /// </para>
    /// <para>
    /// <b>The condition widened to include an unverified row, and without that half the column would be a
    /// one-way door.</b> Somebody signs up, gets the free tier, then follows the confirmation link in their
    /// inbox - and nothing would ever revisit the flag, so a comped address could sit on the free tier
    /// permanently with the tenant saying it was verified all along. Signing in again is what repairs it. The
    /// cost is one Management call per request for an account that never verifies, which is why the claim is
    /// consulted first and the client is asked only when it is configured.
    /// </para>
    /// </remarks>
    private async Task BackfillEmailAsync(
        User user,
        string? emailClaim,
        bool emailClaimVerified,
        CancellationToken cancellationToken)
    {
        var addressMissing = user.Email == user.ExternalId;

        if (!addressMissing && user.EmailVerified) return;

        var profile = emailClaim is null && identity.IsConfigured
            ? await identity.GetProfileAsync(user.ExternalId, cancellationToken)
            : null;

        var email = emailClaim ?? profile?.Email;
        var verified = emailClaim is not null ? emailClaimVerified : profile?.EmailVerified is true;

        var changed = false;

        if (addressMissing && !string.IsNullOrWhiteSpace(email) && email != user.ExternalId)
        {
            user.Email = email;
            addressMissing = false;
            changed = true;
        }

        // Only ever set true here. A tenant that has stopped answering must not be able to demote a verified
        // account to the free tier, which is what `user.EmailVerified = verified` would do on every timeout.
        if (!user.EmailVerified && verified && !addressMissing)
        {
            user.EmailVerified = true;
            changed = true;
        }

        if (changed) await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Records that this account was seen, at most once every <see cref="LastSeenResolution"/>.</summary>
    /// <remarks>
    /// <para>
    /// <b>A separate call rather than a few lines inside <see cref="BackfillEmailAsync"/>, and that is not
    /// tidiness.</b> That method returns early the moment the address is present and verified, which is the
    /// common case for every established account - so a stamp placed inside it would fire only for accounts
    /// still being backfilled, and the column would be null for exactly the accounts anyone wants to look at.
    /// Two jobs, two early returns, neither able to swallow the other.
    /// </para>
    /// <para>
    /// <b>Coalesced against the stored value, not a cache.</b> Fifteen minutes is short enough that "seen
    /// today" and "seen within the hour" are both accurate and long enough that a session moving between
    /// screens writes once rather than forty times. Comparing against the column means the coalescing survives
    /// a container recreate, which is the reasoning <c>chat_usage</c> already records about being a table
    /// rather than an in-memory counter.
    /// </para>
    /// <para>
    /// This is an observation, not a derived value: nothing recomputes when somebody signs in, and there is no
    /// underlying record it could disagree with, because the sign-in is the record. See
    /// <see cref="Data.User.LastSeenAt"/> for why it is not derived from the rows an account happens to write.
    /// </para>
    /// </remarks>
    private async Task TouchLastSeenAsync(User user, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        if (user.LastSeenAt is { } seen && now - seen < LastSeenResolution) return;

        user.LastSeenAt = now;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <remarks>
    /// <c>IgnoreQueryFilters</c> because the owner is not set yet and the filter would otherwise hide the very
    /// rows being claimed. Ordinal comparison: an Auth0 subject is an opaque identifier, not a name, and two
    /// that differ only in case are two different people.
    /// </remarks>
    private async Task AdoptUnownedVehiclesAsync(User user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ownership.ClaimUnownedVehiclesFor)) return;
        if (!string.Equals(ownership.ClaimUnownedVehiclesFor, user.ExternalId, StringComparison.Ordinal)) return;

        await db.Vehicles.IgnoreQueryFilters()
            .Where(v => v.OwnerId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.OwnerId, user.Id), cancellationToken);
    }
}
