using System.ComponentModel;
using CarTracker.Domain;
using CarTracker.Domain.Documents;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using ModelContextProtocol.Server;

namespace CarTracker.ModelContextProtocol.Tools;

/// <summary>
/// Offering to file what the owner shared with the assistant: a photo of the car, a receipt, a certificate.
/// </summary>
/// <remarks>
/// <para>
/// <b>A read, and it stays one.</b> There is no <c>[Authorize(Policy = "McpWrite")]</c> here and the tool is not
/// in <see cref="McpToolClassification.WriteToolNames"/>: it saves nothing, so it needs neither the in-app
/// confirmation nor an audit row, and a read-scope token may call it. The write is the owner's own upload,
/// from the chat's filing card or the upload sheet a filing link opens.
/// </para>
/// <para>
/// An MCP client's model sees pixels and has no file to hand over, and MCP has no upload mechanism, so the tool
/// returns a link the owner opens to file the same photo from their camera roll.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class FilingTools
{
    [McpServerTool(Name = "offer_to_file")]
    [Description(
        "Offer to file a photo or document the user has shared, in Cambelt. Call this once for every photo or "
        + "document the user shares, even when nothing else is asked of it. It cannot upload anything and saves "
        + "nothing: it checks the proposal and returns a link the user opens to file the same file themselves. "
        + "Pick the category: a photo of the car that is not a paper and not evidence for an issue is CarPhoto; "
        + "damage or wear for a known issue is ConditionPhoto linked to that issue (issueId); a receipt, invoice or "
        + "certificate takes its paper category. If the file led to a write (an expense, a service record, an "
        + "issue), call this only after that write has succeeded and link the new record by its id; if the write "
        + "was declined, offer without a link. Never tell the user the file has been filed. Example: type "
        + "PartsReceipt, title \"Euro Car Parts - coolant and thermostat\", documentDate 2026-11-14, "
        + "expenseEntryId 412.")]
    public static async Task<McpResult<FilingOffer>> OfferToFile(
        VehicleResolver resolver,
        DocumentService documents,
        AppOptions app,
        [Description("The category, from the grouped list (MOT, Insurance, PartsReceipt, CarPhoto, ConditionPhoto...).")]
        DocumentType type,
        [Description("A short title for the document, at most 160 characters.")] string title,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("The date on the document or when the photo was taken (yyyy-MM-dd).")] DateOnly? documentDate = null,
        [Description("Attach to this service record. At most one of the three link ids.")] int? serviceRecordId = null,
        [Description("Attach to this expense. At most one of the three link ids.")] int? expenseEntryId = null,
        [Description("Attach to this issue. At most one of the three link ids.")] int? issueId = null,
        [Description(
            "In-app chat only: the 1-based position of the file in the user's latest message with attachments. "
            + "Omit elsewhere.")]
        int? attachment = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var result = await documents.ProposeFilingAsync(
            v, type, title, documentDate, serviceRecordId, expenseEntryId, issueId, attachment, app.Origin,
            cancellationToken);

        return ToolHelpers.ToResult(result, result.Value is { } offer ? Summary(offer) : string.Empty);
    }

    /// <summary>"Ready to file as Parts receipt, linked to Parts · £63.20. Open: https://..."</summary>
    private static string Summary(FilingOffer offer)
    {
        var linked = offer.LinkedTo is { } link ? $", linked to {link.Label}" : string.Empty;
        var open = offer.Url is { } url ? $"Open: {url}" : $"Open Cambelt and go to {offer.Path}";
        var full = offer.Allowance.Used >= offer.Allowance.Limit
            ? " " + DocumentService.FullMessage(new DocumentService.DocumentCapacity(offer.Allowance.Used, offer.Allowance.Limit))
            : string.Empty;

        return $"Ready to file as {offer.Label}{linked}. {open}{full}";
    }
}
