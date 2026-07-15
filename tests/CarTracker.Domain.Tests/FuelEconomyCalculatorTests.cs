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
        decimal? totalCost = null,
        FillLevel? fillLevel = null) =>
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
        // 80,300 -> 80,600 = 300 miles, on 45.5 litres.
        //   mpg  = 300 * 4.54609 / 45.5           = 1363.827 / 45.5 = 29.9742... -> 29.97
        //   l100 = 45.5 * 100 / (300 * 1.609344)  = 4550 / 482.8032 =  9.4241... ->  9.42
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
        Assert.True(second.IsPlausible);
    }

    /// <summary>
    /// One property over the whole history, rather than a handful of examples.
    /// </summary>
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

        // Absent, not implausible: there is no figure to judge.
        Assert.True(first.IsPlausible);
    }

    /// <summary>
    /// Fill level is descriptive and must change nothing.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(FillLevel.Full)]
    [InlineData(FillLevel.Half)]
    [InlineData(FillLevel.Quarter)]
    public void The_recorded_fill_level_does_not_affect_any_figure(FillLevel? level)
    {
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-20", 80_300, 40m, fillLevel: level),
                Fill(2, "2026-07-01", 80_600, 45.5m, fillLevel: level),
            ]);

        var second = result.Entries.Single(e => e.FuelEntryId == 2);

        // Identical for every level and for none: litres and miles are the only inputs.
        Assert.Equal(29.97m, Math.Round(second.Mpg!.Value, 2));
        Assert.True(second.IsReliable);
        Assert.True(second.IsPlausible);
        Assert.Equal(1, result.MeasuredIntervalCount);
    }

    /// <summary>
    /// The splash is excluded because 272 mpg is impossible — not because a box was ticked.
    /// </summary>
    [Fact]
    public void An_implausible_figure_is_excluded_from_the_aggregates_but_kept_on_its_entry()
    {
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-01", 80_000, 40m),
                Fill(2, "2026-06-10", 80_300, 45m),
                // 300 miles on a 5-litre splash: 272 mpg. Exact litres, correct arithmetic, not a real figure.
                Fill(3, "2026-06-20", 80_600, 5m),
                Fill(4, "2026-07-01", 80_900, 44m),
            ]);

        var splash = result.Entries.Single(e => e.FuelEntryId == 3);
        Assert.True(splash.Mpg > 200m);
        Assert.False(splash.IsPlausible);
        Assert.Equal(1, result.ImplausibleCount);

        // Marked, not deleted — but it must never become "best".
        Assert.NotNull(result.BestMpg);
        Assert.True(result.BestMpg < 40m, $"Best MPG {result.BestMpg} came from the splash.");

        // Unlike the old fill-level gate, the interval *after* the splash is unaffected: its own number is
        // plausible, so it counts.
        Assert.Equal(3, result.MeasuredIntervalCount);
        Assert.True(result.Entries.Single(e => e.FuelEntryId == 4).IsPlausible);
    }

    /// <summary>
    /// The case the old fill-level gate could never catch.
    /// </summary>
    [Fact]
    public void A_mistyped_odometer_is_caught_even_though_nothing_was_said_about_the_tank()
    {
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-01", 80_000, 40m, fillLevel: FillLevel.Full),
                // 80,900 fat-fingered as 89,000: 9,000 miles on 45 litres = 909 mpg. Both tanks marked Full,
                // so the old gate declared this reliable and folded it into the average.
                Fill(2, "2026-06-10", 89_000, 45m, fillLevel: FillLevel.Full),
            ]);

        var mistyped = result.Entries.Single(e => e.FuelEntryId == 2);

        Assert.True(mistyped.IsReliable);   // a figure exists
        Assert.False(mistyped.IsPlausible); // but it is not physically possible
        Assert.Null(result.BestMpg);        // and nothing plausible remains to report
    }

    // Litres are doubles here only because C# forbids decimal attribute arguments; converted below.
    [Theory]
    [InlineData(300, 45.0, true)]    // 30.3 mpg
    [InlineData(300, 5.0, false)]    // 272.8 mpg — splash
    [InlineData(300, 150.0, false)]  // 9.1 mpg — below the band; a leak or a missed entry
    [InlineData(300, 136.0, true)]   // 10.03 mpg — just inside
    [InlineData(300, 19.5, true)]    // 69.9 mpg — just inside
    [InlineData(300, 19.4, false)]   // 70.3 mpg — just outside
    public void The_plausibility_band_is_10_to_70(int miles, double litres, bool expectedPlausible)
    {
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-01", 80_000, 40m),
                Fill(2, "2026-06-10", 80_000 + miles, (decimal)litres),
            ]);

        Assert.Equal(expectedPlausible, result.Entries.Single(e => e.FuelEntryId == 2).IsPlausible);
    }

    [Fact]
    public void A_non_advancing_odometer_yields_no_mpg_rather_than_a_divide_by_zero()
    {
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-20", 80_600, 40m),
                Fill(2, "2026-07-01", 80_600, 45m),
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

        Assert.Null(result.Entries.Single(e => e.FuelEntryId == 2).Mpg);
    }

    [Fact]
    public void Total_litres_is_the_plain_sum_and_is_not_doubled()
    {
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
        // 50 L at £1.40 = £70.00; 10 L at £1.60 = £16.00. £86.00 over 60 L = £1.4333/L.
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
        Assert.Null(result.PerFillAverageMpg);
        Assert.Null(result.BestMpg);
        Assert.Null(result.AveragePricePerLitre);
        Assert.Null(result.LastFillDate);
        Assert.Equal(0m, result.TotalLitres);
        Assert.Empty(result.Entries);
    }

    [Fact]
    public void One_fill_has_totals_but_no_economy()
    {
        var result = FuelEconomyCalculator.Calculate([Fill(1, "2026-06-01", 80_000, 40m)]);

        Assert.Equal(40m, result.TotalLitres);
        Assert.Null(result.AverageMpg);
        Assert.Equal(0, result.MeasuredIntervalCount);
        Assert.Equal(new DateOnly(2026, 6, 1), result.LastFillDate);
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
    }

    // ---- Cumulative average (task 3) --------------------------------------------------------------------

    [Fact]
    public void Cumulative_average_excludes_the_first_fills_litres()
    {
        // Span 80,000 -> 80,900 = 900 miles. Litres burned across it = 45 + 44 = 89 (not 129: the opening
        // 40 L filled a tank already burned before recording began).
        //   900 * 4.54609 / 89 = 4091.481 / 89 = 45.97...
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-01", 80_000, 40m),
                Fill(2, "2026-06-10", 80_300, 45m),
                Fill(3, "2026-07-01", 80_900, 44m),
            ]);

        Assert.Equal(45.97m, Math.Round(result.AverageMpg!.Value, 2));

        // Including the opening 40 L would give 31.71 — a third too low.
        Assert.NotEqual(31.71m, Math.Round(result.AverageMpg.Value, 2));
    }

    [Fact]
    public void Cumulative_average_needs_two_fills_and_a_positive_span()
    {
        Assert.Null(FuelEconomyCalculator.Calculate([Fill(1, "2026-06-01", 80_000, 40m)]).AverageMpg);

        var noSpan = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-01", 80_000, 40m),
                Fill(2, "2026-06-10", 80_000, 45m),
            ]);

        Assert.Null(noSpan.AverageMpg);
    }

    [Fact]
    public void Cumulative_average_ignores_fill_level_entirely()
    {
        var withLevels = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-01", 80_000, 40m, fillLevel: FillLevel.Quarter),
                Fill(2, "2026-06-10", 80_300, 45m, fillLevel: FillLevel.Half),
                Fill(3, "2026-07-01", 80_900, 44m, fillLevel: FillLevel.Full),
            ]);

        var withoutLevels = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-01", 80_000, 40m),
                Fill(2, "2026-06-10", 80_300, 45m),
                Fill(3, "2026-07-01", 80_900, 44m),
            ]);

        Assert.Equal(withoutLevels.AverageMpg, withLevels.AverageMpg);
    }
}
