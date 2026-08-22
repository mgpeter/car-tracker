using CarTracker.Data;
using CarTracker.Domain.Accounts;
using CarTracker.Domain.Writes;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Documents;

/// <summary>
/// Filing, listing, re-tagging and removing documents — the shared path behind the REST group, and the seam any
/// future <c>search_documents</c> MCP tool reuses.
/// </summary>
/// <remarks>
/// <para>
/// This is the one write path in the app that does <b>not</b> call <c>AnomalyScanner</c>, and that is correct
/// rather than an omission: a document is not a derived input. It moves no figure, has no odometer reading in
/// it, and trips no detector. Filing the MOT certificate does not change what the MOT countdown says — the
/// logged pass already did that.
/// </para>
/// <para>
/// Deleting is the only operation that removes bytes. The three link FKs point <i>outward</i> and are
/// <c>SetNull</c>, so nothing cascades <i>into</i> a document: delete the service record and the certificate
/// survives with its link severed, which is the whole point of evidence outliving its subject.
/// </para>
/// </remarks>
public sealed class DocumentService(
    CarTrackerDbContext context,
    DocumentStore store,
    IAccountEntitlements entitlements)
{
    /// <summary>How much of the account's document allowance is used, and whether another file would fit.</summary>
    /// <param name="Used">Documents held right now, across every vehicle the account owns.</param>
    /// <param name="Limit">What the plan allows.</param>
    public sealed record DocumentCapacity(int Used, int Limit)
    {
        public bool HasRoom => Used < Limit;

        public int Remaining => Math.Max(0, Limit - Used);
    }

    /// <summary>
    /// What the account has filed against what its plan allows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Counted, never stored.</b> The rows are already owner-scoped - <c>documents</c> is reached only
    /// through a vehicle, and the vehicle query filter is what makes that true - so the count is a
    /// <c>COUNT(*)</c> and there is no counter to fall out of step with the files. That is the whole reason
    /// this allowance needs no table while <see cref="VehicleLookupUsage"/> does: a lookup leaves no row to
    /// count, and a document is nothing but a row.
    /// </para>
    /// <para>
    /// Per account rather than per vehicle, deliberately. The volume is what is being bounded, and a
    /// per-vehicle cap is lifted by adding a car.
    /// </para>
    /// </remarks>
    public async Task<DocumentCapacity> CapacityAsync(CancellationToken cancellationToken = default)
    {
        var allowances = await entitlements.AllowancesAsync(cancellationToken);

        // The filter on Vehicles is what scopes this to the account; Documents carries no OwnerId of its own.
        // The correlated subquery is the same shape the fifteen child statements DEC-018 scoped already use.
        var used = await context.Documents
            .CountAsync(d => context.Vehicles.Any(v => v.Id == d.VehicleId), cancellationToken);

        return new DocumentCapacity(used, allowances.MaxDocuments);
    }

    /// <summary>Papers and photos, split as the two halves of the screen render them.</summary>
    public async Task<DocumentLog> GetLogAsync(int vehicleId, CancellationToken cancellationToken = default)
    {
        var rows = await context.Documents
            .AsNoTracking()
            .Where(d => d.VehicleId == vehicleId)
            .OrderByDescending(d => d.DocumentDate ?? DateOnly.MinValue)
            .ThenByDescending(d => d.Id)
            .ToListAsync(cancellationToken);

        var items = new List<DocumentItem>(rows.Count);
        foreach (var row in rows)
            items.Add(ToItem(row, await DescribeLinkAsync(row, cancellationToken)));

        return new DocumentLog(
            Papers: items.Where(d => d.Type != DocumentType.Photo).ToList(),
            Photos: items.Where(d => d.Type == DocumentType.Photo).ToList(),
            TotalCount: items.Count,
            TotalSizeBytes: rows.Sum(r => r.SizeBytes));
    }

    /// <summary>Every document row as stored, oldest first — no link label, no totals.</summary>
    /// <remarks>
    /// The export's read, beside <see cref="GetLogAsync"/> rather than instead of it. That one splits papers
    /// from photos, counts them and sums their bytes, and names the record each is attached to by reading that
    /// record — four figures the screen needs and an export must not carry, because every one of them is
    /// recomputable from these rows and would age the moment it was written.
    /// </remarks>
    public Task<List<DocumentRowItem>> ListRowsAsync(int vehicleId, CancellationToken cancellationToken = default) =>
        context.Documents
            .AsNoTracking()
            .Where(d => d.VehicleId == vehicleId)
            .OrderBy(d => d.Id)
            .Select(d => new DocumentRowItem(
                d.Id, d.Type, d.Title, d.DocumentDate, d.FilePath, d.ContentType, d.SizeBytes, d.Sha256,
                d.ServiceRecordId, d.ExpenseEntryId, d.IssueId, d.Notes))
            .ToListAsync(cancellationToken);

    public Task<Document?> FindAsync(int vehicleId, int id, CancellationToken cancellationToken = default) =>
        context.Documents.FirstOrDefaultAsync(d => d.Id == id && d.VehicleId == vehicleId, cancellationToken);

    /// <summary>
    /// Records an already-stored file. The bytes are on the volume by the time this runs — the endpoint streams
    /// them through <see cref="DocumentStore"/> first, because the content hash is needed to name the file and
    /// the file name is what this row stores.
    /// </summary>
    /// <remarks>
    /// A byte-identical file already filed <b>for this vehicle</b> is refused rather than filed twice. Not
    /// merely "the file was on disk": content-addressed storage means an identical upload always finds its
    /// bytes there, so disk presence alone would refuse legitimate re-filing after a delete. The row is the
    /// question, and the answer names the existing title so the refusal is actionable.
    /// </remarks>
    public async Task<WriteResult<DocumentItem>> RecordAsync(
        int vehicleId,
        StoredFile stored,
        string contentType,
        DocumentType type,
        string title,
        DateOnly? documentDate,
        int? serviceRecordId,
        int? expenseEntryId,
        int? issueId,
        string? notes,
        EntrySource source,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            return WriteResult<DocumentItem>.Invalid("Title", "A document needs a title.");

        // The endpoint asks this before it streams a byte, so a refusal here means a caller that did not - and
        // the cost of arriving at it is orphaned bytes on the volume. Guarded anyway: the alternative to a
        // second check is a future caller silently walking past the allowance, which is the failure mode
        // ImportWriter's validator exists to prevent one layer up.
        if (await CapacityAsync(cancellationToken) is { HasRoom: false } full)
            return WriteResult<DocumentItem>.Invalid("File", FullMessage(full));

        var duplicate = await context.Documents
            .FirstOrDefaultAsync(d => d.VehicleId == vehicleId && d.Sha256 == stored.Sha256, cancellationToken);
        if (duplicate is not null)
        {
            return WriteResult<DocumentItem>.Invalid("File",
                $"This file is already filed as '{duplicate.Title}'. The bytes are identical, so it would be "
                + "the same document twice.");
        }

        if (await ValidateLinkAsync(vehicleId, serviceRecordId, expenseEntryId, issueId, cancellationToken) is { } error)
            return WriteResult<DocumentItem>.Invalid(error.Field, error.Message);

        var document = new Document
        {
            VehicleId = vehicleId,
            Type = type,
            Title = title.Trim(),
            DocumentDate = documentDate,
            FilePath = stored.RelativePath,
            ContentType = contentType,
            SizeBytes = stored.SizeBytes,
            Sha256 = stored.Sha256,
            ServiceRecordId = serviceRecordId,
            ExpenseEntryId = expenseEntryId,
            IssueId = issueId,
            Notes = notes,
            Source = source,
        };

        context.Documents.Add(document);
        await context.SaveChangesAsync(cancellationToken);

        return WriteResult<DocumentItem>.Created(
            ToItem(document, await DescribeLinkAsync(document, cancellationToken)));
    }

    /// <summary>
    /// The refusal, said once so the endpoint and the domain cannot word it two ways.
    /// </summary>
    /// <remarks>
    /// It states the figure rather than the plan, because "100 of 100" is actionable and "upgrade" is not:
    /// deleting something is a fix available on every tier, and it is often the right one. The upsell belongs on
    /// the account screen, where there is room to make the case.
    /// </remarks>
    public static string FullMessage(DocumentCapacity capacity) =>
        $"This account is holding {capacity.Used} of its {capacity.Limit} documents. Remove one to file another.";

    /// <summary>Re-tags a document: its type, title, date, notes, and which record it is attached to.</summary>
    public async Task<WriteResult<DocumentItem>> UpdateAsync(
        int vehicleId, int id, DocumentPatch patch, CancellationToken cancellationToken = default)
    {
        var document = await FindAsync(vehicleId, id, cancellationToken);
        if (document is null) return WriteResult<DocumentItem>.NotFound();

        if (patch.Title is { } title && string.IsNullOrWhiteSpace(title))
            return WriteResult<DocumentItem>.Invalid("Title", "A document needs a title.");

        // Detach first, so "clear the link and attach it elsewhere" in one patch does not trip the
        // at-most-one-link rule against the link it is replacing.
        if (patch.ClearLink)
        {
            document.ServiceRecordId = null;
            document.ExpenseEntryId = null;
            document.IssueId = null;
        }

        var service = patch.ServiceRecordId ?? document.ServiceRecordId;
        var expense = patch.ExpenseEntryId ?? document.ExpenseEntryId;
        var issue = patch.IssueId ?? document.IssueId;

        if (await ValidateLinkAsync(vehicleId, service, expense, issue, cancellationToken) is { } error)
            return WriteResult<DocumentItem>.Invalid(error.Field, error.Message);

        document.Type = patch.Type ?? document.Type;
        document.Title = patch.Title?.Trim() ?? document.Title;
        document.DocumentDate = patch.DocumentDate ?? document.DocumentDate;
        document.Notes = patch.Notes ?? document.Notes;
        document.ServiceRecordId = service;
        document.ExpenseEntryId = expense;
        document.IssueId = issue;

        await context.SaveChangesAsync(cancellationToken);
        return WriteResult<DocumentItem>.Updated(
            ToItem(document, await DescribeLinkAsync(document, cancellationToken)));
    }

    /// <summary>
    /// Removes the row and, unless another row still points at the same bytes, the file.
    /// </summary>
    /// <remarks>
    /// The row goes first and the file second: an orphaned file wastes disk, while an orphaned row is a
    /// document the screen offers to open and cannot. Of the two failure modes only one lies to the owner.
    /// </remarks>
    public async Task<WriteResult<bool>> DeleteAsync(
        int vehicleId, int id, CancellationToken cancellationToken = default)
    {
        var document = await FindAsync(vehicleId, id, cancellationToken);
        if (document is null) return WriteResult<bool>.NotFound();

        var path = document.FilePath;
        context.Documents.Remove(document);
        await context.SaveChangesAsync(cancellationToken);

        var stillReferenced = await context.Documents.AnyAsync(d => d.FilePath == path, cancellationToken);
        store.Delete(path, stillReferenced);

        return WriteResult<bool>.Updated(true);
    }

    /// <summary>
    /// At most one link, and it must be this vehicle's own record.
    /// </summary>
    /// <remarks>
    /// A document is filed and optionally attached to exactly one thing. Two links would make "open the record
    /// this belongs to" ambiguous, and the chip row would have to grow a rule about precedence for a case
    /// nothing needs. The same-vehicle check is the guard the FKs cannot give: they enforce that the target
    /// exists, not that it belongs to the car whose folder the file is in.
    /// </remarks>
    private async Task<(string Field, string Message)?> ValidateLinkAsync(
        int vehicleId, int? serviceRecordId, int? expenseEntryId, int? issueId, CancellationToken ct)
    {
        var set = new[] { serviceRecordId, expenseEntryId, issueId }.Count(x => x is not null);
        if (set > 1)
        {
            return ("Link",
                "A document attaches to one record — a service record, an expense or an issue — not several.");
        }

        if (serviceRecordId is { } sr
            && !await context.ServiceRecords.AnyAsync(r => r.Id == sr && r.VehicleId == vehicleId, ct))
        {
            return ("ServiceRecordId", $"Service record {sr} is not this vehicle's.");
        }

        if (expenseEntryId is { } ee
            && !await context.ExpenseEntries.AnyAsync(e => e.Id == ee && e.VehicleId == vehicleId, ct))
        {
            return ("ExpenseEntryId", $"Expense {ee} is not this vehicle's.");
        }

        if (issueId is { } ii
            && !await context.Issues.AnyAsync(i => i.Id == ii && i.VehicleId == vehicleId, ct))
        {
            return ("IssueId", $"Issue {ii} is not this vehicle's.");
        }

        return null;
    }

    /// <summary>Names whichever record the document is attached to, for the chip.</summary>
    private async Task<DocumentLink?> DescribeLinkAsync(Document d, CancellationToken ct)
    {
        if (d.ServiceRecordId is { } serviceId)
        {
            var record = await context.ServiceRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == serviceId, ct);
            return record is null
                ? null
                : new DocumentLink(DocumentLinkKind.ServiceRecord, serviceId,
                    $"{record.Type} · {record.ServiceDate:d MMM yyyy}");
        }

        if (d.ExpenseEntryId is { } expenseId)
        {
            var expense = await context.ExpenseEntries.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == expenseId, ct);
            return expense is null
                ? null
                : new DocumentLink(DocumentLinkKind.Expense, expenseId,
                    $"{expense.Category} · £{expense.Amount:N2}");
        }

        if (d.IssueId is { } issueId)
        {
            var issue = await context.Issues.AsNoTracking().FirstOrDefaultAsync(i => i.Id == issueId, ct);
            return issue is null ? null : new DocumentLink(DocumentLinkKind.Issue, issueId, issue.Title);
        }

        return null;
    }

    private static DocumentItem ToItem(Document d, DocumentLink? link) => new(
        d.Id, d.Type, d.Title, d.DocumentDate, d.ContentType, d.SizeBytes, d.Sha256,
        d.ServiceRecordId, d.ExpenseEntryId, d.IssueId, d.Notes, link);
}
