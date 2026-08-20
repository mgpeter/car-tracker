using CarTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Accounts.Import;

/// <summary>
/// Reads an account export back in, cloning its garage into the signed-in account beside whatever is already
/// there - UK GDPR Art. 20's other half.
/// </summary>
/// <remarks>
/// <para>
/// <b>The rows are inserted, not replayed, and everything else follows from that.</b> The obvious
/// implementation feeds the file through <c>FuelEntryFactory</c>, <c>ServiceRecordFactory</c>,
/// <c>ExpenseService</c> and <c>CheckSetAdder</c>, so that every invariant is enforced by the code that already
/// enforces it. It is wrong here, and the reason is the mirrors: a fill written through its factory produces
/// three rows - the fill, a <c>MileageReading</c> stamped <c>Fuel</c>, and a mirrored <c>ExpenseEntry</c> - and
/// <b>the export contains all three</b>, because they are three stored rows. An import built on the factories
/// would inflate every money figure on the dashboard by roughly the value of its own mirrors, silently, which
/// is the workbook's doubled-litres defect in a new costume. So the writing happens through the
/// <see cref="CarTrackerDbContext"/> directly and the invariants become assertions on the way in
/// (<see cref="ImportValidator"/>) rather than side effects.
/// </para>
/// <para>
/// <b>Two calls, and the second carries no payload.</b> A preview parses, validates and reports, and writes
/// nothing on any path including the successful one. A commit names a server-held id and carries only
/// decisions about the file the server is already holding. That is <c>PendingWriteStore</c>'s rule from the
/// chat, for the reason recorded there: re-sending the payload with the commit would validate the request
/// against itself.
/// </para>
/// <para>
/// <b>Ownership comes from the accessor and never from the file.</b> Everything is written through the
/// request's owner-pinned context, so the global query filter and <c>ReferenceOwner.Require</c> apply
/// unchanged, and the <c>account</c> block in the file is provenance shown in the preview and written nowhere.
/// An import cannot change who you are.
/// </para>
/// </remarks>
public sealed class AccountImportService(
    CarTrackerDbContext db,
    ICurrentUserAccessor currentUser,
    PendingImportStore pending,
    ImportWriter writer)
{
    /// <summary>
    /// Parses a file, says exactly what importing it would do, and writes nothing.
    /// </summary>
    /// <param name="appVersion">
    /// The running app's <c>VERSION</c>, so the preview can say when the file was written by a later one. The
    /// same figure the export stamps into <c>schemaVersion</c>, passed in for the same reason it is passed to
    /// <see cref="AccountExportService.WriteAsync"/>: the domain does not read the assembly.
    /// </param>
    public async Task<ImportPreviewResult> PreviewAsync(
        Stream file,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.OwnerId is not int ownerId)
            return new ImportPreviewResult(ImportOutcome.NoAccount, Detail: "No account is signed in.");

        var read = await ImportReader.ReadAsync(file, cancellationToken);
        if (read.Outcome is ImportReadOutcome.TooLarge)
            return new ImportPreviewResult(ImportOutcome.TooLarge, Detail: read.Detail);
        if (read.Outcome is not ImportReadOutcome.Ok || read.Payload is null)
            return new ImportPreviewResult(ImportOutcome.Unreadable, Detail: read.Detail);

        var payload = read.Payload;

        var errors = ImportValidator.Validate(payload);
        if (errors.Count > 0)
        {
            return new ImportPreviewResult(ImportOutcome.Invalid, Errors: errors,
                Detail: "The file is readable, but some of what it describes cannot be created. "
                    + "Each problem below names where it is in the file.");
        }

        var preview = await BuildPreviewAsync(ownerId, payload, appVersion, cancellationToken);
        var importId = pending.Remember(new PendingImport(ownerId, payload, preview));

        return new ImportPreviewResult(ImportOutcome.Previewed, preview with { ImportId = importId });
    }

    /// <summary>Writes a previewed import, under the decisions the caller made about it.</summary>
    public Task<ImportCommitResult> CommitAsync(
        string importId,
        IReadOnlyList<ImportVehicleDecision>? decisions,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.OwnerId is not int ownerId)
            return Task.FromResult(new ImportCommitResult(ImportOutcome.NoAccount, Detail: "No account is signed in."));

        var held = pending.Find(importId, currentUser);
        if (held is null)
        {
            // A foreign id answers exactly as an expired one does. The wording covers both because the server
            // will not say which, and because "it expired" is overwhelmingly the true one.
            return Task.FromResult(new ImportCommitResult(ImportOutcome.NotFound,
                Detail: "That upload is no longer held. A preview lasts fifteen minutes; upload the file again."));
        }

        return CommitHeldAsync(ownerId, importId, held, decisions ?? [], cancellationToken);
    }

    private async Task<ImportCommitResult> CommitHeldAsync(
        int ownerId,
        string importId,
        PendingImport held,
        IReadOnlyList<ImportVehicleDecision> decisions,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var byIndex = new Dictionary<int, ImportVehicleDecision>();

        foreach (var decision in decisions)
        {
            if (decision.Index < 0 || decision.Index >= held.Payload.Vehicles.Count)
            {
                errors[$"vehicles[{decision.Index}]"] =
                    [$"The preview described {held.Payload.Vehicles.Count} vehicle(s), so there is no vehicle "
                     + $"{decision.Index} to decide about."];
                continue;
            }

            // Last one wins rather than a refusal: two decisions for one index is a client sending a list it
            // built twice, not an ambiguity worth failing an import over.
            byIndex[decision.Index] = decision;
        }

        if (errors.Count > 0)
            return new ImportCommitResult(ImportOutcome.Invalid, Errors: errors, Detail: "Unknown vehicle.");

        // Re-read from the database rather than trusting the preview's proposals. Minutes pass between the two
        // calls and a vehicle can be added in them - by another tab, by the assistant, by a first import whose
        // second is being committed now.
        var taken = await TakenRegistrationsAsync(ownerId, cancellationToken);
        var plan = new List<ImportVehiclePlan>();

        for (var i = 0; i < held.Payload.Vehicles.Count; i++)
        {
            // Absent means included. Omitting a vehicle is not the same as excluding it, so an empty decisions
            // array imports everything exactly as previewed.
            var decision = byIndex.GetValueOrDefault(i);
            if (decision is { Include: false }) continue;

            var vehicle = held.Payload.Vehicles[i];
            var source = vehicle.Plate;
            var chosen = decision?.Registration?.Trim();

            if (string.IsNullOrEmpty(chosen))
            {
                // No override: re-propose against the database as it is now, not as it was at preview time.
                chosen = RegistrationProposer.Propose(source, taken);
            }
            else if (chosen.Length > RegistrationProposer.MaxLength)
            {
                errors[$"vehicles[{i}].registration"] =
                    [$"'{chosen}' is longer than {RegistrationProposer.MaxLength} characters."];
                continue;
            }
            else if (taken.Contains(RegistrationProposer.Normalise(chosen)))
            {
                // A distinct outcome from "invalid", because the fix is different: the caller picks another
                // plate and re-commits against the same id, which is why the id survives this refusal.
                return new ImportCommitResult(ImportOutcome.Collision,
                    Detail: $"'{chosen}' is already in your garage. Choose another registration for {source}.",
                    Errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        [$"vehicles[{i}].registration"] = [$"'{chosen}' is already in your garage."],
                    });
            }

            taken.Add(RegistrationProposer.Normalise(chosen));
            plan.Add(new ImportVehiclePlan(vehicle, chosen, source));
        }

        if (errors.Count > 0)
            return new ImportCommitResult(ImportOutcome.Invalid, Errors: errors, Detail: "Check the registrations.");

        var report = await writer.WriteAsync(ownerId, held.Payload, plan, cancellationToken);

        // Consumed on success only. A collision above leaves it standing, so correcting one plate does not
        // cost a re-upload of the whole file.
        pending.Forget(importId);

        return new ImportCommitResult(ImportOutcome.Committed, report);
    }

    private async Task<ImportPreview> BuildPreviewAsync(
        int ownerId, ImportPayload payload, string appVersion, CancellationToken ct)
    {
        // By OwnerId rather than through the query filter, for the reason the export gives: these three tables
        // are the account's own lists, and naming the owner is the definition rather than an accident of whose
        // request it is.
        var garages = await db.Garages.AsNoTracking()
            .Where(g => g.OwnerId == ownerId).Select(g => g.Name).ToListAsync(ct);
        var washLocations = await db.WashLocations.AsNoTracking()
            .Where(w => w.OwnerId == ownerId).Select(w => w.Name).ToListAsync(ct);
        var categories = await db.ExpenseCategories.AsNoTracking()
            .Where(c => c.OwnerId == ownerId).Select(c => c.Name).ToListAsync(ct);

        var reference = new ImportReferencePreview(
            Count(payload.Reference.Garages.Select(g => g.Name), garages),
            Count(payload.Reference.WashLocations.Select(w => w.Name), washLocations),
            Count(payload.Reference.ExpenseCategories.Select(c => c.Name), categories));

        var taken = await TakenRegistrationsAsync(ownerId, ct);
        var vehicles = new List<ImportVehiclePreview>(payload.Vehicles.Count);

        for (var i = 0; i < payload.Vehicles.Count; i++)
        {
            var vehicle = payload.Vehicles[i];
            var plate = vehicle.Plate;
            var collides = taken.Contains(RegistrationProposer.Normalise(plate));
            var proposed = RegistrationProposer.Propose(plate, taken);

            // Claimed against the running set, so the second copy of one plate in one file is proposed -3
            // rather than -2 twice.
            taken.Add(RegistrationProposer.Normalise(proposed));

            vehicles.Add(new ImportVehiclePreview(
                i, plate, Describe(vehicle), collides, proposed,
                new ImportRowCounts(
                    vehicle.MileageReadings.Count, vehicle.FuelEntries.Count, vehicle.Expenses.Count,
                    vehicle.ServiceRecords.Count, vehicle.TyreReadings.Count, vehicle.WashEntries.Count,
                    vehicle.CheckDefinitions.Count, vehicle.CheckLogs.Count, vehicle.Tasks.Count,
                    vehicle.Issues.Count, vehicle.IssueWatchChecks.Count, vehicle.Equipment.Count,
                    vehicle.BudgetGroups.Count),
                new ImportSkipped(vehicle.Documents.Count, vehicle.Anomalies.Count)));
        }

        var newer = IsNewerThan(payload.SchemaVersion, appVersion);

        return new ImportPreview(
            // Filled in by the caller once the payload is actually held. A preview object that carried an id
            // nothing was stored under would be a promise the store had not made.
            ImportId: string.Empty,
            new ImportSource(
                payload.ExportedAt, payload.SchemaVersion,
                payload.Account?.Email, payload.Account?.DisplayName, newer),
            reference,
            vehicles,
            Warnings(payload, vehicles, newer, appVersion));
    }

    /// <summary>Every registration this account already owns, normalised the way the unique index compares them.</summary>
    private async Task<HashSet<string>> TakenRegistrationsAsync(int ownerId, CancellationToken ct)
    {
        var registrations = await db.Vehicles.AsNoTracking()
            .Where(v => v.OwnerId == ownerId)
            .Select(v => v.Registration)
            .ToListAsync(ct);

        return registrations.Select(RegistrationProposer.Normalise).ToHashSet(StringComparer.Ordinal);
    }

    /// <remarks>
    /// Ordinal, because the reference tables are keyed on <c>Name</c> and Postgres compares a primary key
    /// exactly. "K &amp; P Motors" and "k &amp; p motors" are two rows here, which is the same judgement
    /// <c>ReferenceWriter</c> makes on the write path and for the same reason: deciding two similar names are
    /// one place is the reference-list editor's job, not a write path's.
    /// </remarks>
    private static ImportListPreview Count(IEnumerable<string> inFile, IEnumerable<string> mine)
    {
        var owned = mine.ToHashSet(StringComparer.Ordinal);
        var names = inFile.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.Ordinal).ToList();
        var already = names.Count(owned.Contains);

        return new ImportListPreview(names.Count, names.Count - already, already);
    }

    private static string Describe(ImportedVehicle vehicle)
    {
        if (vehicle.Profile is not { } p) return string.Empty;

        var variant = string.IsNullOrWhiteSpace(p.Variant) ? string.Empty : $" {p.Variant}";
        return $"{p.Year} {p.Make} {p.Model}{variant}".Trim();
    }

    /// <summary>
    /// The sentences the panel leads with, ordered by what would cost the reader most to miss.
    /// </summary>
    private static IReadOnlyList<string> Warnings(
        ImportPayload payload, IReadOnlyList<ImportVehiclePreview> vehicles, bool newer, string appVersion)
    {
        var warnings = new List<string>();
        var colliding = vehicles.Count(v => v.Collides);

        // First, always, and this is the one that stops an accidental second import: renaming on collision
        // gave up the idempotency the uniqueness index would otherwise have provided for free.
        if (colliding > 0)
        {
            warnings.Add(
                $"{colliding} of {vehicles.Count} vehicle{(vehicles.Count == 1 ? string.Empty : "s")} already "
                + $"exist{(colliding == 1 ? "s" : string.Empty)} in your garage and will be imported as "
                + $"cop{(colliding == 1 ? "y" : "ies")} under a changed registration.");
        }

        var documents = payload.Vehicles.Sum(v => v.Documents.Count);
        if (documents > 0)
        {
            warnings.Add(
                $"{documents} document record{(documents == 1 ? "" : "s")} name files this export does not "
                + "contain, and will not be imported. Download those files from the documents screen of the "
                + "account they came from.");
        }

        var anomalies = payload.Vehicles.Sum(v => v.Anomalies.Count);
        if (anomalies > 0)
        {
            warnings.Add(
                $"{anomalies} data-integrity flag{(anomalies == 1 ? " is" : "s are")} not imported. They are "
                + "worked out again from the rows once those land, so the queue will describe this database "
                + "rather than the one they came from.");
        }

        var tokens = payload.AssistantTokens.Count;
        if (tokens > 0)
        {
            warnings.Add(
                $"{tokens} assistant token{(tokens == 1 ? " is" : "s are")} listed in the file without "
                + "secrets, so there is nothing to restore. Mint new ones under Assistant access.");
        }

        if (newer)
        {
            warnings.Add(
                $"This file was written by version {payload.SchemaVersion}, which is newer than this app "
                + $"({appVersion}). Anything that version records and this one does not will be dropped.");
        }

        return warnings;
    }

    /// <summary>
    /// Whether the file came from a later release.
    /// </summary>
    /// <remarks>
    /// Both figures can carry the informational suffix an assembly version has (<c>0.18.0+2f9c1a</c>), so the
    /// build metadata is cut before parsing. Anything unparseable answers false: a version that cannot be read
    /// is not evidence of anything, and a warning shown on every import would train people to ignore it.
    /// </remarks>
    internal static bool IsNewerThan(string? fileVersion, string? appVersion) =>
        TryParse(fileVersion) is { } file && TryParse(appVersion) is { } app && file > app;

    private static Version? TryParse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;

        var core = version.Split('+', '-')[0];
        return Version.TryParse(core, out var parsed) ? parsed : null;
    }
}

/// <summary>What the caller decided about one previewed vehicle.</summary>
/// <param name="Index">Its position in the preview's vehicle list.</param>
/// <param name="Include">
/// Defaults to true, and a vehicle the request does not mention is included. Omitting one is not the same as
/// excluding it - a client that sends an empty array gets the import it was shown.
/// </param>
/// <param name="Registration">The plate to use. Absent means the server's proposal stands.</param>
public sealed record ImportVehicleDecision(int Index, bool Include = true, string? Registration = null);

/// <summary>One vehicle, with the plate settled. The writer takes a list of these and nothing else.</summary>
/// <param name="ImportedFrom">The registration in the file, which the vehicle's notes will record.</param>
public sealed record ImportVehiclePlan(ImportedVehicle Vehicle, string Registration, string ImportedFrom);
