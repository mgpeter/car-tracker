using System.Globalization;
using CarTracker.Shared;

namespace CarTracker.Domain.Documents;

/// <summary>
/// The link a filing offer hands back: <c>/file?vehicle=...&amp;type=...</c>, read by the web app's <c>/file</c>
/// route.
/// </summary>
/// <remarks>
/// <para>
/// <b>It never builds the vehicle slug.</b> The registration travels as written and the web app matches it
/// against the garage, so the rule for turning "BT53 AKJ" into a URL segment lives in one place (<c>hrefFor</c>).
/// </para>
/// <para>
/// The link authorises nothing. It pre-fills an upload sheet the owner reviews, and the upload endpoint checks
/// the type, the link id and the allowance again.
/// </para>
/// </remarks>
public static class FilingLink
{
    public static string PathFor(
        string registration,
        DocumentType type,
        string title,
        DateOnly? documentDate,
        int? serviceRecordId,
        int? expenseEntryId,
        int? issueId)
    {
        List<(string Key, string Value)> query =
        [
            ("vehicle", registration),
            ("type", type.ToString()),
            ("title", title),
        ];

        if (documentDate is { } date) query.Add(("date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        if (serviceRecordId is { } service) query.Add(("serviceRecordId", service.ToString(CultureInfo.InvariantCulture)));
        if (expenseEntryId is { } expense) query.Add(("expenseEntryId", expense.ToString(CultureInfo.InvariantCulture)));
        if (issueId is { } issue) query.Add(("issueId", issue.ToString(CultureInfo.InvariantCulture)));

        return "/file?" + string.Join('&', query.Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}"));
    }

    /// <summary>The path made absolute, or null when the deployment has no public origin configured.</summary>
    /// <param name="origin">Normally <see cref="AppOptions.Origin"/>, already trimmed; trimmed again here regardless.</param>
    public static string? UrlFor(string? origin, string path) =>
        origin?.Trim().TrimEnd('/') is { Length: > 0 } trimmed ? trimmed + path : null;
}
