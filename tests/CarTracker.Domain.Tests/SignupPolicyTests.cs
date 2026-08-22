using CarTracker.Domain.Accounts;

namespace CarTracker.Domain.Tests;

/// <summary>
/// The invitation door, tested where it is a pure decision.
/// </summary>
/// <remarks>
/// <para>
/// The interesting cases are all failures of the closed default, and none of them look like bugs in a config
/// file: a trailing comma, a key set to nothing, a domain written as a bare "@". Each would turn "nobody is
/// admitted" into "everybody is", silently, on the deployment least likely to be watching — so each is a test
/// rather than a comment.
/// </para>
/// <para>
/// <b>Every test below names <see cref="SignupMode.InviteOnly"/> explicitly, and that is load-bearing.</b> The
/// shipped default became <see cref="SignupMode.Open"/> in 0.24.0, which admits everybody - so a helper that
/// left the mode alone would make this whole class assert nothing while staying green. The mode is the first
/// thing <c>Admits</c> reads.
/// </para>
/// </remarks>
public sealed class SignupPolicyTests
{
    private static SignupPolicy Policy(string? emails = null, string? domains = null) =>
        new(new SignupOptions
        {
            Mode = "InviteOnly",
            AllowedEmails = emails,
            AllowedDomains = domains,
        });

    /// <summary>
    /// The list half, asked about an address the tenant <i>has</i> verified — so that a test about the list is
    /// about the list. The verification half has its own tests below.
    /// </summary>
    private static bool AdmitsVerified(SignupPolicy policy, string? email) => policy.Admits(email, emailVerified: true);

    [Fact]
    public void An_unset_allowlist_admits_nobody()
    {
        // The whole spec turns on this reading: absent means closed, not open.
        Assert.False(AdmitsVerified(Policy(), "someone@example.com"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    [InlineData(" , , ")]
    public void A_blank_or_punctuation_only_allowlist_admits_nobody(string value)
    {
        // The env-var shapes that produce an entry of "" — the fail-open failure this class is built to refuse.
        Assert.False(AdmitsVerified(Policy(emails: value, domains: value), "someone@example.com"));
    }

    [Fact]
    public void A_bare_at_sign_is_not_a_domain_that_matches_everything()
    {
        Assert.False(AdmitsVerified(Policy(domains: "@"), "stranger@anywhere.test"));
    }

    [Fact]
    public void A_listed_address_is_admitted_whatever_its_casing_or_padding()
    {
        var policy = Policy(emails: " Owner@Example.com , second@example.com ");

        Assert.True(AdmitsVerified(policy, "owner@example.com"));
        Assert.True(AdmitsVerified(policy, "  OWNER@EXAMPLE.COM  "));
        Assert.True(AdmitsVerified(policy, "second@example.com"));
    }

    [Fact]
    public void An_unlisted_address_at_a_listed_persons_domain_is_refused()
    {
        // Emails and domains are separate lists on purpose: listing one person does not open their employer.
        Assert.False(AdmitsVerified(Policy(emails: "owner@example.com"), "stranger@example.com"));
    }

    [Fact]
    public void A_listed_domain_admits_every_address_at_it_and_no_other()
    {
        var policy = Policy(domains: "example.com, @usualexpat.com");

        Assert.True(AdmitsVerified(policy, "anyone@example.com"));
        Assert.True(AdmitsVerified(policy, "anyone@usualexpat.com"));       // written with the '@' — same instruction
        Assert.False(AdmitsVerified(policy, "anyone@example.com.evil.test")); // suffix, not the domain
        Assert.False(AdmitsVerified(policy, "anyone@notexample.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("trailing@")]
    public void An_address_that_cannot_be_read_is_refused_rather_than_admitted(string? email)
    {
        // The token carries no email claim on this tenant, so the address comes from the Management API and is
        // null whenever that is unconfigured or unreachable. Unknown must never mean welcome.
        Assert.False(AdmitsVerified(Policy(emails: "owner@example.com", domains: "example.com"), email));
    }

    [Fact]
    public void An_unverified_address_is_refused_however_perfectly_it_matches_the_list()
    {
        // The defect this test was written for: with the address alone deciding, a domain allowlist admits
        // anybody willing to type `anything@example.com` into a self-service sign-up form, and the deployment
        // reads as invitation-only while being open to the internet. An unverified address is a claim.
        var policy = Policy(emails: "owner@example.com", domains: "example.com");

        Assert.False(policy.Admits("owner@example.com", emailVerified: false));
        Assert.False(policy.Admits("impostor@example.com", emailVerified: false));

        // And the same two addresses, once the tenant has confirmed them.
        Assert.True(policy.Admits("owner@example.com", emailVerified: true));
        Assert.True(policy.Admits("impostor@example.com", emailVerified: true));
    }

    [Fact]
    public void The_problem_type_is_the_one_the_client_matches_on()
    {
        // Guards the guard: the constant is a wire contract with AuthGate, not an internal name.
        Assert.Equal("signup-not-invited", SignupPolicy.NotInvitedProblemType);
    }
}
