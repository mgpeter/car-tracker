namespace CarTracker.Shared.Logs;

// Tasks, issues, check definitions, anomalies and the stored reference facts — row shapes and their derived-count
// wrappers, lifted out of the WebApi endpoints so the REST endpoints, the shared services and the MCP tools all
// return one shape.

public sealed record TaskItem(
    int Id,
    MaintenanceTaskKind Kind,
    Priority Priority,
    string Title,
    string? Description,
    decimal? EstimatedCost,
    MaintenanceTaskStatus Status,
    DateOnly? TargetDate,
    string? TargetService,
    DateOnly? CompletedDate,
    string? AssignedGarage,
    int? ServiceRecordId,
    string? Notes);

public sealed record TaskLog(
    IReadOnlyList<TaskItem> Tasks,
    decimal BundleCost,
    int BundleCount,
    decimal OpenEstimateTotal);

public sealed record IssueItem(
    int Id,
    string Title,
    Severity Severity,
    DateOnly FirstNoted,
    DateOnly? LastChecked,
    string? CurrentObservation,
    string? ActionIfWorsens,
    decimal? EstimatedFixCost,
    IssueStatus Status,
    DateOnly? ResolvedDate,
    string? Notes);

public sealed record IssueLog(
    IReadOnlyList<IssueItem> Issues,
    int MonitoringCount,
    int ResolvedCount,
    decimal WorstCaseCost);

public sealed record CheckDefinitionResponse(
    int Id,
    string Name,
    string CadenceLabel,
    int IntervalDays,
    string? Guidance,
    int DisplayOrder,
    bool IsActive);

public sealed record AnomalyItem(
    int Id,
    AnomalyKind Kind,
    AnomalySeverity Severity,
    string EntityType,
    int? EntityId,
    string Message,
    string? Detail,
    AnomalyStatus Status,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote,
    DateTimeOffset CreatedAt);

/// <summary>The stored reference facts an owner asks the assistant for — "what oil", "what pressure laden".</summary>
public sealed record VehicleReference(
    string Registration,
    string Make,
    string Model,
    string? Variant,
    int Year,
    string? Colour,
    string? EngineCode,
    int? EngineSizeCc,
    string? Vin,
    string? Transmission,
    string? Drivetrain,
    string? OilSpec,
    decimal? OilCapacityLitres,
    string? CoolantSpec,
    decimal? CoolantCapacityLitres,
    decimal? FuelTankCapacityLitres,
    string? BrakeFluidSpec,
    string? TransmissionOilSpec,
    string? SparkPlugPart,
    string? OilFilterPart,
    string? AirFilterPart,
    string? FuelFilterPart,
    string? CabinFilterPart,
    string? TyreSize,
    decimal? PressureFrontPsi,
    decimal? PressureRearPsi,
    decimal? PressureFrontLadenPsi,
    decimal? PressureRearLadenPsi,
    decimal? MinTreadMm,
    string? DefaultGarage);

public sealed record TaskInput(
    string Title,
    MaintenanceTaskKind Kind = MaintenanceTaskKind.DIY,
    Priority Priority = Priority.Medium,
    MaintenanceTaskStatus Status = MaintenanceTaskStatus.Open,
    string? Description = null,
    decimal? EstimatedCost = null,
    DateOnly? TargetDate = null,
    string? TargetService = null,
    string? AssignedGarage = null,
    string? Notes = null);

public sealed record IssueInput(
    string Title,
    DateOnly FirstNoted,
    Severity Severity = Severity.Low,
    IssueStatus Status = IssueStatus.Monitoring,
    DateOnly? LastChecked = null,
    string? CurrentObservation = null,
    string? ActionIfWorsens = null,
    decimal? EstimatedFixCost = null,
    string? Notes = null);
