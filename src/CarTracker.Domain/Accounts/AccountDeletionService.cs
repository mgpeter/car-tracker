using CarTracker.Data;
using CarTracker.Domain.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CarTracker.Domain.Accounts;

/// <summary>
/// What the deletion screen states before it will arm — the weight of what is about to go.
/// </summary>
/// <param name="LogEntryCount">
/// One number across the ten per-vehicle log tables, because the screen is establishing weight rather than
/// producing an inventory. The inventory is the export, which is offered beside it.
/// </param>
public sealed record AccountSummary(
    string Email,
    DateTimeOffset CreatedAt,
    int VehicleCount,
    int LogEntryCount,
    int DocumentCount,
    long DocumentBytes,
    int AssistantTokenCount);

/// <summary>How a deletion request ended. Each maps to a distinct status because each has a distinct fix.</summary>
public enum AccountDeletionOutcome
{
    /// <summary>The data is gone. The identity is gone too, or is queued until it is.</summary>
    Deleted = 1,

    /// <summary>No account behind the request — an API-key principal, or an identity with no local row.</summary>
    NoAccount = 2,

    /// <summary>Authenticated, but not as the person whose account this is. Nothing was touched.</summary>
    NotAccountHolder = 3,

    /// <summary>The typed confirmation did not match the account's address. Nothing was touched.</summary>
    ConfirmationMismatch = 4,

    /// <summary>No identity-provider credential, so the login could not be erased. <b>Nothing was touched.</b></summary>
    IdentityDeletionNotConfigured = 5,
}

/// <param name="Field">The request field to mark, when the refusal is about one. Null otherwise.</param>
/// <param name="IdentityDeleted">
/// False when the local data is gone but the login is not yet — the operation still succeeded, and the pending
/// row is the promise that the provider will keep being asked.
/// </param>
public sealed record AccountDeletionResult(
    AccountDeletionOutcome Outcome,
    string? Detail = null,
    string? Field = null,
    bool IdentityDeleted = false);

/// <summary>
/// Erases an account: its vehicles and everything under them, its reference lists, its assistant tokens, its
/// document bytes, its user row, and the login behind it (UK GDPR Art. 17).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every refusal is decided here, not at the endpoint.</b> The confirmation match, the not-configured refusal
/// and the check that the caller is the account holder are all preconditions of the most destructive operation
/// in the app, and an endpoint is the one place in this codebase with no test project behind it. The endpoint
/// keeps only the mapping from an outcome to a status code.
/// </para>
/// <para>
/// <b>The order is forced by the schema, not chosen.</b> <c>vehicles.owner_id</c> and
/// <c>assistant_tokens.owner_id</c> are both <c>Restrict</c>, so the user row cannot go first — the database
/// refuses. The owned vehicle ids are collected before anything is deleted, because step 4 needs them and they
/// are unobtainable afterwards.
/// </para>
/// <para>
/// <b>The erasure claim has one acknowledged gap.</b> The rows and the login are guaranteed; the document bytes
/// are best effort. Removing a folder can fail for reasons that have nothing to do with this app — a file held
/// open by a NAS indexer, a volume mounted read-only — and by the time it is attempted the transaction has
/// committed, so failing the request would report a completed erasure as a 500 and invite a retry that answers
/// 401. Such a folder is therefore logged as an error and left: residual files with nothing in the database
/// referencing them, recoverable only by hand. Small, and better stated here than discovered.
/// </para>
/// </remarks>
public sealed class AccountDeletionService(
    CarTrackerDbContext db,
    DocumentStore documents,
    IIdentityProviderClient identity,
    TimeProvider clock,
    ILogger<AccountDeletionService> logger)
{
    /// <summary>The counts the confirmation states, or null when there is no account behind the request.</summary>
    public async Task<AccountSummary?> GetSummaryAsync(int? ownerId, CancellationToken cancellationToken = default)
    {
        if (ownerId is not int id) return null;

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null) return null;

        var vehicleIds = await OwnedVehicleIdsAsync(id, cancellationToken);

        // The ten tables an owner would call "things I logged". Check definitions and budget groups are
        // configuration rather than entries, and anomalies are flags the app raised rather than rows anyone
        // typed — counting those would inflate the figure the consent rests on.
        var logEntryCount =
            await db.MileageReadings.CountAsync(x => vehicleIds.Contains(x.VehicleId), cancellationToken)
            + await db.FuelEntries.CountAsync(x => vehicleIds.Contains(x.VehicleId), cancellationToken)
            + await db.ExpenseEntries.CountAsync(x => vehicleIds.Contains(x.VehicleId), cancellationToken)
            + await db.ServiceRecords.CountAsync(x => vehicleIds.Contains(x.VehicleId), cancellationToken)
            + await db.TyreReadings.CountAsync(x => vehicleIds.Contains(x.VehicleId), cancellationToken)
            + await db.WashEntries.CountAsync(x => vehicleIds.Contains(x.VehicleId), cancellationToken)
            + await db.MaintenanceTasks.CountAsync(x => vehicleIds.Contains(x.VehicleId), cancellationToken)
            + await db.Issues.CountAsync(x => vehicleIds.Contains(x.VehicleId), cancellationToken)
            + await db.EquipmentItems.CountAsync(x => vehicleIds.Contains(x.VehicleId), cancellationToken)
            // Through the definitions, because check_logs carries no vehicle column.
            + await db.CheckLogs.CountAsync(
                l => db.CheckDefinitions.Any(d => d.Id == l.CheckDefinitionId && vehicleIds.Contains(d.VehicleId)),
                cancellationToken);

        var documentRows = await db.Documents
            .Where(d => vehicleIds.Contains(d.VehicleId))
            .Select(d => d.SizeBytes)
            .ToListAsync(cancellationToken);

        return new AccountSummary(
            user.Email,
            user.CreatedAt,
            VehicleCount: vehicleIds.Count,
            LogEntryCount: logEntryCount,
            DocumentCount: documentRows.Count,
            DocumentBytes: documentRows.Sum(),
            AssistantTokenCount: await db.AssistantTokens.CountAsync(t => t.OwnerId == id, cancellationToken));
    }

    /// <summary>
    /// Destroys the account named by <paramref name="ownerId"/>, once every precondition holds.
    /// </summary>
    /// <param name="subjectClaim">
    /// The <c>sub</c> on the request's principal. It must match the account's external id: an account is deleted
    /// by the person signed in as it, never by anything else holding a resolved owner.
    /// </param>
    /// <param name="confirmEmail">The address typed into the confirmation. Compared ordinal, case-insensitively.</param>
    public async Task<AccountDeletionResult> DeleteAsync(
        int? ownerId,
        string? subjectClaim,
        string? confirmEmail,
        CancellationToken cancellationToken = default)
    {
        if (ownerId is not int id)
            return new AccountDeletionResult(AccountDeletionOutcome.NoAccount, "No account is signed in.");

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
            return new AccountDeletionResult(AccountDeletionOutcome.NoAccount, "No account is signed in.");

        // An assistant token resolves an owner but carries no subject, so it fails here. In practice it never
        // reaches this far — /api/account sits behind the Auth0 fallback policy and a token 401s at the door —
        // but the guard is what makes that a property of the operation rather than of the route's registration.
        if (!string.Equals(subjectClaim, user.ExternalId, StringComparison.Ordinal))
        {
            return new AccountDeletionResult(AccountDeletionOutcome.NotAccountHolder,
                "An account can only be deleted by the person signed in as it.");
        }

        if (!string.Equals(confirmEmail?.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
        {
            return new AccountDeletionResult(AccountDeletionOutcome.ConfirmationMismatch,
                $"Type {user.Email} exactly to confirm. This is irreversible.",
                Field: "confirmEmail");
        }

        // Checked before a single row goes, and this ordering is the whole point: a deployment with no
        // management credential would otherwise delete every local trace and leave the login standing — the
        // worst of both outcomes, and silent. The Lookup: precedent answers 503 for the same reason.
        if (!identity.IsConfigured)
        {
            return new AccountDeletionResult(AccountDeletionOutcome.IdentityDeletionNotConfigured,
                "This deployment cannot remove the login behind an account (Auth0:Management: is not "
                + "configured), so it refuses to delete the data and leave the sign-in working.");
        }

        // Before anything is deleted: after the vehicles go there is nothing left that names their folders.
        var vehicleIds = await OwnedVehicleIdsAsync(id, cancellationToken);

        // Mandatory, not decorative: Aspire's EnrichNpgsqlDbContext installs a retrying execution strategy that
        // refuses a user-initiated transaction outside it. The test context has no retry strategy, so this is
        // one of the failures the suite cannot catch.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // A retry re-runs this whole body on the *same* context, which still holds everything the failed
            // attempt staged. Without this, the second attempt re-sends vehicles already marked Deleted (a
            // concurrency exception) and queues a second pending row for one subject (a unique violation) — so
            // the transient failure the strategy exists to absorb becomes the permanent one instead.
            db.ChangeTracker.Clear();

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Materialised and removed rather than ExecuteDelete: Vehicle shares its table with four owned
            // blocks (fluids, tyres, insurance, breakdown), and an account holds a handful of vehicles, so the
            // read costs nothing and sidesteps the question entirely. The 13 direct child tables and the 3
            // indirect ones go with them through the database's own cascades.
            var vehicles = await db.Vehicles.Where(v => v.OwnerId == id).ToListAsync(cancellationToken);
            db.Vehicles.RemoveRange(vehicles);
            await db.SaveChangesAsync(cancellationToken);

            // The write audit cascades from the token it belongs to. A null-owner token predates multi-user and
            // is nobody's to delete; revoked ones are still this account's, so revocation is not a filter here.
            await db.AssistantTokens.Where(t => t.OwnerId == id).ExecuteDeleteAsync(cancellationToken);

            // Explicitly, even though owner_id cascades from users. Relying on a cascade to do something you
            // intended is how the document bytes came to be forgotten in the first place.
            await db.Garages.Where(g => g.OwnerId == id).ExecuteDeleteAsync(cancellationToken);
            await db.WashLocations.Where(w => w.OwnerId == id).ExecuteDeleteAsync(cancellationToken);
            await db.ExpenseCategories.Where(c => c.OwnerId == id).ExecuteDeleteAsync(cancellationToken);

            await db.Users.Where(u => u.Id == id).ExecuteDeleteAsync(cancellationToken);

            // Upsert, because a subject can legitimately arrive here twice: a deployment whose Management grant
            // is missing leaves the first row standing forever, the person signs in again and is provisioned a
            // fresh account, and deletes that one too. ix_pending_identity_deletions_external_id makes an
            // unconditional insert throw, which rolls the whole transaction back and answers 500 — the index was
            // meant to keep the queue at one row per subject, not to refuse the second deletion outright.
            var queued = await db.PendingIdentityDeletions
                .FirstOrDefaultAsync(p => p.ExternalId == user.ExternalId, cancellationToken);

            if (queued is null)
            {
                db.PendingIdentityDeletions.Add(new PendingIdentityDeletion
                {
                    ExternalId = user.ExternalId,
                    RequestedAt = clock.GetUtcNow(),
                });
            }
            else
            {
                // Restated as this request, not carried over from the last one. The attempt count and the error
                // describe the erasure being asked for now, and an obligation dated to an account that no longer
                // exists is the wrong date to answer a regulator with.
                queued.RequestedAt = clock.GetUtcNow();
                queued.Attempts = 0;
                queued.LastError = null;
            }

            await db.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        });

        // After the commit, for the reason DocumentService.DeleteAsync gives: bytes orphaned by a failed unlink
        // are invisible and reclaimable, while rows pointing at deleted files are broken images on a screen. The
        // failure modes are not symmetric, so the order is not arbitrary.
        foreach (var vehicleId in vehicleIds)
        {
            try
            {
                documents.DeleteVehicleFolder(vehicleId);
            }
            catch (Exception ex)
            {
                // Caught per vehicle so one unremovable folder does not keep the rest of them, and caught
                // broadly on purpose: the transaction is already committed, so there is no failure here worth
                // turning a completed erasure into a 500 the caller cannot even retry — they have no account to
                // authenticate with any more. Logged at Error because nothing in the database names this folder
                // now, so this line is the only thing that will ever ask for it back.
                logger.LogError(ex,
                    "Account deletion for owner {OwnerId} removed every row but could not remove the document "
                    + "folder for vehicle {VehicleId}. Those files are orphaned on the documents volume and must "
                    + "be deleted by hand.", id, vehicleId);
            }
        }

        var deletion = await identity.DeleteUserAsync(user.ExternalId, cancellationToken);
        if (deletion.Outcome is IdentityDeletionOutcome.Deleted)
        {
            await db.PendingIdentityDeletions
                .Where(p => p.ExternalId == user.ExternalId)
                .ExecuteDeleteAsync(cancellationToken);

            return new AccountDeletionResult(AccountDeletionOutcome.Deleted, IdentityDeleted: true);
        }

        // The data is gone either way, and that is what the 204 promises. What is left is a login with nothing
        // behind it, which the retry service keeps asking about until the provider agrees.
        await RecordFailureAsync(user.ExternalId, deletion.Detail, cancellationToken);
        return new AccountDeletionResult(AccountDeletionOutcome.Deleted, deletion.Detail);
    }

    /// <summary>
    /// Asks the provider again about every identity still queued. Returns how many were finally erased.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bounded per pass: nothing here is urgent, and an unbounded loop over a table that only grows on failure
    /// is how a retry job becomes the outage. An unconfigured provider stops the pass rather than marking every
    /// row with the same error — the credential is the fix, and fifty identical <c>LastError</c>s do not help
    /// anyone find it.
    /// </para>
    /// <para>
    /// <b>A row whose subject has a live account again is dropped, not sent.</b> This half and
    /// <c>AccountProvisioner</c>'s have to agree or one of them undoes the other: see the comment there for why
    /// coming back cancels the queued identity removal rather than the other way round.
    /// </para>
    /// </remarks>
    public async Task<int> RetryPendingAsync(CancellationToken cancellationToken = default)
    {
        if (!identity.IsConfigured) return 0;

        var pending = await db.PendingIdentityDeletions
            .OrderBy(p => p.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        var cleared = 0;
        foreach (var row in pending)
        {
            // The identity is behind a live account again, so deleting it would lock a person out of data they
            // are using right now and orphan it beyond recovery — the worst outcome in this whole lifecycle,
            // reached by a job whose log line would say it had succeeded. Dropped rather than kept: the queued
            // obligation was to erase the data, and that was discharged when the earlier account went.
            if (await db.Users.AnyAsync(u => u.ExternalId == row.ExternalId, cancellationToken))
            {
                db.PendingIdentityDeletions.Remove(row);
                // Not counted as cleared — nothing was erased at the provider, and the pass says how many were.
                logger.LogInformation(
                    "Dropped the queued identity deletion for {ExternalId}: the subject holds an account again, "
                    + "and the data the queued removal covered went with the earlier one.", row.ExternalId);
                continue;
            }

            var result = await identity.DeleteUserAsync(row.ExternalId, cancellationToken);

            if (result.Outcome is IdentityDeletionOutcome.NotConfigured) break;

            if (result.Outcome is IdentityDeletionOutcome.Deleted)
            {
                db.PendingIdentityDeletions.Remove(row);
                cleared++;
            }
            else
            {
                row.Attempts++;
                // Null rather than "": ck_pending_identity_deletions_last_error refuses the empty string,
                // because a failure that says nothing is worse than a failure that says it said nothing.
                row.LastError = string.IsNullOrWhiteSpace(result.Detail) ? null : result.Detail;
            }
        }

        if (pending.Count > 0) await db.SaveChangesAsync(cancellationToken);
        return cleared;
    }

    /// <remarks>
    /// The row was inserted in the deletion transaction, so this is an update in the ordinary case. It is
    /// written outside a transaction deliberately — the data is already committed, and a failure to record why
    /// the identity survived must not look like a failure to delete.
    /// </remarks>
    private async Task RecordFailureAsync(string externalId, string? detail, CancellationToken cancellationToken)
    {
        var row = await db.PendingIdentityDeletions
            .FirstOrDefaultAsync(p => p.ExternalId == externalId, cancellationToken);
        if (row is null) return;

        row.Attempts++;
        row.LastError = string.IsNullOrWhiteSpace(detail) ? null : detail;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <remarks>
    /// By <c>OwnerId</c> explicitly rather than leaning on the query filter: this is the one operation whose
    /// definition <i>is</i> "everything owner N holds", and the filter would make that read as an accident of
    /// which account the request happens to belong to.
    /// </remarks>
    private Task<List<int>> OwnedVehicleIdsAsync(int ownerId, CancellationToken cancellationToken) =>
        db.Vehicles.AsNoTracking()
            .Where(v => v.OwnerId == ownerId)
            .Select(v => v.Id)
            .ToListAsync(cancellationToken);
}
