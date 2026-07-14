using CarTracker.Data;
using CarTracker.Domain.Calculators;
using CarTracker.Shared;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Tests;

public sealed class FuelEconomyCalculatorTests
{
    private static FuelEntry Fill(
        int id,
        string date,
        int mileage,
        decimal litres,
        decimal pricePerLitre = 1.489m,
        FillLevel fillLevel = FillLevel.Full,
        decimal? totalCost = null) =>
        new()
        {
            Id = id,
            VehicleId = 1,
            EntryDate = DateOnly.Parse(date),
            Mileage = mileage,
            Litres = litres,
            PricePerLitre = pricePerLitre,
            TotalCost = totalCost ?? Math.Round(litres * pricePerLitre, 2),
            FillLevel = fillLevel,
            Source = EntrySource.Import,
        };

    [Fact]
    public void Mpg_and_litres_per_100km_match_hand_computed_values()
    {
        // 80,300 -> 80,600 = 300 miles, on 45.5 litres, both fills Full.
        //   mpg  = 300 * 4.54609 / 45.5          = 1363.827 / 45.5 = 29.9742... -> 29.97
        //   l100 = 45.5 * 100 / (300 * 1.609344) = 4550 / 482.8032 =  9.4241... ->  9.42
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-20", 80_300, 40m),
                Fill(2, "2026-07-01", 80_600, 45.5m),
            ]);

        var second = result.Entries.Single(e => e.FuelEntryId == 2);

        Assert.Equal(300, second.MilesSinceLast);
        Assert.Equal(29.97m, Math.Round(second.Mpg!.Value, 2));
        Assert.Equal(9.42m, Math.Round(second.LitresPer100Km!.Value, 2));
        Assert.True(second.IsReliable);
    }

    /// <summary>
    /// One property over the whole history, rather than a handful of examples.
    /// </summary>
    /// <remarks>
    /// mpg * l/100km is constant (≈282.4809) for any non-zero interval. This catches a transposed constant or
    /// an inverted formula in either direction — errors that individually plausible examples can miss.
    /// </remarks>
    [Fact]
    public void The_mpg_to_litres_per_100km_invariant_holds_across_every_interval()
    {
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-03-20", 76_800, 42.1m),
                Fill(2, "2026-04-05", 77_150, 38.4m),
                Fill(3, "2026-04-22", 77_500, 41.9m),
                Fill(4, "2026-05-10", 77_930, 46.2m),
                Fill(5, "2026-06-01", 78_240, 33.7m),
            ]);

        var measured = result.Entries.Where(e => e.Mpg is not null).ToList();
        Assert.Equal(4, measured.Count);

        foreach (var entry in measured)
        {
            Assert.Equal(
                Math.Round(Units.MpgTimesLitresPer100Km, 4),
                Math.Round(entry.Mpg!.Value * entry.LitresPer100Km!.Value, 4));
        }
    }

    [Fact]
    public void The_first_fill_has_no_mpg_because_there_is_no_interval()
    {
        var result = FuelEconomyCalculator.Calculate([Fill(1, "2026-03-20", 76_800, 42.1m)]);

        var first = result.Entries.Single();
        Assert.Null(first.Mpg);
        Assert.Null(first.MilesSinceLast);
        Assert.False(first.IsReliable);
        Assert.Equal(MpgUnreliableReason.NoPreviousFill, first.UnreliableReason);
    }

    [Theory]
    [InlineData(FillLevel.Half, FillLevel.Full)]
    [InlineData(FillLevel.Full, FillLevel.Half)]
    [InlineData(FillLevel.Quarter, FillLevel.Quarter)]
    public void An_interval_touching_a_partial_fill_is_unreliable(FillLevel previous, FillLevel current)
    {
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-20", 80_300, 40m, fillLevel: previous),
                Fill(2, "2026-07-01", 80_600, 45.5m, fillLevel: current),
            ]);

        var second = result.Entries.Single(e => e.FuelEntryId == 2);

        // The figure is still computed — it is arithmetic — but it is not a measurement. The litres added
        // only equal the fuel consumed if the tank was full at both ends.
        Assert.NotNull(second.Mpg);
        Assert.False(second.IsReliable);
        Assert.Equal(MpgUnreliableReason.PartialFill, second.UnreliableReason);
    }

    /// <summary>
    /// A partial fill invalidates <b>two</b> intervals, not one.
    /// </summary>
    /// <remarks>
    /// The interval ending at the partial fill is unmeasurable because the litres added do not equal the fuel
    /// burned. The <i>next</i> interval is equally unmeasurable, because the tank was not full at its start —
    /// so those litres cover an unknown amount of the previous leg too. Four fills with one partial in the
    /// middle therefore yield one reliable interval, not two.
    /// </remarks>
    [Fact]
    public void A_partial_fills_inflated_mpg_is_excluded_from_best_and_worst()
    {
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-01", 80_000, 40m),
                Fill(2, "2026-06-10", 80_300, 45m),                              // 1->2: Full to Full, reliable
                // A 5-litre splash after 300 miles computes to ~273 mpg. Left in, it becomes "best MPG" and
                // sits on the Dashboard as good news — the single most likely way this feature could lie.
                Fill(3, "2026-06-20", 80_600, 5m, fillLevel: FillLevel.Quarter), // 2->3: unreliable, partial end
                Fill(4, "2026-07-01", 80_900, 44m),                              // 3->4: unreliable, partial start
            ]);

        Assert.Equal(1, result.ReliableIntervalCount);
        Assert.NotNull(result.BestMpg);
        Assert.True(result.BestMpg < 40m, $"Best MPG {result.BestMpg} came from the partial fill.");

        // The inflated figure still exists on the entry — it is simply marked, not deleted.
        var splash = result.Entries.Single(e => e.FuelEntryId == 3);
        Assert.True(splash.Mpg > 200m);
        Assert.False(splash.IsReliable);
    }

    [Fact]
    public void A_non_advancing_odometer_yields_no_mpg_rather_than_a_divide_by_zero()
    {
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-20", 80_600, 40m),
                Fill(2, "2026-07-01", 80_600, 45m), // same mileage
            ]);

        var second = result.Entries.Single(e => e.FuelEntryId == 2);
        Assert.Null(second.Mpg);
        Assert.Null(second.LitresPer100Km);
        Assert.Equal(MpgUnreliableReason.NonMonotonicMileage, second.UnreliableReason);
    }

    [Fact]
    public void A_backwards_odometer_yields_no_mpg_rather_than_a_negative()
    {
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-20", 80_600, 40m),
                Fill(2, "2026-07-01", 80_400, 45m),
            ]);

        var second = result.Entries.Single(e => e.FuelEntryId == 2);
        Assert.Null(second.Mpg);
        Assert.Equal(MpgUnreliableReason.NonMonotonicMileage, second.UnreliableReason);
    }

    [Fact]
    public void Total_litres_is_the_plain_sum_and_is_not_doubled()
    {
        // The workbook's Dashboard says 1,112.94 against a real 556.47 — exactly 2.0000x, because the
        // summary counts every fill twice.
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-01", 80_000, 40.5m),
                Fill(2, "2026-06-10", 80_300, 45.25m),
                Fill(3, "2026-07-01", 80_600, 38.75m),
            ]);

        Assert.Equal(124.5m, result.TotalLitres);
        Assert.Equal(3, result.FillCount);
    }

    [Fact]
    public void Average_price_per_litre_is_volume_weighted()
    {
        // 50 L at £1.40 = £70.00; 10 L at £1.60 = £16.00. Total £86.00 over 60 L = £1.4333/L.
        // A plain mean of the price column gives £1.50 — the answer to a question nobody asked.
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-01", 80_000, 50m, pricePerLitre: 1.40m),
                Fill(2, "2026-06-10", 80_300, 10m, pricePerLitre: 1.60m),
            ]);

        Assert.Equal(1.433m, Math.Round(result.AveragePricePerLitre!.Value, 3));
        Assert.NotEqual(1.50m, Math.Round(result.AveragePricePerLitre.Value, 3));
    }

    [Fact]
    public void Average_price_uses_the_receipt_total_not_litres_times_price()
    {
        // Forecourt rounding: 45.23 L at 1.489 = £67.3474, charged as £67.35. The receipt is authoritative.
        var result = FuelEconomyCalculator.Calculate(
            [Fill(1, "2026-06-01", 80_000, 45.23m, pricePerLitre: 1.489m, totalCost: 67.35m)]);

        Assert.Equal(67.35m, result.TotalCost);
        Assert.Equal(Math.Round(67.35m / 45.23m, 4), Math.Round(result.AveragePricePerLitre!.Value, 4));
    }

    [Fact]
    public void No_fills_yields_nulls_and_zero_totals()
    {
        var result = FuelEconomyCalculator.Calculate([]);

        Assert.Null(result.AverageMpg);
        Assert.Null(result.BestMpg);
        Assert.Null(result.WorstMpg);
        Assert.Null(result.AveragePricePerLitre);
        Assert.Null(result.LastFillDate);
        Assert.Equal(0m, result.TotalLitres);
        Assert.Equal(0, result.FillCount);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void One_fill_has_totals_but_no_economy()
    {
        var result = FuelEconomyCalculator.Calculate([Fill(1, "2026-06-01", 80_000, 40m)]);

        Assert.Equal(40m, result.TotalLitres);
        Assert.Null(result.AverageMpg);
        Assert.Equal(0, result.ReliableIntervalCount);
        Assert.Equal(new DateOnly(2026, 6, 1), result.LastFillDate);
    }

    [Fact]
    public void Fleet_average_is_computed_from_unrounded_intervals()
    {
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-01", 80_000, 40m),
                Fill(2, "2026-06-10", 80_300, 45m),
                Fill(3, "2026-06-20", 80_610, 43m),
            ]);

        // Rounding each interval to 1dp before averaging gives a different answer than averaging then
        // rounding. Never round intermediates.
        var expected = result.Entries
            .Where(e => e.IsReliable)
            .Average(e => e.Mpg!.Value);

        Assert.Equal(Math.Round(expected, 6), Math.Round(result.AverageMpg!.Value, 6));
    }

    [Fact]
    public void Fills_are_not_assumed_sorted()
    {
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(3, "2026-07-01", 80_600, 38m),
                Fill(1, "2026-06-01", 80_000, 40m),
                Fill(2, "2026-06-10", 80_300, 45m),
            ]);

        Assert.Equal(new DateOnly(2026, 7, 1), result.LastFillDate);
        Assert.Equal(300, result.Entries.Single(e => e.FuelEntryId == 2).MilesSinceLast);
        Assert.Equal(300, result.Entries.Single(e => e.FuelEntryId == 3).MilesSinceLast);
    }
}
