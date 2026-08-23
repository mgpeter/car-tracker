using CarTracker.Domain.Legal;

namespace CarTracker.Domain.Tests;

/// <summary>
/// Whether this deployment publishes legal documents, and who they say is responsible.
/// </summary>
/// <remarks>
/// <b>The polarity is the reverse of its neighbour in the same compose file</b> (DEC-024). A blank
/// <c>Signup:</c> section means the door is <i>open</i>; a blank <c>Legal:</c> section means nothing is
/// <i>published</i>. That is deliberate rather than an inconsistency: for a legal document the fail-safe
/// direction is not arguable, because a page naming the wrong controller makes a false statement about who is
/// accountable to whom, and this repository is deployed by people who are not its author.
/// </remarks>
public sealed class LegalOptionsTests
{
    private static LegalOptions With(string? name, string? contact) =>
        new() { ControllerName = name, ControllerContact = contact };

    [Fact]
    public void Both_a_name_and_a_contact_publish()
    {
        Assert.True(With("Usual Expat Ltd", "privacy@example.test").IsPublished);
    }

    [Theory]
    [InlineData(null, "privacy@example.test")]
    [InlineData("", "privacy@example.test")]
    [InlineData("   ", "privacy@example.test")]
    [InlineData("Usual Expat Ltd", null)]
    [InlineData("Usual Expat Ltd", "")]
    [InlineData("Usual Expat Ltd", "   ")]
    [InlineData(null, null)]
    public void Either_one_missing_publishes_nothing(string? name, string? contact)
    {
        // A document naming a controller nobody can write to is not a document, and one with a contact and no
        // controller names nobody. Both halves are required, and whitespace is not a value - a .env file makes
        // trailing spaces easy and invisible.
        Assert.False(With(name, contact).IsPublished);
    }

    [Fact]
    public void The_jurisdiction_defaults_rather_than_blocking_publication()
    {
        // Unlike the two above, this one has a sane default, so forgetting it does not take the pages down. The
        // deployment this repository is written for is a UK one; anywhere else sets the key.
        var options = With("Usual Expat Ltd", "privacy@example.test");

        Assert.True(options.IsPublished);
        Assert.Equal("United Kingdom", options.ResolvedJurisdiction);
    }

    [Theory]
    [InlineData("Ireland", "Ireland")]
    [InlineData("  Ireland  ", "Ireland")]
    [InlineData("", "United Kingdom")]
    [InlineData("   ", "United Kingdom")]
    [InlineData(null, "United Kingdom")]
    public void The_jurisdiction_is_trimmed_and_falls_back_when_blank(string? configured, string expected)
    {
        var options = With("Usual Expat Ltd", "privacy@example.test");
        options.Jurisdiction = configured;

        Assert.Equal(expected, options.ResolvedJurisdiction);
    }

    [Fact]
    public void The_address_and_the_hosting_summary_are_independently_optional()
    {
        // Neither blocks publication and neither implies the other. A self-hoster may have no postal address
        // worth publishing and no idea what to say about hosting, and should still be able to name themselves.
        var options = With("A Person", "them@example.test");

        Assert.True(options.IsPublished);
        Assert.Null(options.ControllerAddress);
        Assert.Null(options.HostingSummary);
    }

    [Theory]
    [InlineData("  Usual Expat Ltd  ", "Usual Expat Ltd")]
    [InlineData("Usual Expat Ltd", "Usual Expat Ltd")]
    public void The_published_values_are_trimmed(string configured, string expected)
    {
        // What reaches the page is what the page renders, so trimming happens here rather than in a component
        // - otherwise every future consumer has to remember to do it.
        var options = new LegalOptions { ControllerName = configured, ControllerContact = " them@example.test " };

        Assert.Equal(expected, options.Publication!.ControllerName);
        Assert.Equal("them@example.test", options.Publication!.ControllerContact);
    }

    [Fact]
    public void An_unpublished_deployment_offers_no_publication_at_all()
    {
        // Null rather than a record full of empty strings. The endpoint sends this straight out as a nullable
        // block, and the client tests it for null - so "not published" has exactly one representation and a
        // page cannot render a heading above four blanks.
        Assert.Null(new LegalOptions().Publication);
    }

    [Fact]
    public void A_blank_optional_value_is_carried_as_null_rather_than_as_an_empty_string()
    {
        // The client renders the address and the hosting summary conditionally. An empty string is truthy
        // enough in enough places that it would eventually paint an empty paragraph under a heading.
        var options = new LegalOptions
        {
            ControllerName = "A Person",
            ControllerContact = "them@example.test",
            ControllerAddress = "   ",
            HostingSummary = "",
        };

        Assert.Null(options.Publication!.ControllerAddress);
        Assert.Null(options.Publication!.HostingSummary);
    }
}
