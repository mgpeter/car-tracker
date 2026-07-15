using CarTracker.Data;
using CarTracker.Shared.Metrics;

namespace CarTracker.Domain;

/// <summary>
/// Loads, then computes. Holds no logic of its own — every figure comes from <see cref="DerivedMetrics"/>.
/// </summary>
public sealed class DerivedMetricsService(IVehicleMetricsLoader loader, Clock clock) : IDerivedMetricsService
{
    public async Task<VehicleSummary?> GetVehicleSummaryAsync(int vehicleId, CancellationToken cancellationToken = default)
    {
        var data = await loader.LoadAsync(vehicleId, cancellationToken);

        // Null, not an empty summary: "this vehicle does not exist" and "this vehicle has no data" are
        // different answers, and the caller needs to tell them apart.
        return data is null ? null : DerivedMetrics.Compute(data, clock.Today());
    }

    public async Task<BudgetSummary?> GetBudgetSummaryAsync(
        int vehicleId,
        BudgetPeriod period = BudgetPeriod.CalendarYear,
        CancellationToken cancellationToken = default)
    {
        var data = await loader.LoadAsync(vehicleId, cancellationToken);

        return data is null ? null : DerivedMetrics.ComputeBudget(data, period, clock.Today());
    }

    /// <remarks>
    /// One summary per vehicle, then projected. That is N loads for N vehicles, and deliberately so: reusing
    /// the same computation the dashboard runs is what stops a card and a dashboard disagreeing about the same
    /// car. A hand-rolled aggregate query would be faster and would be a second implementation of every
    /// figure — spec §4 exists to prevent exactly that. This is a personal garage; N is 1.
    /// </remarks>
    public async Task<IReadOnlyList<GarageItem>> GetGarageAsync(CancellationToken cancellationToken = default)
    {
        var ids = await loader.ListVehicleIdsAsync(cancellationToken);
        var openAnomalies = await loader.CountOpenAnomaliesAsync(cancellationToken);

        var today = clock.Today();
        var items = new List<GarageItem>(ids.Count);

        foreach (var id in ids)
        {
            var data = await loader.LoadAsync(id, cancellationToken);

            // A vehicle that vanished between the id list and the load. Skip it rather than fail the garage:
            // one missing car must not stop the others rendering.
            if (data is null) continue;

            var summary = DerivedMetrics.Compute(data, today);
            items.Add(ToGarageItem(data.Vehicle, summary, openAnomalies.GetValueOrDefault(id)));
        }

        return items;
    }

    private static GarageItem ToGarageItem(Vehicle vehicle, VehicleSummary summary, int openAnomalies)
    {
        var renewals = summary.Renewals;

        return new GarageItem(
            VehicleId: summary.VehicleId,
            Registration: summary.Registration,
            Name: summary.Name,
            Status: vehicle.Status,
            IsDefault: vehicle.IsDefault,
            CurrentMileage: summary.Mileage.CurrentMileage,
            MilesSincePurchase: summary.Mileage.MilesSincePurchase,
            CostPerMile: summary.Spend.CostPerMile,
            MonthlyAverage: summary.Spend.MonthlyAverage,
            AverageMpg: summary.Fuel.AverageMpg,
            // The newest fill's own MPG, which is not the average and must not be shown as if it were. Null
            // when the last fill has no measurable interval — the card says so.
            LatestMpg: summary.Fuel.Entries.Count == 0 ? null : summary.Fuel.Entries[^1].Mpg,
            Mot: renewals.Mot,
            OverdueCheckCount: summary.Checks.OverdueCount,
            NeverLoggedCheckCount: summary.Checks.NeverLoggedCount,
            OpenAnomalyCount: openAnomalies)
        {
            RenewalsOk = IsOk(renewals.Mot) && IsOk(renewals.Insurance) && IsOk(renewals.RoadTax),
        };
    }

    /// <remarks>
    /// A renewal with no date is not OK and not urgent — it is unknown, and saying "Renewals OK" about a car
    /// whose insurance date nobody has entered would be the app inventing reassurance.
    /// </remarks>
    private static bool IsOk(Renewal renewal) => renewal.Urgency == RenewalUrgency.Ok;
}
