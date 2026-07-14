using System.Reflection;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain.Tests;

/// <summary>
/// Guards the shape of the result records, not their values.
/// </summary>
public sealed class ResultTypeTests
{
    /// <summary>Figures that are legitimately unavailable, and must therefore be nullable.</summary>
    public static TheoryData<Type, string> GenuinelyOptional() => new()
    {
        // No previous fill, or an interval spanning a partial fill.
        { typeof(FuelEntryMetrics), nameof(FuelEntryMetrics.Mpg) },
        { typeof(FuelEntryMetrics), nameof(FuelEntryMetrics.LitresPer100Km) },
        { typeof(FuelEntryMetrics), nameof(FuelEntryMetrics.MilesSinceLast) },
        // No fills at all.
        { typeof(FuelEconomySummary), nameof(FuelEconomySummary.AverageMpg) },
        { typeof(FuelEconomySummary), nameof(FuelEconomySummary.AveragePricePerLitre) },
        { typeof(FuelEconomySummary), nameof(FuelEconomySummary.LastFillDate) },
        // No readings; or zero miles since purchase.
        { typeof(MileageResult), nameof(MileageResult.CurrentMileage) },
        { typeof(MileageResult), nameof(MileageResult.MilesSincePurchase) },
        { typeof(SpendSummary), nameof(SpendSummary.CostPerMile) },
        { typeof(SpendSummary), nameof(SpendSummary.MonthlyAverage) },
        // A zero budget has no meaningful percentage; an unbudgeted category has no target.
        { typeof(BudgetLine), nameof(BudgetLine.PercentUsed) },
        { typeof(BudgetLine), nameof(BudgetLine.AnnualBudget) },
        // Never logged: no last date, no next due, no countdown.
        { typeof(CheckState), nameof(CheckState.LastPerformedOn) },
        { typeof(CheckState), nameof(CheckState.NextDue) },
        { typeof(CheckState), nameof(CheckState.DaysRemaining) },
        // No expiry recorded anywhere.
        { typeof(Renewal), nameof(Renewal.ExpiryDate) },
        { typeof(Renewal), nameof(Renewal.DaysRemaining) },
    };

    [Theory]
    [MemberData(nameof(GenuinelyOptional))]
    public void Unavailable_figures_are_nullable_rather_than_sentinel_values(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName);
        Assert.NotNull(property);

        var isNullable = Nullable.GetUnderlyingType(property.PropertyType) is not null
                         || new NullabilityInfoContext().Create(property).ReadState == NullabilityState.Nullable;

        Assert.True(
            isNullable,
            $"{type.Name}.{propertyName} must be nullable. These figures are genuinely unavailable in real " +
            "cases, and a sentinel (0, -1) is indistinguishable from a real measurement.");
    }

    [Fact]
    public void Check_status_has_a_never_logged_member()
    {
        // The workbook's Dashboard counts 17 of 18 checks because it has nowhere to put this state.
        Assert.Contains(CheckStatus.NeverLogged, Enum.GetValues<CheckStatus>());
        Assert.Equal(4, Enum.GetValues<CheckStatus>().Length);
    }

    [Fact]
    public void Check_counts_sum_to_the_total()
    {
        var summary = new CheckStatusSummary(7, 3, 7, 1, []);

        // The real BT53 AKJ distribution: 7 + 3 + 7 + 1 = 18, not 17.
        Assert.Equal(18, summary.TotalCount);
    }

    [Fact]
    public void Renewal_urgency_carries_no_colour()
    {
        // Mapping urgency to a hex value is the UI's job. A domain type naming a colour has the layering wrong.
        var names = Enum.GetNames<RenewalUrgency>();

        Assert.Equal(["Ok", "Amber", "Red"], names);
        Assert.DoesNotContain(typeof(Renewal).GetProperties(), p => p.Name.Contains("Colour", StringComparison.OrdinalIgnoreCase));
    }
}
