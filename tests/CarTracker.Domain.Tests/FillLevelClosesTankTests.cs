using CarTracker.Data;
using CarTracker.Domain.Calculators;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Tests;

/// <summary>
/// Pins the contract <see cref="FillLevel"/> now carries in the fuel calculator: it is read only as a hard
/// binary — does this fill <b>close the tank</b> or not. Full and unrecorded (null) close it and measure MPG
/// across the open segment; Half and Quarter defer the figure to the next closing fill, with their litres
/// accumulated into it. How partial a partial is (Half vs Quarter) is never read.
/// </summary>
/// <remarks>
/// This replaces the former <c>NoFillLevelInCalculationsTests</c>, which asserted that no derived figure could
/// move when the fill level changed. The partial-fill spec (2026-07-18) deliberately makes fill level
/// load-bearing for grouping, so that invariant is now the wrong one. What must hold instead is the grouping
/// contract below — and, critically, that it reduces to the old behaviour on an all-closing history (proven by
/// the untouched workbook fixture in <c>DashboardReproductionTests</c>).
/// </remarks>
public sealed class FillLevelClosesTankTests
{
    private static FuelEntry Fill(int id, string date, int mileage, decimal litres, FillLevel? fillLevel) =>
        new()
        {
            Id = id,
            VehicleId = 1,
            EntryDate = DateOnly.Parse(date),
            Mileage = mileage,
            Litres = litres,
            PricePerLitre = 1.489m,
            TotalCost = Math.Round(litres * 1.489m, 2),
            FillLevel = fillLevel,
            Source = EntrySource.Import,
        };

    /// <summary>Full and unrecorded both close the tank, so a fill between two of either measures the same.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(FillLevel.Full)]
    public void Full_and_unrecorded_both_close_the_tank(FillLevel? closing)
    {
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-01", 80_000, 40m, closing),
                Fill(2, "2026-06-20", 80_300, 42m, closing),
            ]);

        var second = result.Entries.Single(e => e.FuelEntryId == 2);
        Assert.NotNull(second.Mpg);
        Assert.Equal(1, second.SpannedFillCount);
        Assert.Equal(MpgUnreliableReason.NoPreviousFill, result.Entries[0].UnreliableReason);
    }

    /// <summary>Half and Quarter both defer, and to the same figure — the magnitude is not read.</summary>
    [Theory]
    [InlineData(FillLevel.Half)]
    [InlineData(FillLevel.Quarter)]
    public void Half_and_quarter_both_defer(FillLevel partial)
    {
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-01", 80_000, 40m, FillLevel.Full),
                Fill(2, "2026-06-10", 80_150, 20m, partial),
                Fill(3, "2026-06-20", 80_330, 30m, FillLevel.Full),
            ]);

        Assert.Null(result.Entries.Single(e => e.FuelEntryId == 2).Mpg);
        Assert.Equal(MpgUnreliableReason.AwaitingFullTank,
            result.Entries.Single(e => e.FuelEntryId == 2).UnreliableReason);

        // The closing figure is identical for Half and Quarter (asserted by comparing to a fixed expected value
        // that both inline cases reach).
        Assert.Equal(30.00m, Math.Round(result.Entries.Single(e => e.FuelEntryId == 3).Mpg!.Value, 2));
    }

    /// <summary>
    /// The one thing the arithmetic reads about a level is whether it closes the tank. Swapping Half for Quarter
    /// changes nothing; swapping Full for Half changes everything.
    /// </summary>
    [Fact]
    public void Only_closes_versus_not_is_read_never_the_magnitude()
    {
        FuelEntry[] Log(FillLevel middle) =>
        [
            Fill(1, "2026-06-01", 80_000, 40m, FillLevel.Full),
            Fill(2, "2026-06-10", 80_150, 20m, middle),
            Fill(3, "2026-06-20", 80_330, 30m, FillLevel.Full),
        ];

        var half = FuelEconomyCalculator.Calculate(Log(FillLevel.Half));
        var quarter = FuelEconomyCalculator.Calculate(Log(FillLevel.Quarter));
        var full = FuelEconomyCalculator.Calculate(Log(FillLevel.Full));

        // Half and Quarter are indistinguishable to the arithmetic.
        Assert.Equal(half.MeasuredIntervalCount, quarter.MeasuredIntervalCount);
        Assert.Equal(half.Entries.Single(e => e.FuelEntryId == 3).Mpg,
            quarter.Entries.Single(e => e.FuelEntryId == 3).Mpg);

        // Making the middle fill Full instead splits the one grouped figure into two ungrouped ones.
        Assert.Equal(1, half.MeasuredIntervalCount);
        Assert.Equal(2, full.MeasuredIntervalCount);
    }

    [Fact]
    public void The_fill_level_is_reported_back_exactly_as_recorded()
    {
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-01", 80_000, 40m, null),
                Fill(2, "2026-06-20", 80_300, 42m, FillLevel.Half),
            ]);

        Assert.Null(result.Entries[0].FillLevel);
        Assert.Equal(FillLevel.Half, result.Entries[1].FillLevel);
    }

    [Fact]
    public void FillLevel_still_has_three_values()
    {
        // Guards the guard: were the enum reshaped, the theories above could pass vacuously.
        Assert.Equal(3, Enum.GetValues<FillLevel>().Length);
    }
}
