using CarTracker.Data;
using CarTracker.Domain.Documents;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CarTracker.Domain.Vehicles;

/// <summary>What deleting this vehicle would destroy, so the confirmation can state it before it arms.</summary>
/// <param name="LogEntryCount">
/// One number across the ten per-vehicle log tables, for the reason <c>AccountDeletionService</c> gives about
/// its own equivalent: the screen is establishing weight rather than producing an inventory. Check
/// definitions and budget groups are configuration rather than entries and are counted separately; anomalies
/// are flags the app raised rather than rows anyone typed, and counting those would inflate the figure the
/// consent rests on.
/// </param>
public sealed record VehicleDeletionSummary(
    string Registration,
    string Name,
    VehicleStatus Status,
    bool IsDefault,
    int LogEntryCount,
    int DocumentCount,
    long DocumentBytes,
    int CheckDefinitionCount,
    int IssueCount);

/// <summary>How a vehicle deletion ended. Each maps to a distinct status because each has a distinct fix.</summary>
public enum VehicleDeletionOutcome
{
    /// <summary>The vehicle and everything under it is gone.</summary>
    Deleted = 1,

    /// <summary>
    /// No such vehicle for this account. <b>Including one that belongs to somebody else</b>, which answers
    /// identically because it resolves through the owner query filter and simply is not there.
    /// </summary>
    NotFound = 2,

    /// <summary>The typed registration did not match. Nothing was touched.</summary>
    ConfirmationMismatch = 3,
}

/// <param name="PromotedRegistration">
/// The vehicle that became the account's default because the deleted one was. Null when the deleted vehicle
/// was not the default, or when no vehicle is left to promote.
/// </param>
public sealed record VehicleDeletionResult(
    VehicleDeletionOutcome Outcome,
    string? Detail = null,
    string? Field = null,
    string? PromotedRegistration = null);

/// <summary>
/// Destroys one vehicle and everything filed under it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every refusal is decided here, not at the endpoint.</b> There is no <c>CarTracker.WebApi.Tests</c>
/// project, and this is the second most destructive operation in the app; the endpoint keeps only the mapping
/// from an outcome to a status code, which is genuinely about HTTP. The same rule
/// <see cref="Accounts.AccountDeletionService"/> states, for the same reason.
/// </para>
/// <para>
/// <b>The safety property is the query filter, not a hand-written predicate.</b> The vehicle is resolved
/// through <c>db.Vehicles</c>, which <c>CarTrackerDbContext</c> scopes to the signed-in owner, so another
/// account's registration does not resolve and answers NotFound without leaking that it exists. Writing
/// <c>OwnerId == x</c> here instead would work today and would be the thing a future endpoint forgets.
/// </para>
/// <para>
/// <b>There is deliberately no "you cannot delete your last vehicle" rule.</b> A mistyped plate at creation is
/// the likeliest reason anyone deletes a vehicle at all, and an empty garage is a designed state the garage
/// screen already renders. Inventing a rule the rest of the app does not have would make the common case the
/// refused one.
/// </para>
/// <para>
/// <b>And no MCP tool.</b> <c>AccountEndpoints</c> set the precedent and the reasoning carries unchanged: the
/// blast radius of a leaked read-write token stays where DEC-014 put it. An unattended assistant must not be
/// able to destroy a car's whole history.
/// </para>
/// </remarks>
public sealed class VehicleDeletionService(
    CarTrackerDbContext db,
    DocumentStore documents,
    ILogger<VehicleDeletionService> logger)
{
    /// <summary>The counts the confirmation states, or null when this account has no such vehicle.</summary>
    public async Task<VehicleDeletionSummary?> GetSummaryAsync(
        int vehicleId, CancellationToken cancellationToken = default)
    {
        var vehicle = await db.Vehicles.AsNoTracking()
            .Where(v => v.Id == vehicleId)
            .Select(v => new { v.Registration, v.Make, v.Model, v.Status, v.IsDefault })
            .FirstOrDefaultAsync(cancellationToken);

        if (vehicle is null) return null;

        // The ten tables an owner would call "things I logged".
        var logEntryCount =
            await db.MileageReadings.CountAsync(x => x.VehicleId == vehicleId, cancellationToken)
            + await db.FuelEntries.CountAsync(x => x.VehicleId == vehicleId, cancellationToken)
            + await db.ExpenseEntries.CountAsync(x => x.VehicleId == vehicleId, cancellationToken)
            + await db.ServiceRecords.CountAsync(x => x.VehicleId == vehicleId, cancellationToken)
            + await db.TyreReadings.CountAsync(x => x.VehicleId == vehicleId, cancellationToken)
            + await db.WashEntries.CountAsync(x => x.VehicleId == vehicleId, cancellationToken)
            + await db.MaintenanceTasks.CountAsync(x => x.VehicleId == vehicleId, cancellationToken)
            + await db.Issues.CountAsync(x => x.VehicleId == vehicleId, cancellationToken)
            + await db.EquipmentItems.CountAsync(x => x.VehicleId == vehicleId, cancellationToken)
            // Through the definitions, because check_logs carries no vehicle column.
            + await db.CheckLogs.CountAsync(
                l => db.CheckDefinitions.Any(d => d.Id == l.CheckDefinitionId && d.VehicleId == vehicleId),
                cancellationToken);

        var documentRows = await db.Documents
            .Where(d => d.VehicleId == vehicleId)
            .Select(d => d.SizeBytes)
            .ToListAsync(cancellationToken);

        return new VehicleDeletionSummary(
            vehicle.Registration,
            $"{vehicle.Make} {vehicle.Model}".Trim(),
            vehicle.Status,
            vehicle.IsDefault,
            logEntryCount,
            documentRows.Count,
            documentRows.Sum(),
            await db.CheckDefinitions.CountAsync(d => d.VehicleId == vehicleId, cancellationToken),
            await db.Issues.CountAsync(i => i.VehicleId == vehicleId, cancellationToken));
    }

    /// <summary>
    /// Deletes the vehicle named by <paramref name="vehicleId"/>, once the typed confirmation matches.
    /// </summary>
    /// <param name="confirmRegistration">
    /// The registration, typed out. Compared through <see cref="VehicleResolver.Normalize"/> rather than
    /// ordinally, because "bt53 akj" and "BT53 AKJ" are one car on every other screen and a gate that
    /// disagrees with the app's own definition of a plate teaches nothing.
    /// </param>
    public async Task<VehicleDeletionResult> DeleteAsync(
        int vehicleId,
        string? confirmRegistration,
        CancellationToken cancellationToken = default)
    {
        var vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.Id == vehicleId, cancellationToken);
        if (vehicle is null)
        {
            return new VehicleDeletionResult(VehicleDeletionOutcome.NotFound,
                "No such vehicle in this account.");
        }

        if (string.IsNullOrWhiteSpace(confirmRegistration)
            || VehicleResolver.Normalize(confirmRegistration) != VehicleResolver.Normalize(vehicle.Registration))
        {
            return new VehicleDeletionResult(VehicleDeletionOutcome.ConfirmationMismatch,
                $"Type {vehicle.Registration} exactly to confirm. This cannot be undone.",
                Field: "confirmRegistration");
        }

        // Captured before anything goes: after the row is removed nothing names its document folder, and
        // nothing remembers whether it held the account's default.
        var ownerId = vehicle.OwnerId;
        var wasDefault = vehicle.IsDefault;
        string? promoted = null;

        // Mandatory, not decorative: Aspire's EnrichNpgsqlDbContext installs a retrying execution strategy that
        // refuses a user-initiated transaction outside it. The test context has no retry strategy, so this is
        // one of the failures the suite cannot catch.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // A retry re-runs this whole body on the same context, which still holds everything the failed
            // attempt staged, so the transient failure the strategy exists to absorb would become a permanent
            // one instead.
            db.ChangeTracker.Clear();
            promoted = null;

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            // Re-read inside the transaction, because the clear above detached the instance loaded outside it.
            var row = await db.Vehicles.FirstAsync(v => v.Id == vehicleId, cancellationToken);

            // Materialised and removed rather than ExecuteDelete: Vehicle shares its table with four owned
            // blocks (fluids, tyres, insurance, breakdown). The 13 direct child tables and the 3 indirect ones
            // go with it through the database's own cascades.
            db.Vehicles.Remove(row);
            await db.SaveChangesAsync(cancellationToken);

            await ReleaseAuditRowsAsync(vehicleId, cancellationToken);

            if (wasDefault && ownerId is int owner)
            {
                promoted = await PromoteDefaultAsync(owner, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        });

        // After the commit, for the reason DocumentService.DeleteAsync gives: bytes orphaned by a failed unlink
        // are invisible and reclaimable, while rows pointing at deleted files are broken images on a screen.
        // The failure modes are not symmetric, so the order is not arbitrary.
        try
        {
            documents.DeleteVehicleFolder(vehicleId);
        }
        catch (Exception ex)
        {
            // Caught broadly on purpose: the transaction has already committed, so there is no failure here
            // worth turning a completed deletion into a 500 the caller would retry against a vehicle that no
            // longer exists. Logged at Error because nothing in the database names this folder now, so this
            // line is the only thing that will ever ask for it back.
            logger.LogError(ex,
                "Vehicle {VehicleId} was deleted but its document folder could not be removed. Those files are "
                + "orphaned on the documents volume and must be deleted by hand.", vehicleId);
        }

        return new VehicleDeletionResult(VehicleDeletionOutcome.Deleted, PromotedRegistration: promoted);
    }

    /// <summary>
    /// Detaches the assistant's write audit from the vehicle it named, rather than deleting it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>assistant_write_audits.vehicle_id</c> is a plain nullable column with <b>no foreign key</b>, so a
    /// delete leaves rows naming a vehicle id nothing can resolve. Null already means "not vehicle-scoped" and
    /// both the audit view and the account export already handle it, so this puts the rows into a state the
    /// app understands.
    /// </para>
    /// <para>
    /// <b>Deleting them instead would be wrong</b>: the trail records what a *token* did, and it is presented
    /// on the account screen as that token's history, so erasing it destroys audit the owner did not ask to
    /// delete. <b>Leaving the dead id is the worst of the three</b>: Postgres does not reuse identity values,
    /// so it would not name the wrong car, but it names a car nothing can resolve and no reader can tell that
    /// from a bug.
    /// </para>
    /// <para>
    /// A real FK with <c>ON DELETE SET NULL</c> was considered and rejected: it is a migration that makes the
    /// audit table's integrity depend on <c>vehicles</c>, and it buys nothing this one statement does not,
    /// unless the audit is ever surfaced per vehicle.
    /// </para>
    /// <para>
    /// Scoping by <c>VehicleId</c> alone is exact even though this table carries no query filter, because a
    /// vehicle id is globally unique.
    /// </para>
    /// </remarks>
    private Task ReleaseAuditRowsAsync(int vehicleId, CancellationToken cancellationToken) =>
        db.AssistantWriteAudits
            .Where(a => a.VehicleId == vehicleId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.VehicleId, (int?)null), cancellationToken);

    /// <summary>
    /// Gives the account a default again when the deleted vehicle was it. Returns the promoted registration.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Zero defaults is legal under <c>ix_vehicles_default</c>, which is partial, but it is a state an account
    /// can enter and never leave: <see cref="VehicleFactory"/> sets <c>IsDefault</c> only for an owner's first
    /// vehicle, and nothing else sets it at all. Two things then change silently. The garage's top card moves,
    /// because <c>VehicleMetricsLoader</c> orders default-first. And <see cref="VehicleResolver"/> falls back
    /// to the lowest id for every MCP tool and chat turn that omits a vehicle. Both degrade to "the oldest
    /// car", which is a fine answer that has become an accident rather than a choice.
    /// </para>
    /// <para>
    /// <b>Active first, then oldest.</b> Promoting a Sold car would make the assistant resolve, by default, a
    /// car the owner no longer has.
    /// </para>
    /// <para>
    /// Inside the caller's transaction and after the delete, so the partial unique index never sees two
    /// claimants at once.
    /// </para>
    /// </remarks>
    private async Task<string?> PromoteDefaultAsync(int ownerId, CancellationToken cancellationToken)
    {
        var replacement = await db.Vehicles
            .Where(v => v.OwnerId == ownerId)
            .OrderBy(v => v.Status == VehicleStatus.Active ? 0 : 1)
            .ThenBy(v => v.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (replacement is null) return null;

        replacement.IsDefault = true;
        await db.SaveChangesAsync(cancellationToken);

        return replacement.Registration;
    }
}
