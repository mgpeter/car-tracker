using CarTracker.Shared;
using CarTracker.Domain.Lookup;

namespace CarTracker.Domain.Tests;

/// <summary>
/// Turning DVLA wording into the app's own vocabulary.
/// </summary>
/// <remarks>
/// The mapping is the load-bearing half of the lookup: everything else is an HTTP call. A wrong fuel type
/// silently wrongs every MPG figure derived from it, and a mis-cased colour is the kind of thing that makes a
/// looked-up car look unlike a typed one. No live DVLA call here or in CI — these are pure functions over the
/// shapes the upstream returns.
/// </remarks>
public sealed class VehicleLookupMappingTests
{
    [Theory]
    [InlineData("PETROL", FuelType.Petrol)]
    [InlineData("petrol", FuelType.Petrol)]
    [InlineData("DIESEL", FuelType.Diesel)]
    [InlineData("ELECTRICITY", FuelType.Electric)]
    [InlineData("HYBRID ELECTRIC", FuelType.Hybrid)]
    // The app models one Hybrid, so VES's several hybrid wordings all land there rather than the enum growing
    // a member to match an upstream's taxonomy.
    [InlineData("PLUG-IN HYBRID", FuelType.Hybrid)]
    [InlineData("GAS BI-FUEL", FuelType.LPG)]
    public void Ves_fuel_wording_maps_to_the_app_enum(string ves, FuelType expected) =>
        Assert.Equal(expected, LookupMapping.MapFuel(ves));

    /// <summary>
    /// An unrecognised fuel type is null, never a guess.
    /// </summary>
    /// <remarks>
    /// Null leaves the sheet's own select standing at its default, which the owner can see and correct. A guess
    /// would be invisible and would wrong every MPG figure computed from that car thereafter.
    /// </remarks>
    [Theory]
    [InlineData("STEAM")]
    [InlineData("")]
    [InlineData(null)]
    public void An_unknown_fuel_type_is_null_rather_than_a_guess(string? ves) =>
        Assert.Null(LookupMapping.MapFuel(ves));

    [Theory]
    [InlineData("SILVER", "Silver")]
    [InlineData("BLENHEIM SILVER", "Blenheim Silver")]
    [InlineData("blue", "Blue")]
    public void Ves_shouts_its_colours_and_the_app_does_not(string ves, string expected) =>
        Assert.Equal(expected, LookupMapping.Titlecase(ves));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_colour_stays_absent(string? ves) => Assert.Null(LookupMapping.Titlecase(ves));

    /// <summary>"BT53 AKJ" and "bt53akj" are the same plate, and the lookup must treat them so.</summary>
    [Theory]
    [InlineData("BT53 AKJ", "BT53AKJ")]
    [InlineData("bt53akj", "BT53AKJ")]
    [InlineData(" bt53-akj ", "BT53AKJ")]
    public void A_registration_normalises_the_way_the_rest_of_the_app_normalises_it(string input, string expected) =>
        Assert.Equal(expected, LookupMapping.Normalize(input));
}

/// <summary>
/// What the lookup does on a deployment that has no DVLA credentials — which is every fresh checkout, and CI.
/// </summary>
public sealed class VehicleLookupConfigurationTests
{
    [Fact]
    public void An_unconfigured_deployment_reports_it_rather_than_pretending_to_be_down()
    {
        var options = new VehicleLookupOptions();

        // Distinct from "unavailable": no key is permanent until someone provisions one, so the message must
        // say that rather than inviting a retry that cannot succeed.
        Assert.False(options.IsConfigured);
        Assert.False(options.IsMotConfigured);
    }

    [Fact]
    public void The_mot_half_is_configured_independently_of_the_identity_half()
    {
        // VES alone is a useful lookup: make, colour, year, engine and tax all come back, and the MOT seed is
        // the one thing missing. Requiring both keys to use either would make the feature all-or-nothing for
        // no reason.
        var options = new VehicleLookupOptions { VesApiKey = "ves-key" };

        Assert.True(options.IsConfigured);
        Assert.False(options.IsMotConfigured);
    }

    [Fact]
    public void Both_halves_configured_is_the_full_lookup()
    {
        var options = new VehicleLookupOptions
        {
            VesApiKey = "ves-key",
            MotApiKey = "mot-key",
            MotTokenUrl = "https://login.example/token",
            MotClientId = "client",
            MotClientSecret = "secret",
        };

        Assert.True(options.IsConfigured);
        Assert.True(options.IsMotConfigured);
    }

    /// <summary>
    /// The keys are server-side config and nothing else. This pins that they are not defaulted to a literal in
    /// code, which is the failure mode that puts a credential in a commit.
    /// </summary>
    [Fact]
    public void No_credential_is_defaulted_in_code()
    {
        var options = new VehicleLookupOptions();

        Assert.Null(options.VesApiKey);
        Assert.Null(options.MotApiKey);
        Assert.Null(options.MotClientId);
        Assert.Null(options.MotClientSecret);
        // The base URLs are not secrets and do have sensible defaults — a host is not a credential.
        Assert.NotEmpty(options.VesBaseUrl);
        Assert.NotEmpty(options.MotBaseUrl);
    }
}
