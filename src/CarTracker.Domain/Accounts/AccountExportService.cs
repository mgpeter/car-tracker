using System.Text.Json;
using System.Text.Json.Serialization;
using CarTracker.Data;
using CarTracker.Domain.Documents;
using CarTracker.Domain.Expenses;
using CarTracker.Domain.Logs;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Accounts;

/// <summary>
/// Writes everything an account owns as raw rows — UK GDPR Art. 15 and Art. 20 in one response.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing derived appears, and that is the whole design.</b> No MPG, no cost-per-mile, no check status, no
/// spend rollup, no bundle total, no reference count. Every one of those is recomputable from these rows by
/// definition, and an export carrying stored derived figures would reproduce the exact defect the five workbook
/// figures document — in the one artefact whose entire purpose is to be read later, when nothing can recompute
/// it and nothing can contradict it either. Where a shared read wraps rows in derived figures
/// (<c>TaskLog.BundleCost</c>, <c>IssueLog.WorstCaseCost</c>, <c>DocumentLog.TotalSizeBytes</c>,
/// <see cref="Shared.Logs.IssueItem.Watch"/>) this unwraps to the rows and drops the wrapper.
/// </para>
/// <para>
/// <b>Written, not built.</b> The payload goes to the response stream a vehicle at a time through a
/// <see cref="Utf8JsonWriter"/>, flushed between vehicles, rather than materialised into one object graph and
/// serialised. One vehicle is small today; an account with several and years of history is the case that
/// matters, and correct-once costs nothing over correct-later here.
/// </para>
/// </remarks>
public sealed class AccountExportService(
    CarTrackerDbContext db,
    LogQueryService logs,
    ExpenseService expenses,
    DocumentService documents,
    TimeProvider clock)
{
    /// <summary>
    /// The serializer settings, stated rather than inherited.
    /// </summary>
    /// <remarks>
    /// A hand-rolled writer does not go through <c>ConfigureHttpJsonOptions</c>, so nothing here is implied by
    /// the rest of the API: without these, enums would land as integers and properties as PascalCase, and an
    /// export would be the one payload in the app that disagreed with every other one about how a
    /// <see cref="FuelType"/> is spelt.
    /// </remarks>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// What the payload says about itself, because the absences are otherwise indistinguishable from oversights.
    /// </summary>
    private static readonly string[] Notes =
    [
        "Every figure this app displays — fuel economy, cost per mile, check status, spend totals — is worked "
        + "out from these rows when a screen asks for it, and is never stored. That is why no calculated value "
        + "appears in this file: the rows are the record, and the figures follow from them.",

        "Document files are not included, only their details. Each one names the file it refers to; download "
        + "the files themselves individually from the documents screen.",

        "Assistant tokens are listed without their secrets. A secret is shown once when the token is created "
        + "and only a hash of it is ever stored, so there is nothing here to reveal.",
    ];

    /// <summary>
    /// Streams the whole account to <paramref name="destination"/>.
    /// </summary>
    /// <param name="schemaVersion">The app version that wrote it, so a reader knows what shape they have.</param>
    /// <exception cref="InvalidOperationException">
    /// When <paramref name="ownerId"/> names no account. It came from the middleware, which resolved it against
    /// a live row on this same request, so this is a bug rather than a user-facing case — and a named failure
    /// beats an export that is silently empty.
    /// </exception>
    public async Task WriteAsync(
        int ownerId,
        string schemaVersion,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == ownerId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Cannot export account {ownerId}: no such user row. The request resolved an owner that does "
                + "not exist, which means the accessor and the database disagree.");

        await using var writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("exportedAt", clock.GetUtcNow());
        writer.WriteString("schemaVersion", schemaVersion);
        Write(writer, "notes", Notes);

        // The external id is here deliberately. It is the identifier the whole account hangs off, so a subject
        // access response that withheld it would be withholding the one field that explains every other.
        Write(writer, "account", new ExportedAccount(
            user.ExternalId, user.Email, user.DisplayName, user.CreatedAt));

        await WriteReferenceAsync(writer, ownerId, cancellationToken);
        await WriteVehiclesAsync(writer, ownerId, cancellationToken);
        await WriteAssistantAsync(writer, ownerId, cancellationToken);

        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
    }

    /// <remarks>
    /// By <c>OwnerId</c> rather than through the query filter, for the reason the deletion service gives: these
    /// three tables are the account's own lists, and naming the owner is the definition rather than an accident
    /// of whose request it is. Reference <i>counts</i> are absent — they are derived, and they were the quiet
    /// cross-tenant leak this release closed.
    /// </remarks>
    private async Task WriteReferenceAsync(Utf8JsonWriter writer, int ownerId, CancellationToken ct)
    {
        writer.WritePropertyName("reference");
        writer.WriteStartObject();

        Write(writer, "garages", await db.Garages.AsNoTracking()
            .Where(g => g.OwnerId == ownerId).OrderBy(g => g.Name)
            .Select(g => new ExportedGarage(g.Name, g.Contact, g.Address, g.Notes))
            .ToListAsync(ct));

        Write(writer, "washLocations", await db.WashLocations.AsNoTracking()
            .Where(w => w.OwnerId == ownerId).OrderBy(w => w.Name)
            .Select(w => new ExportedWashLocation(w.Name, w.Notes))
            .ToListAsync(ct));

        Write(writer, "expenseCategories", await db.ExpenseCategories.AsNoTracking()
            .Where(c => c.OwnerId == ownerId).OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
            .Select(c => new ExportedExpenseCategory(c.Name, c.DisplayOrder, c.IsSystem))
            .ToListAsync(ct));

        writer.WriteEndObject();
    }

    private async Task WriteVehiclesAsync(Utf8JsonWriter writer, int ownerId, CancellationToken ct)
    {
        var vehicles = await db.Vehicles.AsNoTracking()
            .Where(v => v.OwnerId == ownerId)
            .OrderBy(v => v.Id)
            .ToListAsync(ct);

        writer.WritePropertyName("vehicles");
        writer.WriteStartArray();

        foreach (var vehicle in vehicles)
        {
            writer.WriteStartObject();
            writer.WriteString("registration", vehicle.Registration);

            // The entity itself, not a projection of it. A hand-listed set of ~40 columns is a list that ages
            // silently: add a column and the export quietly stops carrying it, with nothing to fail. The entity
            // has no navigation properties, so this is the row and its four owned blocks and nothing else.
            Write(writer, "profile", vehicle);

            Write(writer, "mileageReadings", await logs.ListMileageAsync(vehicle.Id, ct));
            Write(writer, "fuelEntries", await logs.ListFuelAsync(vehicle.Id, ct));
            Write(writer, "expenses", await expenses.ListAsync(vehicle.Id, ct));
            Write(writer, "serviceRecords", await logs.ListServiceRecordsAsync(vehicle.Id, ct));
            Write(writer, "tyreReadings", await logs.ListTyresAsync(vehicle.Id, ct));
            Write(writer, "washEntries", await logs.ListWashesAsync(vehicle.Id, ct));
            Write(writer, "checkDefinitions", await logs.ListCheckDefinitionsAsync(vehicle.Id, ct));
            Write(writer, "checkLogs", await logs.ListCheckLogsAsync(vehicle.Id, ct));
            // Unwrapped: TaskLog's other three members are the board's bundle figures, all sums over these rows.
            Write(writer, "tasks", (await logs.GetTaskLogAsync(vehicle.Id, ct)).Tasks);
            Write(writer, "issues", await logs.ListIssuesAsync(vehicle.Id, ct));
            Write(writer, "issueWatchChecks", await logs.ListWatchLinksAsync(vehicle.Id, ct));
            Write(writer, "equipment", await logs.ListEquipmentAsync(vehicle.Id, ct));
            Write(writer, "budgetGroups", await logs.ListBudgetGroupsAsync(vehicle.Id, ct));
            Write(writer, "documents", await documents.ListRowsAsync(vehicle.Id, ct));
            // Resolved ones too: a retracted flag is part of the record of what the data did, and an export that
            // showed only what is still open would leave that history out.
            Write(writer, "anomalies", await logs.ListAnomaliesAsync(vehicle.Id, includeResolved: true, ct));

            writer.WriteEndObject();

            // The point at which the bytes leave: one vehicle's rows are in the buffer at a time, not the fleet's.
            await writer.FlushAsync(ct);
        }

        writer.WriteEndArray();
    }

    /// <remarks>
    /// Never <c>TokenHash</c>, and never anything derived from it. The audit trail is included because it is a
    /// record of changes made to this person's data — leaving it out would be the kind of silent omission the
    /// <see cref="Notes"/> array exists to make impossible.
    /// </remarks>
    private async Task WriteAssistantAsync(Utf8JsonWriter writer, int ownerId, CancellationToken ct)
    {
        Write(writer, "assistantTokens", await db.AssistantTokens.AsNoTracking()
            .Where(t => t.OwnerId == ownerId).OrderBy(t => t.Id)
            .Select(t => new ExportedAssistantToken(
                t.Id, t.Name, t.Scope, t.CreatedAt, t.LastUsedAt, t.RevokedAt, t.ReadCount, t.WriteCount))
            .ToListAsync(ct));

        Write(writer, "assistantWriteAudit", await db.AssistantWriteAudits.AsNoTracking()
            .Where(a => db.AssistantTokens.Any(t => t.Id == a.TokenId && t.OwnerId == ownerId))
            .OrderBy(a => a.Id)
            .Select(a => new ExportedAssistantWrite(a.TokenId, a.Tool, a.VehicleId, a.Summary, a.TimestampUtc))
            .ToListAsync(ct));
    }

    private static void Write<T>(Utf8JsonWriter writer, string name, T value)
    {
        writer.WritePropertyName(name);
        JsonSerializer.Serialize(writer, value, Json);
    }

    // The shapes with no DTO of their own, because nothing else reads them: an account's own identity row, its
    // three reference lists without their derived counts, and its tokens without their secrets.

    private sealed record ExportedAccount(
        string ExternalId, string Email, string? DisplayName, DateTimeOffset CreatedAt);

    private sealed record ExportedGarage(string Name, string? Contact, string? Address, string? Notes);

    private sealed record ExportedWashLocation(string Name, string? Notes);

    private sealed record ExportedExpenseCategory(string Name, int DisplayOrder, bool IsSystem);

    private sealed record ExportedAssistantToken(
        int Id,
        string Name,
        AssistantScope Scope,
        DateTimeOffset CreatedAt,
        DateTimeOffset? LastUsedAt,
        DateTimeOffset? RevokedAt,
        int ReadCount,
        int WriteCount);

    private sealed record ExportedAssistantWrite(
        int TokenId, string Tool, int? VehicleId, string Summary, DateTimeOffset TimestampUtc);
}
