using CarTracker.Domain.Accounts;

namespace CarTracker.Domain.Tests;

/// <summary>
/// The half of the door that changed in 0.24.0: sign-up is open unless a deployment says otherwise.
/// </summary>
/// <remarks>
/// A separate class from <see cref="SignupPolicyTests"/> on purpose. That one is about a list and pins every
/// way a list can fail open; this one is about a mode, and its whole content is that the list is not consulted.
/// Mixing them would put the two readings of a blank <c>Signup:</c> section - closed then, open now - in one
/// file where the next person has to work out which tests mean which.
/// </remarks>
public sealed class SignupModeTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_mode_is_open(string? mode)
    {
        // Two claims in one. The reversal - a blank Signup section used to admit nobody, and anybody arriving
        // here from SignupPolicyTests will expect the old answer. And the shape: the compose file writes every
        // key it knows, so an unset SIGNUP_MODE reaches the binder as "". Bound to the enum directly that
        // throws at startup, which is a deployment that does not boot over a key nobody filled in.
        Assert.Equal(SignupMode.Open, new SignupOptions { Mode = mode }.Resolved);
    }

    [Theory]
    [InlineData("open")]
    [InlineData("INVITEONLY")]
    [InlineData(" InviteOnly ")]
    public void The_mode_is_read_case_insensitively_and_trimmed(string mode)
    {
        // A value typed into a .env file, by hand, at the end of a line.
        Assert.Equal(
            mode.Trim().Equals("open", StringComparison.OrdinalIgnoreCase) ? SignupMode.Open : SignupMode.InviteOnly,
            new SignupOptions { Mode = mode }.Resolved);
    }

    [Fact]
    public void A_misspelt_mode_throws_rather_than_quietly_opening_the_door()
    {
        // The one case where being lenient would be indefensible: somebody who wrote "InvitOnly" meant to shut
        // the door, and defaulting a typo to Open is the single outcome nothing downstream could ever detect.
        var error = Assert.Throws<InvalidOperationException>(() => new SignupOptions { Mode = "InvitOnly" }.Resolved);

        Assert.Contains("InviteOnly", error.Message);
    }

    [Theory]
    [InlineData("stranger@nowhere.test", true)]
    [InlineData("stranger@nowhere.test", false)]
    public void Open_admits_an_address_on_no_list_verified_or_not(string email, bool verified)
    {
        // Verification is not the door's business any more - it decides a *plan*, not admission. An
        // unverified account exists and is on the free tier, which is the fail-safe direction reached without
        // locking anybody out. See AccountEntitlements.
        var policy = new SignupPolicy(new SignupOptions { Mode = "Open" });

        Assert.True(policy.Admits(email, verified));
    }

    [Fact]
    public void Open_admits_a_subject_whose_address_could_not_be_read()
    {
        // The case that makes open sign-up work on a deployment with no Auth0:Management: credential, which is
        // every fresh checkout. Refusing here would mean a stranger can authenticate and then be told, by an
        // app that never asked anybody a question, that it could not read their address.
        var policy = new SignupPolicy(new SignupOptions { Mode = "Open" });

        Assert.True(policy.Admits(null, emailVerified: false));
    }

    [Fact]
    public void Open_is_never_reported_as_closed_however_empty_the_allowlist()
    {
        // IsClosed drives the boot warning. An open deployment with no allowlist is the *normal* shape, and a
        // warning fired at every container start for the normal shape is a warning people learn to skim.
        var policy = new SignupPolicy(new SignupOptions { Mode = "Open" });

        Assert.False(policy.IsClosed);
    }

    [Fact]
    public void Invite_only_with_nothing_listed_is_closed()
    {
        var policy = new SignupPolicy(new SignupOptions { Mode = "InviteOnly" });

        Assert.True(policy.IsClosed);
        Assert.False(policy.Admits("someone@example.com", emailVerified: true));
    }
}
