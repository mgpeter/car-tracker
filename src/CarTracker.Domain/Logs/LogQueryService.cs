using CarTracker.Data;
using CarTracker.Shared;
using CarTracker.Shared.Logs;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Logs;

/// <summary>
/// The shared read projections for the log screens — the raw rows each screen shows, as the REST endpoints and
/// the MCP <c>list_*</c> tools both read them. One projection per screen, in one place, so a list on the assistant
/// is byte-for-byte the list on the web.
/// </summary>
public sealed class LogQueryService(CarTrackerDbContext context)
{
    public Task<List<MileageReadingItem>> ListMileageAsync(int vehicleId, CancellationToken cancellationToken = default) =>
        context.MileageReadings
            .AsNoTracking()
            .Where(m => m.VehicleId == vehicleId)
            .OrderByDescending(m => m.ReadingDate).ThenByDescending(m => m.Id)
            .Select(m => new MileageReadingItem(m.Id, m.ReadingDate, m.Mileage, m.Origin, m.Notes))
            .ToListAsync(cancellationToken);

    public Task<List<ServiceRecordItem>> ListServiceRecordsAsync(int vehicleId, CancellationToken cancellationToken = default) =>
        context.ServiceRecords
            .AsNoTracking()
            .Where(r => r.VehicleId == vehicleId)
            .OrderBy(r => r.ServiceDate).ThenBy(r => r.Id)
            .Select(r => new ServiceRecordItem(
                r.Id, r.ServiceDate, r.Type, r.Mileage, r.Garage, r.WorkDone, r.PartsReplaced,
                r.Cost, r.NextDueDate, r.NextDueMileage, r.Notes))
            .ToListAsync(cancellationToken);

    public Task<List<TyreReadingItem>> ListTyresAsync(int vehicleId, CancellationToken cancellationToken = default) =>
        context.TyreReadings
            .AsNoTracking()
            .Where(t => t.VehicleId == vehicleId)
            .OrderBy(t => t.ReadingDate).ThenBy(t => t.Id)
            .Select(t => new TyreReadingItem(
                t.Id, t.ReadingDate, t.Mileage,
                t.PsiFrontLeft, t.PsiFrontRight, t.PsiRearLeft, t.PsiRearRight, t.PsiSpare,
                t.TreadFrontLeft, t.TreadFrontRight, t.TreadRearLeft, t.TreadRearRight,
                t.Location, t.Tool, t.Notes))
            .ToListAsync(cancellationToken);

    public Task<List<WashItem>> ListWashesAsync(int vehicleId, CancellationToken cancellationToken = default) =>
        context.WashEntries
            .AsNoTracking()
            .Where(w => w.VehicleId == vehicleId)
            .OrderBy(w => w.WashDate).ThenBy(w => w.Id)
            .Select(w => new WashItem(w.Id, w.WashDate, w.Location, w.WashType, w.Cost, w.Mileage, w.Notes))
            .ToListAsync(cancellationToken);

    public Task<List<EquipmentItemDto>> ListEquipmentAsync(int vehicleId, CancellationToken cancellationToken = default) =>
        context.EquipmentItems
            .AsNoTracking()
            .Where(e => e.VehicleId == vehicleId)
            .OrderBy(e => e.Status).ThenBy(e => e.Category).ThenBy(e => e.Name)
            .Select(e => new EquipmentItemDto(
                e.Id, e.Name, e.Category, e.PurchasedDate, e.SourceVendor, e.Cost, e.StoredAt, e.Status, e.Notes))
            .ToListAsync(cancellationToken);

    public Task<List<CheckDefinitionResponse>> ListCheckDefinitionsAsync(int vehicleId, CancellationToken cancellationToken = default) =>
        context.CheckDefinitions
            .AsNoTracking()
            .Where(d => d.VehicleId == vehicleId)
            .OrderBy(d => d.DisplayOrder).ThenBy(d => d.Name)
            .Select(d => new CheckDefinitionResponse(d.Id, d.Name, d.CadenceLabel, d.IntervalDays, d.Guidance, d.DisplayOrder, d.IsActive))
            .ToListAsync(cancellationToken);

    /// <summary>The integrity queue: open flags by default, or every flag when <paramref name="includeResolved"/>.</summary>
    public Task<List<AnomalyItem>> ListAnomaliesAsync(int vehicleId, bool includeResolved, CancellationToken cancellationToken = default)
    {
        var query = context.DataAnomalies.AsNoTracking().Where(a => a.VehicleId == vehicleId);
        if (!includeResolved) query = query.Where(a => a.Status == AnomalyStatus.Open);

        return query
            .OrderByDescending(a => a.Severity).ThenByDescending(a => a.CreatedAt)
            .Select(a => new AnomalyItem(
                a.Id, a.Kind, a.Severity, a.EntityType, a.EntityId, a.Message, a.Detail,
                a.Status, a.ResolvedAt, a.ResolutionNote, a.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>The DIY/Workshop tasks with the derived bundle figures the tasks board shows.</summary>
    public async Task<TaskLog> GetTaskLogAsync(int vehicleId, CancellationToken cancellationToken = default)
    {
        var tasks = await context.MaintenanceTasks
            .AsNoTracking()
            .Where(t => t.VehicleId == vehicleId)
            .OrderBy(t => t.Status).ThenBy(t => t.Priority).ThenBy(t => t.TargetDate)
            .Select(t => new TaskItem(
                t.Id, t.Kind, t.Priority, t.Title, t.Description, t.EstimatedCost, t.Status,
                t.TargetDate, t.TargetService, t.CompletedDate, t.AssignedGarage, t.ServiceRecordId, t.Notes))
            .ToListAsync(cancellationToken);

        var bundle = tasks
            .Where(t => t.Kind == MaintenanceTaskKind.Workshop && t.Status != MaintenanceTaskStatus.Done)
            .ToList();

        return new TaskLog(
            tasks,
            BundleCost: bundle.Sum(t => t.EstimatedCost ?? 0m),
            BundleCount: bundle.Count,
            OpenEstimateTotal: tasks.Where(t => t.Status != MaintenanceTaskStatus.Done).Sum(t => t.EstimatedCost ?? 0m));
    }

    /// <summary>The issues watchlist with the derived worst-case cost of everything still monitored.</summary>
    public async Task<IssueLog> GetIssueLogAsync(int vehicleId, CancellationToken cancellationToken = default)
    {
        var issues = await context.Issues
            .AsNoTracking()
            .Where(i => i.VehicleId == vehicleId)
            .OrderBy(i => i.Status).ThenBy(i => i.Severity).ThenByDescending(i => i.FirstNoted)
            .Select(i => new IssueItem(
                i.Id, i.Title, i.Severity, i.FirstNoted, i.LastChecked, i.CurrentObservation,
                i.ActionIfWorsens, i.EstimatedFixCost, i.Status, i.ResolvedDate, i.Notes))
            .ToListAsync(cancellationToken);

        var monitoring = issues.Where(i => i.Status == IssueStatus.Monitoring).ToList();

        return new IssueLog(
            issues,
            MonitoringCount: monitoring.Count,
            ResolvedCount: issues.Count - monitoring.Count,
            WorstCaseCost: monitoring.Sum(i => i.EstimatedFixCost ?? 0m));
    }

    /// <summary>The stored reference facts — the one screen that is not derived.</summary>
    public Task<VehicleReference?> GetReferenceAsync(int vehicleId, CancellationToken cancellationToken = default) =>
        context.Vehicles
            .AsNoTracking()
            .Where(v => v.Id == vehicleId)
            .Select(v => new VehicleReference(
                v.Registration, v.Make, v.Model, v.Variant, v.Year, v.Colour, v.EngineCode, v.EngineSizeCc, v.Vin,
                v.Transmission, v.Drivetrain,
                v.Fluids.OilSpec, v.Fluids.OilCapacityLitres, v.Fluids.CoolantSpec, v.Fluids.CoolantCapacityLitres,
                v.Fluids.FuelTankCapacityLitres, v.Fluids.BrakeFluidSpec, v.Fluids.TransmissionOilSpec,
                v.Fluids.SparkPlugPart, v.Fluids.OilFilterPart, v.Fluids.AirFilterPart, v.Fluids.FuelFilterPart, v.Fluids.CabinFilterPart,
                v.Tyres.TyreSize, v.Tyres.PressureFrontPsi, v.Tyres.PressureRearPsi,
                v.Tyres.PressureFrontLadenPsi, v.Tyres.PressureRearLadenPsi, v.Tyres.MinTreadMm,
                v.DefaultGarage))
            .SingleOrDefaultAsync(cancellationToken);
}
