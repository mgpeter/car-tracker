using System.ComponentModel;
using CarTracker.Domain;
using CarTracker.Domain.Expenses;
using CarTracker.Domain.Logs;
using CarTracker.Shared.Logs;
using CarTracker.Shared.Metrics;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CarTracker.ModelContextProtocol.Tools;

/// <summary>
/// The raw per-screen list tools and the log-shaped reads — the full rows each screen shows, for the owner's
/// "read all my data" reach. They call the same query services the REST endpoints use, so a list here is the
/// same projection the web shows.
/// </summary>
[McpServerToolType]
public sealed class LogReadTools
{
    [McpServerTool(Name = "list_expenses")]
    [Description(
        "List a vehicle's expense entries (newest first): date, category, vendor, amount, mileage and whether the "
        + "row is mirrored from a fuel fill or a service record. For totals and cost-per-mile use get_spend_summary.")]
    public static async Task<McpResult<IReadOnlyList<ExpenseItem>>> ListExpenses(
        VehicleResolver resolver,
        ExpenseService expenses,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var rows = await expenses.ListAsync(v.VehicleId, cancellationToken);
        return new McpResult<IReadOnlyList<ExpenseItem>>(
            $"{rows.Count} expense(s) on {v.Registration}, totalling £{rows.Sum(r => r.Amount):N2}.", rows);
    }

    [McpServerTool(Name = "list_fuel_fillups")]
    [Description(
        "List a vehicle's fuel fill-ups with each one's derived per-fill MPG, L/100km, litres, price and station "
        + "(newest first). MPG is null on the first fill and on partial fills that defer to the next full one. For "
        + "the averages and range use get_fuel_status instead.")]
    public static async Task<McpResult<IReadOnlyList<FuelEntryMetrics>>> ListFuelFillups(
        VehicleResolver resolver,
        IDerivedMetricsService metrics,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var summary = await metrics.GetVehicleSummaryAsync(v.VehicleId, cancellationToken)
            ?? throw new McpException($"Could not load {v.Registration}.");

        // Newest first, to match the other list_* tools; the summary orders oldest-first for the chart.
        var rows = summary.Fuel.Entries.OrderByDescending(e => e.EntryDate).ThenByDescending(e => e.FuelEntryId).ToList();
        return new McpResult<IReadOnlyList<FuelEntryMetrics>>($"{rows.Count} fill(s) on {v.Registration}.", rows);
    }

    [McpServerTool(Name = "list_mileage")]
    [Description("List a vehicle's odometer readings, newest first, each with its origin (Manual, Fuel, Service, …).")]
    public static async Task<McpResult<IReadOnlyList<MileageReadingItem>>> ListMileage(
        VehicleResolver resolver,
        LogQueryService queries,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var rows = await queries.ListMileageAsync(v.VehicleId, cancellationToken);
        return new McpResult<IReadOnlyList<MileageReadingItem>>($"{rows.Count} mileage reading(s) on {v.Registration}.", rows);
    }

    [McpServerTool(Name = "list_service_history")]
    [Description("List a vehicle's service and MOT records, oldest first: date, type, mileage, garage, work done and cost.")]
    public static async Task<McpResult<IReadOnlyList<ServiceRecordItem>>> ListServiceHistory(
        VehicleResolver resolver,
        LogQueryService queries,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var rows = await queries.ListServiceRecordsAsync(v.VehicleId, cancellationToken);
        return new McpResult<IReadOnlyList<ServiceRecordItem>>($"{rows.Count} service record(s) on {v.Registration}.", rows);
    }

    [McpServerTool(Name = "list_tyre_readings")]
    [Description("List a vehicle's tyre readings: pressure and tread by corner (plus spare), oldest first.")]
    public static async Task<McpResult<IReadOnlyList<TyreReadingItem>>> ListTyreReadings(
        VehicleResolver resolver,
        LogQueryService queries,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var rows = await queries.ListTyresAsync(v.VehicleId, cancellationToken);
        return new McpResult<IReadOnlyList<TyreReadingItem>>($"{rows.Count} tyre reading(s) on {v.Registration}.", rows);
    }

    [McpServerTool(Name = "list_wash_log")]
    [Description("List a vehicle's wash log, oldest first: date, location, type and cost.")]
    public static async Task<McpResult<IReadOnlyList<WashItem>>> ListWashLog(
        VehicleResolver resolver,
        LogQueryService queries,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var rows = await queries.ListWashesAsync(v.VehicleId, cancellationToken);
        return new McpResult<IReadOnlyList<WashItem>>($"{rows.Count} wash(es) on {v.Registration}.", rows);
    }

    [McpServerTool(Name = "list_equipment")]
    [Description("List a vehicle's equipment/kit inventory: what is owned, on order and still to buy, with cost and storage.")]
    public static async Task<McpResult<IReadOnlyList<EquipmentItemDto>>> ListEquipment(
        VehicleResolver resolver,
        LogQueryService queries,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var rows = await queries.ListEquipmentAsync(v.VehicleId, cancellationToken);
        return new McpResult<IReadOnlyList<EquipmentItemDto>>($"{rows.Count} equipment item(s) on {v.Registration}.", rows);
    }

    [McpServerTool(Name = "list_check_definitions")]
    [Description(
        "List a vehicle's regular-check definitions (including retired ones): name, cadence, interval days, "
        + "guidance and whether active. For the live due status use get_check_status instead.")]
    public static async Task<McpResult<IReadOnlyList<CheckDefinitionResponse>>> ListCheckDefinitions(
        VehicleResolver resolver,
        LogQueryService queries,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var rows = await queries.ListCheckDefinitionsAsync(v.VehicleId, cancellationToken);
        return new McpResult<IReadOnlyList<CheckDefinitionResponse>>($"{rows.Count} check definition(s) on {v.Registration}.", rows);
    }

    [McpServerTool(Name = "get_open_tasks")]
    [Description(
        "The DIY and Workshop tasks for a vehicle, with the derived bundle cost of the open Workshop jobs waiting "
        + "on one garage visit. Optional 'vehicle' is a registration or id; omitted, the default vehicle.")]
    public static async Task<McpResult<TaskLog>> GetOpenTasks(
        VehicleResolver resolver,
        LogQueryService queries,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var log = await queries.GetTaskLogAsync(v.VehicleId, cancellationToken);
        var open = log.Tasks.Count(t => t.Status != Shared.MaintenanceTaskStatus.Done);
        return new McpResult<TaskLog>(
            $"{open} open task(s) on {v.Registration}; workshop bundle £{log.BundleCost:N2} across {log.BundleCount} job(s).", log);
    }

    [McpServerTool(Name = "get_issues")]
    [Description(
        "The issues watchlist for a vehicle — things being monitored — worst first, with each one's current "
        + "observation and the derived worst-case cost of everything still monitored.")]
    public static async Task<McpResult<IssueLog>> GetIssues(
        VehicleResolver resolver,
        LogQueryService queries,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var log = await queries.GetIssueLogAsync(v.VehicleId, cancellationToken);
        return new McpResult<IssueLog>(
            $"{log.MonitoringCount} issue(s) monitored on {v.Registration}; worst-case £{log.WorstCaseCost:N2}.", log);
    }

    [McpServerTool(Name = "get_data_integrity")]
    [Description(
        "The data-integrity queue for a vehicle: flags the detectors raised (a below-odometer reading, an "
        + "implausible MPG), worst first. Open flags by default; set includeResolved=true to include decided ones.")]
    public static async Task<McpResult<IReadOnlyList<AnomalyItem>>> GetDataIntegrity(
        VehicleResolver resolver,
        LogQueryService queries,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        [Description("Include Corrected/Accepted/Dismissed flags, not just Open ones.")] bool includeResolved = false,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var rows = await queries.ListAnomaliesAsync(v.VehicleId, includeResolved, cancellationToken);
        var open = rows.Count(a => a.Status == Shared.AnomalyStatus.Open);
        return new McpResult<IReadOnlyList<AnomalyItem>>(
            open == 0 ? $"No open integrity flags on {v.Registration}." : $"{open} open integrity flag(s) on {v.Registration}.", rows);
    }

    [McpServerTool(Name = "get_reference")]
    [Description(
        "The stored reference facts for a vehicle — engine, fluids and capacities (oil, coolant, brake, tank), "
        + "part numbers, tyre sizes and pressures (including laden), and the default garage. Answers 'what oil "
        + "does it take' or 'what pressure for a full load'. Optional 'vehicle' is a registration or id.")]
    public static async Task<McpResult<VehicleReference>> GetReference(
        VehicleResolver resolver,
        LogQueryService queries,
        [Description("Registration or id. Omit for the default vehicle.")] string? vehicle = null,
        CancellationToken cancellationToken = default)
    {
        var v = await ToolHelpers.ResolveVehicleAsync(resolver, vehicle, cancellationToken);
        var reference = await queries.GetReferenceAsync(v.VehicleId, cancellationToken)
            ?? throw new McpException($"Could not load {v.Registration}.");
        return new McpResult<VehicleReference>(
            $"{reference.Registration} — {reference.Make} {reference.Model}{(reference.OilSpec is { } o ? $", oil {o}" : "")}.",
            reference);
    }
}
