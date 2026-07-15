using CarTracker.Data;
using CarTracker.Domain.Calculators;
using CarTracker.Shared;

namespace CarTracker.Domain.Tests;

/// <summary>
/// Fails the build if <see cref="FillLevel"/> can change any computed fuel figure.
/// </summary>
/// <remarks>
/// <para>
/// Fill level is descriptive. Litres is a receipt figure accurate to 2dp; "the tank was about half" is a
/// glance at a needle. Letting the second gate the first made a soft observation the arbiter of a hard one.
/// Whether an MPG is trustworthy is decided by whether the number is physically plausible — see
/// <see cref="FuelEconomyCalculator.MinPlausibleMpg"/>.
/// </para>
/// <para>
/// <b>This was an IL scan asserting the identifier never appeared in CarTracker.Domain</b> — a stricter rule
/// than the one that matters, and a weaker one than it looks. Stricter, because it also forbade *reporting*
/// the field: when <see cref="Shared.Metrics.FuelEntryMetrics"/> grew the descriptive columns the fuel log
/// needs, the scan failed on a pass-through that no calculation touches, leaving a field the API accepts and
/// the app can never render back. Weaker, because "the identifier is absent" is only a proxy for the real
/// invariant, and a proxy is all it can ever check.
/// </para>
/// <para>
/// So this asserts the invariant itself: hold a log constant, vary only the fill levels, and every derived
/// figure must be identical. That survives a pass-through, and it fails on a gate however the gate is spelled
/// — aliased, inlined, or hidden behind a helper.
/// </para>
/// </remarks>
public sealed class NoFillLevelInCalculationsTests
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

    private static FuelEntry[] Log(FillLevel? level) =>
    [
        // A real-shaped log: a first fill with nothing to measure from, then two measurable intervals.
        Fill(1, "2026-06-01", 80_000, 40m, level),
        Fill(2, "2026-06-20", 80_300, 42m, level),
        Fill(3, "2026-07-01", 80_600, 45.5m, level),
    ];

    /// <summary>Every level, plus the unrecorded case — which is not the same as Full.</summary>
    public static TheoryData<FillLevel?> AllLevels() => [null, FillLevel.Full, FillLevel.Half, FillLevel.Quarter];

    [Theory]
    [MemberData(nameof(AllLevels))]
    public void No_derived_figure_moves_when_the_fill_level_changes(FillLevel? level)
    {
        // Full is the baseline only because it is the level the old gate waved through. Any would serve — the
        // whole point is that the choice cannot matter.
        var baseline = FuelEconomyCalculator.Calculate(Log(FillLevel.Full));
        var actual = FuelEconomyCalculator.Calculate(Log(level));

        Assert.Equal(baseline.AverageMpg, actual.AverageMpg);
        Assert.Equal(baseline.PerFillAverageMpg, actual.PerFillAverageMpg);
        Assert.Equal(baseline.BestMpg, actual.BestMpg);
        Assert.Equal(baseline.WorstMpg, actual.WorstMpg);
        Assert.Equal(baseline.MeasuredIntervalCount, actual.MeasuredIntervalCount);
        Assert.Equal(baseline.ImplausibleCount, actual.ImplausibleCount);
        Assert.Equal(baseline.AveragePricePerLitre, actual.AveragePricePerLitre);

        foreach (var (want, got) in baseline.Entries.Zip(actual.Entries))
        {
            Assert.Equal(want.Mpg, got.Mpg);
            Assert.Equal(want.LitresPer100Km, got.LitresPer100Km);
            Assert.Equal(want.IsReliable, got.IsReliable);
            Assert.Equal(want.IsPlausible, got.IsPlausible);
            Assert.Equal(want.UnreliableReason, got.UnreliableReason);
        }
    }

    [Fact]
    public void A_partial_fill_still_gets_an_mpg()
    {
        // The design's fuel sheet withholds MPG on a partial fill and prints "· ·". The fuel-basis spec removed
        // that rule: the litres are exactly as known as on any other fill, so the arithmetic is exactly as
        // valid. The concrete case the invariant above generalises.
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-20", 80_300, 40m, FillLevel.Full),
                Fill(2, "2026-07-01", 80_600, 45.5m, FillLevel.Quarter),
            ]);

        var partial = result.Entries.Single(e => e.FuelEntryId == 2);

        Assert.NotNull(partial.Mpg);
        Assert.True(partial.IsReliable);
    }

    [Fact]
    public void The_fill_level_is_reported_back_exactly_as_recorded()
    {
        // Descriptive, and therefore worth showing: a field the API accepts but the app never renders is a
        // field the user cannot check. Null stays null — "not recorded" is not Full.
        var result = FuelEconomyCalculator.Calculate(
            [
                Fill(1, "2026-06-01", 80_000, 40m, null),
                Fill(2, "2026-06-20", 80_300, 42m, FillLevel.Half),
            ]);

        Assert.Null(result.Entries[0].FillLevel);
        Assert.Equal(FillLevel.Half, result.Entries[1].FillLevel);
    }

    [Fact]
    public void FillLevel_still_exists_for_its_descriptive_use()
    {
        // Guards the guard: if the enum were deleted, the theory above would pass vacuously forever.
        Assert.Equal(3, Enum.GetValues<FillLevel>().Length);
    }
}
