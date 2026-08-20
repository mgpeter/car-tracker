using CarTracker.Domain.Accounts.Import;

namespace CarTracker.Domain.Tests;

/// <summary>
/// What an imported car is called when its plate is already in the garage.
/// </summary>
/// <remarks>
/// The rule is small and it is the sharpest edge in the import spec, because the plate it produces is
/// fictional: <c>GET /api/vehicles/lookup/{reg}</c> will not resolve <c>BT53 AKJ-2</c>, and an assistant asked
/// about "BT53 AKJ" now has two cars to choose between. The proposal is editable in the preview and the
/// vehicle's notes record what it was cloned from, which is what makes that cost payable rather than hidden.
/// </remarks>
public class RegistrationProposerTests
{
    private static HashSet<string> Taken(params string[] registrations) =>
        registrations.Select(RegistrationProposer.Normalise).ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void A_free_registration_is_proposed_unchanged()
    {
        Assert.Equal("BT53 AKJ", RegistrationProposer.Propose("BT53 AKJ", Taken("KV02 XYZ")));
    }

    [Fact]
    public void A_taken_registration_becomes_the_second_of_it()
    {
        Assert.Equal("BT53 AKJ-2", RegistrationProposer.Propose("BT53 AKJ", Taken("BT53 AKJ")));
    }

    [Fact]
    public void It_keeps_counting_past_the_second()
    {
        Assert.Equal("BT53 AKJ-4", RegistrationProposer.Propose("BT53 AKJ", Taken("BT53 AKJ", "BT53 AKJ-2", "BT53 AKJ-3")));
    }

    /// <summary>
    /// The index is on <c>upper(replace(registration, ' ', ''))</c>, so spacing and case are not a difference
    /// the database recognises - and a proposer that thought otherwise would hand back a plate the insert then
    /// refuses.
    /// </summary>
    [Theory]
    [InlineData("bt53 akj")]
    [InlineData("BT53AKJ")]
    [InlineData(" BT53  AKJ")]
    public void Spacing_and_case_are_not_a_free_registration(string owned)
    {
        var proposed = RegistrationProposer.Propose("BT53 AKJ", Taken(owned));

        Assert.NotEqual("BT53 AKJ", proposed);
    }

    /// <summary>
    /// <c>Registration</c> is <c>varchar(16)</c> and so is the computed column beside it, so a long plate with
    /// a suffix appended is not a long registration - it is a failed insert.
    /// </summary>
    [Fact]
    public void A_plate_that_fills_the_column_is_truncated_to_make_room_for_the_suffix()
    {
        var full = new string('A', RegistrationProposer.MaxLength);

        var proposed = RegistrationProposer.Propose(full, Taken(full));

        Assert.Equal(RegistrationProposer.MaxLength, proposed.Length);
        Assert.EndsWith("-2", proposed);
    }

    [Fact]
    public void A_truncated_proposal_that_is_itself_taken_keeps_counting()
    {
        var full = new string('A', RegistrationProposer.MaxLength);
        var second = full[..(RegistrationProposer.MaxLength - 2)] + "-2";

        var proposed = RegistrationProposer.Propose(full, Taken(full, second));

        Assert.Equal(full[..(RegistrationProposer.MaxLength - 2)] + "-3", proposed);
    }

    /// <summary>Nothing is ever proposed longer than the column, whatever the suffix grows to.</summary>
    [Fact]
    public void No_proposal_ever_exceeds_the_column()
    {
        var full = new string('A', RegistrationProposer.MaxLength);
        var taken = Taken(full);

        for (var n = 2; n <= 200; n++)
        {
            var proposed = RegistrationProposer.Propose(full, taken);
            Assert.True(proposed.Length <= RegistrationProposer.MaxLength, proposed);
            taken.Add(RegistrationProposer.Normalise(proposed));
        }
    }

    [Fact]
    public void Surrounding_whitespace_is_not_part_of_a_registration()
    {
        Assert.Equal("BT53 AKJ", RegistrationProposer.Propose("  BT53 AKJ  ", Taken()));
    }
}
