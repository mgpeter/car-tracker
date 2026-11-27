using System.Web;
using CarTracker.Domain;
using CarTracker.Domain.Documents;
using CarTracker.Shared;

namespace CarTracker.Domain.Tests;

/// <summary>
/// The filing link: a path the web app's <c>/file</c> route reads back, and an absolute URL when this deployment
/// knows its public origin.
/// </summary>
/// <remarks>
/// The server never builds the vehicle slug (the web app owns that rule in <c>hrefFor</c>), so the registration
/// travels as written and the client normalises it.
/// </remarks>
public sealed class FilingLinkTests
{
    /// <summary>
    /// A server-built path kept as a literal, so the web app's <c>FileLinkPage.test.tsx</c> can parse the same
    /// string and a change on either side fails a test.
    /// </summary>
    public const string SamplePath =
        "/file?vehicle=BT53%20AKJ&type=PartsReceipt&title=Coolant%20%26%20thermostat%20%23412%20%2B%20VAT%20%C2%A363.20%20%25off&date=2026-11-14&expenseEntryId=412";

    private static System.Collections.Specialized.NameValueCollection Query(string path)
    {
        Assert.StartsWith("/file?", path);
        return HttpUtility.ParseQueryString(path["/file?".Length..]);
    }

    [Fact]
    public void The_path_carries_every_field_in_order()
    {
        var path = FilingLink.PathFor(
            "BT53 AKJ", DocumentType.PartsReceipt, "Coolant & thermostat #412 + VAT £63.20 %off",
            new DateOnly(2026, 11, 14), serviceRecordId: null, expenseEntryId: 412, issueId: null);

        Assert.Equal(SamplePath, path);
    }

    [Fact]
    public void The_registration_is_escaped_not_slugged()
    {
        var path = FilingLink.PathFor("BT53 AKJ", DocumentType.CarPhoto, "x", null, null, null, null);

        Assert.Contains("vehicle=BT53%20AKJ", path);
        Assert.Equal("BT53 AKJ", Query(path)["vehicle"]);
    }

    [Theory]
    [InlineData("Euro Car Parts & co")]
    [InlineData("Invoice #412")]
    [InlineData("Coolant + thermostat")]
    [InlineData("100% done")]
    [InlineData("£63.20 receipt")]
    [InlineData("  spaces   inside  ")]
    [InlineData("a=b&c=d?e/f")]
    public void A_title_round_trips_through_a_query_parser(string title)
    {
        var path = FilingLink.PathFor("BT53 AKJ", DocumentType.Receipt, title, null, null, null, null);

        Assert.Equal(title, Query(path)["title"]);
    }

    [Fact]
    public void An_absent_date_and_link_leave_their_keys_out()
    {
        var query = Query(FilingLink.PathFor("BT53 AKJ", DocumentType.CarPhoto, "On the drive", null, null, null, null));

        Assert.Equal(["vehicle", "type", "title"], query.AllKeys.Select(k => k ?? string.Empty).ToArray());
    }

    [Theory]
    [InlineData(5, null, null, "serviceRecordId", "5")]
    [InlineData(null, 6, null, "expenseEntryId", "6")]
    [InlineData(null, null, 7, "issueId", "7")]
    public void The_link_travels_under_its_own_key(int? service, int? expense, int? issue, string key, string value)
    {
        var query = Query(FilingLink.PathFor("BT53 AKJ", DocumentType.ConditionPhoto, "t", null, service, expense, issue));

        Assert.Equal(value, query[key]);
    }

    [Fact]
    public void The_date_is_an_iso_calendar_day()
    {
        var query = Query(FilingLink.PathFor("BT53 AKJ", DocumentType.MOT, "MOT", new DateOnly(2026, 7, 8), null, null, null));

        Assert.Equal("2026-07-08", query["date"]);
        Assert.Equal("MOT", query["type"]);
    }

    [Fact]
    public void The_url_is_the_origin_and_the_path_when_the_origin_is_set()
    {
        Assert.Equal("https://cambelt.app/file?vehicle=X", FilingLink.UrlFor("https://cambelt.app", "/file?vehicle=X"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void The_url_is_null_when_the_origin_is_blank(string? origin)
    {
        Assert.Null(FilingLink.UrlFor(origin, "/file?vehicle=X"));
    }

    [Theory]
    [InlineData("https://cambelt.app", "https://cambelt.app")]
    [InlineData("https://cambelt.app/", "https://cambelt.app")]
    [InlineData("https://cambelt.app///", "https://cambelt.app")]
    [InlineData("  https://cambelt.app/  ", "https://cambelt.app")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("/", null)]
    [InlineData(null, null)]
    public void AppOptions_trims_trailing_slashes_and_reads_blank_as_none(string? configured, string? expected)
    {
        Assert.Equal(expected, new AppOptions { PublicOrigin = configured }.Origin);
    }

    [Fact]
    public void The_url_uses_the_normalised_origin()
    {
        var origin = new AppOptions { PublicOrigin = "https://cambelt.app/" }.Origin;

        Assert.Equal("https://cambelt.app/file?vehicle=X", FilingLink.UrlFor(origin, "/file?vehicle=X"));
    }
}
