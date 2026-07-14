using CarTracker.Domain;

namespace CarTracker.Domain.Tests;

public sealed class UnitsTests
{
    [Fact]
    public void Imperial_gallon_is_exact()
    {
        // UK MPG. The US gallon is 3.785411784 L — using it would overstate MPG by ~20%.
        Assert.Equal(4.54609m, Units.LitresPerImperialGallon);
    }

    [Fact]
    public void Mile_is_exact()
    {
        Assert.Equal(1.609344m, Units.KmPerMile);
    }

    [Fact]
    public void The_mpg_to_litres_per_100km_invariant_holds()
    {
        // 4.54609 x 100 / 1.609344 = 282.4809...
        Assert.Equal(282.48m, Math.Round(Units.MpgTimesLitresPer100Km, 2));
    }

    [Fact]
    public void A_worked_example_agrees_with_the_invariant()
    {
        // 300 miles on 45.5 litres.
        //   mpg  = 300 * 4.54609 / 45.5           = 1363.827 / 45.5      = 29.9742... -> 29.97
        //   l100 = 45.5 * 100 / (300 * 1.609344)  = 4550 / 482.8032      =  9.4241... ->  9.42
        const decimal miles = 300m;
        const decimal litres = 45.5m;

        var mpg = miles * Units.LitresPerImperialGallon / litres;
        var litresPer100Km = litres * 100m / (miles * Units.KmPerMile);

        Assert.Equal(29.97m, Math.Round(mpg, 2));
        Assert.Equal(9.42m, Math.Round(litresPer100Km, 2));
        Assert.Equal(
            Math.Round(Units.MpgTimesLitresPer100Km, 4),
            Math.Round(mpg * litresPer100Km, 4));
    }
}
