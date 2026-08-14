using System.Text.Json;

namespace CarTracker.Chat.Tests;

/// <summary>
/// What the owner edited on the draft card, checked against the tool's own schema before anything runs.
/// </summary>
/// <remarks>
/// The check is narrow on purpose — an unknown field and a required one left empty. Both can be reported against
/// the field that caused them, which is what earns them a place here. A domain refusal ("that mileage is below
/// the current reading") is not a schema problem and is not re-implemented here: it comes back through the loop
/// as the tool's own sentence. A second copy of the domain's rules would be the copy that drifted.
/// </remarks>
public sealed class ChatArgumentsTests
{
    private static IDictionary<string, object?>? Read(string json) =>
        ChatArguments.Read(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void A_field_the_tool_does_not_have_is_named_in_the_errors()
    {
        var errors = ChatArguments.Check("add_task", Read("""{"title":"Replace pads","colour":"green"}"""), TestCatalogue.Services);

        Assert.True(errors.ContainsKey("colour"));
        Assert.Contains("add_task", errors["colour"][0]);
    }

    [Fact]
    public void A_required_field_cleared_to_nothing_is_reported_against_that_field()
    {
        // The realistic edit: the owner deletes a value the model misread and confirms without replacing it.
        // Reported in the same RFC 9457 shape every add sheet already marks its fields from.
        var errors = ChatArguments.Check("add_task", Read("""{"title":"   "}"""), TestCatalogue.Services);

        Assert.True(errors.ContainsKey("title"));
    }

    [Fact]
    public void Values_the_tool_accepts_pass()
    {
        Assert.Empty(ChatArguments.Check("add_task", Read("""{"title":"Replace front pads"}"""), TestCatalogue.Services));
    }

    [Fact]
    public void No_arguments_at_all_means_exactly_what_was_proposed()
    {
        // Confirming without editing sends nothing, and the loop runs the model's own call unchanged. There is
        // nothing to check, and checking anyway would refuse a draft the tool itself produced.
        Assert.Null(ChatArguments.Read(null));
        Assert.Empty(ChatArguments.Check("add_task", null, TestCatalogue.Services));
    }
}
