using System.ComponentModel;
using CarTracker.Domain;
using CarTracker.Domain.Reminders;
using CarTracker.Shared.Metrics;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CarTracker.ModelContextProtocol.Tools;

/// <summary>
/// The derived read tools — "what needs my attention", "what's my MPG", "how much have I spent". Every figure
/// comes from <see cref="IDerivedMetricsService"/>, the same instance the web API calls, so the assistant and the
/// dashboard cannot disagree (README §4, §5.2).
/// </summary>
[McpServerToolType]
public sealed class SummaryReadTools
{
    [McpServerTool(Name = "get_due_items")]
    [Description(
        "What needs the owner's attention on a vehicle: overdue or soon-due checks, upcoming renewals (MOT, tax, "
        + "insurance), service due, and firing reminders. This is the 'what should I do' call. Optional 'vehicle' "
        + "is a registration or id; omitted, it uses the default vehicle. Set includeQuiet=true to also list "
        + "amber/soon items that are not yet firing.")]
    public static async Task<McpResult<ReminderList>> GetDueItems(
        VehicleResolver resolver,
        IDerivedMetricsService metrics,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Include amber/soon items that are not yet firing.")] bool includeQuiet = false,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var summary = await metrics.GetVehicleSummaryAsync(v.VehicleId, cancellationToken)
            ?? throw new McpException($"Could not load {v.Registration}.");

        var reminders = ReminderEvaluator.Evaluate(summary, includeQuiet);

        var text = reminders.FiringCount == 0
            ? $"Nothing firing on {v.Registration} ({v.Name})."
            : $"{reminders.FiringCount} item(s) need attention on {v.Registration}: "
              + string.Join("; ", reminders.Items.Where(i => i.Firing).Select(i => $"{i.Subject} — {i.Reason}"));

        return new McpResult<ReminderList>(text, reminders);
    }

    [McpServerTool(Name = "get_vehicle_summary")]
    [Description(
        "A vehicle's headline state: current mileage, miles/days since purchase, the next renewals with day "
        + "counts, spend rollups, check status, fuel headline and full-tank range. Optional 'vehicle' is a "
        + "registration or id; omitted, the default vehicle.")]
    public static async Task<McpResult<VehicleOverview>> GetVehicleSummary(
        VehicleResolver resolver,
        IDerivedMetricsService metrics,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var s = await metrics.GetVehicleSummaryAsync(v.VehicleId, cancellationToken)
            ?? throw new McpException($"Could not load {v.Registration}.");

        var overview = new VehicleOverview(
            s.Registration, s.Name, s.AsOfDate, s.Identity, s.Mileage, s.Renewals, s.Spend,
            FuelStatus.From(s), s.Checks, s.Integrity, s.FullTankRangeMiles);

        var mileage = s.Mileage.CurrentMileage is { } m ? $"{m:N0} mi" : "no mileage yet";
        return new McpResult<VehicleOverview>($"{s.Registration} ({s.Name}): {mileage} as of {s.AsOfDate:d MMM yyyy}.", overview);
    }

    [McpServerTool(Name = "get_fuel_status")]
    [Description(
        "Fuel economy for a vehicle: last fill date, average/best/worst MPG, average price per litre, and the "
        + "estimated full-tank range. Optional 'vehicle' is a registration or id; omitted, the default vehicle.")]
    public static async Task<McpResult<FuelStatus>> GetFuelStatus(
        VehicleResolver resolver,
        IDerivedMetricsService metrics,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var s = await metrics.GetVehicleSummaryAsync(v.VehicleId, cancellationToken)
            ?? throw new McpException($"Could not load {v.Registration}.");

        var fuel = FuelStatus.From(s);
        var text = fuel.AverageMpg is { } avg
            ? $"{v.Registration}: {avg:0.0} mpg average over {fuel.MeasuredIntervalCount} interval(s); last fill {fuel.LastFillDate:d MMM yyyy}."
            : $"{v.Registration}: not enough fills to measure MPG yet ({fuel.FillCount} on record).";
        return new McpResult<FuelStatus>(text, fuel);
    }

    [McpServerTool(Name = "get_spend_summary")]
    [Description(
        "Spend rollups for a vehicle: year-to-date and since-purchase totals by category, cost per mile and "
        + "monthly average. Optional 'vehicle' is a registration or id; omitted, the default vehicle.")]
    public static async Task<McpResult<SpendSummary>> GetSpendSummary(
        VehicleResolver resolver,
        IDerivedMetricsService metrics,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var s = await metrics.GetVehicleSummaryAsync(v.VehicleId, cancellationToken)
            ?? throw new McpException($"Could not load {v.Registration}.");

        var cpm = s.Spend.CostPerMile is { } c ? $"£{c:0.00}/mi" : "cost/mile n/a";
        return new McpResult<SpendSummary>(
            $"{v.Registration}: £{s.Spend.TotalYtd:N2} YTD, £{s.Spend.TotalSincePurchase:N2} since purchase ({cpm}).",
            s.Spend);
    }

    [McpServerTool(Name = "get_check_status")]
    [Description(
        "Per-check due status for a vehicle: how many are OK, due soon, overdue or never logged, and each check's "
        + "next-due date. Optional 'vehicle' is a registration or id; omitted, the default vehicle.")]
    public static async Task<McpResult<CheckStatusSummary>> GetCheckStatus(
        VehicleResolver resolver,
        IDerivedMetricsService metrics,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var s = await metrics.GetVehicleSummaryAsync(v.VehicleId, cancellationToken)
            ?? throw new McpException($"Could not load {v.Registration}.");

        var c = s.Checks;
        return new McpResult<CheckStatusSummary>(
            $"{v.Registration}: {c.OkCount} ok, {c.DueSoonCount} due soon, {c.OverdueCount} overdue, {c.NeverLoggedCount} never logged.",
            c);
    }

    [McpServerTool(Name = "get_budget")]
    [Description(
        "Budget targets versus actual spend for a vehicle, by category, with variance and over-budget flags. "
        + "Period is CalendarYear (default), Rolling12Months or SincePurchase. Optional 'vehicle' is a "
        + "registration or id; omitted, the default vehicle.")]
    public static async Task<McpResult<BudgetSummary>> GetBudget(
        VehicleResolver resolver,
        IDerivedMetricsService metrics,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("CalendarYear, Rolling12Months or SincePurchase. Defaults to CalendarYear.")] BudgetPeriod period = BudgetPeriod.CalendarYear,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var budget = await metrics.GetBudgetSummaryAsync(v.VehicleId, period, cancellationToken)
            ?? throw new McpException($"Could not load {v.Registration}.");

        return new McpResult<BudgetSummary>(
            $"{v.Registration} ({budget.Period}): £{budget.TotalActual:N2} spent against £{budget.TotalBudget:N2} budgeted.",
            budget);
    }
}

/// <summary>A vehicle's headline state, slimmed — the full fill log is deliberately not carried (use list_fuel_fillups).</summary>
public sealed record VehicleOverview(
    string Registration,
    string Name,
    DateOnly AsOfDate,
    VehicleIdentity Identity,
    MileageResult Mileage,
    RenewalSummary Renewals,
    SpendSummary Spend,
    FuelStatus Fuel,
    CheckStatusSummary Checks,
    IntegritySummary Integrity,
    decimal? FullTankRangeMiles);

/// <summary>The fuel headline, without the per-fill log (spec §5.2 <c>get_fuel_status</c>).</summary>
public sealed record FuelStatus(
    DateOnly? LastFillDate,
    decimal? AverageMpg,
    decimal? BestMpg,
    decimal? WorstMpg,
    decimal? AveragePricePerLitre,
    int FillCount,
    int MeasuredIntervalCount,
    decimal? FullTankRangeMiles)
{
    public static FuelStatus From(VehicleSummary s) => new(
        s.Fuel.LastFillDate,
        s.Fuel.AverageMpg,
        s.Fuel.BestMpg,
        s.Fuel.WorstMpg,
        s.Fuel.AveragePricePerLitre,
        s.Fuel.FillCount,
        s.Fuel.MeasuredIntervalCount,
        s.FullTankRangeMiles);
}
