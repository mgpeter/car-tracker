using CarTracker.Domain.Admin;

namespace CarTracker.Domain.Tests;

/// <summary>
/// What an operator sees instead of somebody else's number plate.
/// </summary>
public sealed class RegistrationMaskTests
{
    [Theory]
    [InlineData("BT53 AKJ", "BT** **J")]
    [InlineData("BT53AKJ", "BT****J")]
    [InlineData("A1 AAA", "A1 **A")]
    public void The_first_two_and_the_last_survive_and_the_shape_is_kept(string registration, string expected)
    {
        Assert.Equal(expected, RegistrationMask.Mask(registration));
    }

    [Fact]
    public void An_import_suffix_stays_readable()
    {
        // The one detail worth keeping: a re-imported car is BT53 AKJ-2, and two rows differing only by the
        // suffix would otherwise mask to the same string and read as duplicates of each other.
        Assert.Equal("BT** ***-2", RegistrationMask.Mask("BT53 AKJ-2"));
    }

    [Theory]
    [InlineData("A", "*")]
    [InlineData("AB", "**")]
    [InlineData("AB1", "***")]
    public void Anything_shorter_than_four_characters_is_hidden_entirely(string registration, string expected)
    {
        // Keeping "the first two and the last" of three characters is keeping all of them.
        Assert.Equal(expected, RegistrationMask.Mask(registration));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_masks_to_nothing(string? registration)
    {
        Assert.Equal(string.Empty, RegistrationMask.Mask(registration));
    }

    [Fact]
    public void Surrounding_whitespace_is_not_treated_as_part_of_the_plate()
    {
        // Otherwise a padded value spends its two visible characters on spaces and leaks the real first two.
        Assert.Equal("BT** **J", RegistrationMask.Mask("  BT53 AKJ  "));
    }

    [Theory]
    [InlineData("BT53 AKJ")]
    [InlineData("BT53AKJ")]
    [InlineData("AB-C")]
    [InlineData("A- -B")]
    [InlineData("AB53")]
    public void A_value_of_four_characters_or_more_never_comes_back_unchanged(string registration)
    {
        // The load-bearing negative. The last two cases are why the implementation counts what it hid rather
        // than trusting the loop: a value whose entire middle is separators would survive byte-for-byte, and
        // the one thing this function exists to prevent would quietly not have happened.
        Assert.NotEqual(registration, RegistrationMask.Mask(registration));
    }

    [Fact]
    public void The_masked_value_is_the_same_length_as_what_it_hides()
    {
        // Length is not a secret, and keeping it is what makes a column of masked plates line up and read as
        // plates rather than as redaction.
        Assert.Equal("BT53 AKJ".Length, RegistrationMask.Mask("BT53 AKJ").Length);
        Assert.Equal("BT53 AKJ-2".Length, RegistrationMask.Mask("BT53 AKJ-2").Length);
    }
}
