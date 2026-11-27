using System.Text.Json;
using CarTracker.ModelContextProtocol;
using CarTracker.Shared;
using CarTracker.Shared.Logs;

namespace CarTracker.Chat.Tests;

/// <summary>
/// The <c>filing_offer</c> frame: one compact line, camelCase, enums as their names.
/// </summary>
/// <remarks>
/// An SSE frame is line-oriented, so indented JSON splits into lines the client cannot parse (the trap the
/// <c>done</c> frame fell into once). And the web client types <c>type</c>, <c>kind</c> and <c>group</c> from the
/// generated contract's string unions, so a number on the wire would match nothing.
/// </remarks>
public sealed class ChatStreamJsonTests
{
    private static readonly FilingOffer Offer = new(
        "BT53 AKJ", DocumentType.PartsReceipt, DocumentKind.Paper, DocumentGroup.WorkAndParts, "Parts receipt",
        "Euro Car Parts - coolant", new DateOnly(2026, 11, 14),
        new DocumentLink(DocumentLinkKind.Expense, 412, "Parts · £63.20"), 1, new FilingAllowance(37, 2000),
        "/file?vehicle=BT53%20AKJ&type=PartsReceipt", null);

    [Fact]
    public void An_offer_serialises_to_one_line()
    {
        var json = JsonSerializer.Serialize(Offer, ChatStreamJson.Options);

        Assert.DoesNotContain('\n', json);
    }

    [Fact]
    public void An_offer_is_camel_case_with_enums_as_names()
    {
        using var parsed = JsonDocument.Parse(JsonSerializer.Serialize(Offer, ChatStreamJson.Options));
        var root = parsed.RootElement;

        Assert.Equal("PartsReceipt", root.GetProperty("type").GetString());
        Assert.Equal("Paper", root.GetProperty("kind").GetString());
        Assert.Equal("WorkAndParts", root.GetProperty("group").GetString());
        Assert.Equal("Expense", root.GetProperty("linkedTo").GetProperty("kind").GetString());
        Assert.Equal(412, root.GetProperty("linkedTo").GetProperty("id").GetInt32());
        Assert.Equal("2026-11-14", root.GetProperty("documentDate").GetString());
        Assert.Equal(1, root.GetProperty("attachment").GetInt32());
        Assert.Equal(2000, root.GetProperty("allowance").GetProperty("limit").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("url").ValueKind);
        Assert.Equal("BT53 AKJ", root.GetProperty("registration").GetString());
    }

    [Fact]
    public void The_offer_is_read_from_a_typed_result()
    {
        Assert.Equal(Offer, ChatConversationService.FilingOfferIn(new McpResult<FilingOffer>("Ready.", Offer)));
    }

    [Fact]
    public void The_offer_is_read_from_a_serialised_result_in_either_casing()
    {
        var camel = JsonSerializer.SerializeToElement(new McpResult<FilingOffer>("Ready.", Offer), ChatStreamJson.Options);
        var pascal = JsonSerializer.SerializeToElement(new McpResult<FilingOffer>("Ready.", Offer));

        Assert.Equal(Offer, ChatConversationService.FilingOfferIn(camel));
        Assert.Equal(Offer, ChatConversationService.FilingOfferIn(pascal));
    }

    [Theory]
    [InlineData("Rejected - Issue 4 is not this vehicle's.")]
    [InlineData(null)]
    public void A_refusal_or_nothing_is_no_offer(string? result)
    {
        Assert.Null(ChatConversationService.FilingOfferIn(result));
    }

    [Fact]
    public void A_result_without_an_offer_is_no_offer()
    {
        Assert.Null(ChatConversationService.FilingOfferIn(JsonSerializer.SerializeToElement(new { summary = "x" })));
        Assert.Null(ChatConversationService.FilingOfferIn(JsonSerializer.SerializeToElement(new { data = new { type = "Nope" } })));
    }
}
