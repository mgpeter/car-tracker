using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CarTracker.Chat.Tests;

/// <summary>
/// The exact JSON a user message has to arrive as.
/// </summary>
/// <remarks>
/// The browser builds the user turn by hand — it has no <c>ChatMessage</c> type — and the server deserialises
/// it with the library's own converters. So the wire shape is a real contract that the OpenAPI document cannot
/// express (it declares an opaque element, because a transcript carries reasoning signatures that no
/// hand-written DTO would round-trip). This is where that contract is written down, and a library upgrade that
/// renames a property fails here rather than in a panel that stops answering.
/// </remarks>
public sealed class TranscriptShapeTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public void A_user_message_round_trips_through_the_shape_the_client_sends()
    {
        var json = JsonSerializer.Serialize<List<ChatMessage>>(
            [new(ChatRole.User, "What needs my attention?")],
            AIJsonUtilities.DefaultOptions);

        output.WriteLine(json);

        var back = JsonSerializer.Deserialize<List<ChatMessage>>(json, AIJsonUtilities.DefaultOptions);

        Assert.Equal("What needs my attention?", Assert.Single(back!).Text);
    }

    [Fact]
    public void The_client_can_build_that_shape_from_nothing()
    {
        // Byte-for-byte what src/api/chat.ts assembles. If this stops deserialising, the panel stops working
        // and nothing else in the suite would notice.
        const string handWritten = """
            [{"role":"user","contents":[{"$type":"text","text":"What needs my attention?"}]}]
            """;

        var messages = JsonSerializer.Deserialize<List<ChatMessage>>(handWritten, AIJsonUtilities.DefaultOptions);

        var message = Assert.Single(messages!);
        Assert.Equal(ChatRole.User, message.Role);
        Assert.Equal("What needs my attention?", message.Text);
    }

    [Fact]
    public void A_suspended_write_survives_the_round_trip_the_client_makes()
    {
        // The confirm path replays the turn that proposed the write, so the approval request has to serialise
        // out to the browser and back. It is the one content type the client is guaranteed to echo and never
        // constructs itself, which makes it the one nothing else would catch.
        var call = new FunctionCallContent("call-1", "add_vehicle", new Dictionary<string, object?> { ["registration"] = "BT53 AKJ" });

        var json = JsonSerializer.Serialize<List<ChatMessage>>(
            [new(ChatRole.Assistant, [new ToolApprovalRequestContent("call-1", call)])],
            AIJsonUtilities.DefaultOptions);

        output.WriteLine(json);

        var back = JsonSerializer.Deserialize<List<ChatMessage>>(json, AIJsonUtilities.DefaultOptions);

        var request = Assert.Single(Assert.Single(back!).Contents.OfType<ToolApprovalRequestContent>());
        Assert.Equal("call-1", ((FunctionCallContent)request.ToolCall).CallId);
    }
}
