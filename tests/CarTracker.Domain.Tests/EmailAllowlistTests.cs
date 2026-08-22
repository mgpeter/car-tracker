using CarTracker.Domain.Accounts;

namespace CarTracker.Domain.Tests;

/// <summary>
/// The parsing both allowlists share, tested once where it lives.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <see cref="SignupPolicy"/> when the comp list appeared, and this class is the reason the
/// extraction was worth doing rather than copying twelve lines: <b>every case here is a way a list fails
/// open</b>, and a second copy is a second chance to get one of them wrong. Since 0.24.0 an entry that matched
/// everything would hand out the paid tier rather than an account, so the stakes went up as the door came down.
/// </para>
/// <para>
/// <see cref="SignupPolicyTests"/> still covers the same shapes through the door, deliberately. These are the
/// unit tests; those are the ones that prove the door actually asks.
/// </para>
/// </remarks>
public sealed class EmailAllowlistTests
{
    [Fact]
    public void An_unset_list_matches_nobody()
    {
        Assert.False(new EmailAllowlist(null, null).Contains("someone@example.com"));
        Assert.True(EmailAllowlist.Empty.IsEmpty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    [InlineData(" , , ")]
    [InlineData("\t\n")]
    public void A_blank_or_punctuation_only_list_matches_nobody(string value)
    {
        // The env-var shapes that produce an entry of "". A list holding one empty string is non-empty, and a
        // domain comparison against "" answers yes for every address alive.
        var list = new EmailAllowlist(value, value);

        Assert.True(list.IsEmpty);
        Assert.False(list.Contains("someone@example.com"));
    }

    [Theory]
    [InlineData("@")]
    [InlineData("@,@")]
    [InlineData(" @ ")]
    public void A_bare_at_sign_is_not_a_domain_that_matches_everything(string domains)
    {
        // '@' is stripped so "@example.com" and "example.com" are the same instruction; an entry of nothing but
        // '@' would survive that as "", which is the fail-open case this type exists to refuse.
        var list = new EmailAllowlist(null, domains);

        Assert.Equal(0, list.DomainCount);
        Assert.False(list.Contains("anyone@anywhere.test"));
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("@example.com")]
    [InlineData(" example.com , other.test ")]
    public void A_domain_matches_every_address_at_it_however_it_is_written(string domains)
    {
        Assert.True(new EmailAllowlist(null, domains).Contains("someone@example.com"));
    }

    [Fact]
    public void Matching_ignores_case_on_both_halves()
    {
        Assert.True(new EmailAllowlist("Someone@Example.COM", null).Contains("someone@example.com"));
        Assert.True(new EmailAllowlist(null, "EXAMPLE.com").Contains("SOMEONE@example.COM"));
    }

    [Fact]
    public void The_domain_is_what_follows_the_last_at_sign()
    {
        // A local part may legally contain '@' inside quotes. Splitting on the first would read the domain as
        // part of the name and match a list entry nobody wrote.
        Assert.True(new EmailAllowlist(null, "example.com").Contains("\"odd@name\"@example.com"));
        Assert.False(new EmailAllowlist(null, "example.com").Contains("\"example.com@x\"@elsewhere.test"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("trailing@")]
    public void An_absent_or_malformed_address_matches_nothing(string? email)
    {
        // Including against a list that does contain entries, which is the case worth pinning: "we could not
        // read the address" must never be answered with "then let them in".
        var list = new EmailAllowlist("someone@example.com", "example.com");

        Assert.False(list.Contains(email));
    }

    [Fact]
    public void Counts_report_what_the_list_actually_matches_against()
    {
        // Read from the parsed arrays, not re-split from the raw strings. The boot posture line uses these, and
        // an operator reading "1 entry" about a list of one stray comma is being told the opposite of the truth.
        var list = new EmailAllowlist("someone@example.com, ,", "@example.com,@,");

        Assert.Equal(1, list.EmailCount);
        Assert.Equal(1, list.DomainCount);
    }
}
