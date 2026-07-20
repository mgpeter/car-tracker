namespace CarTracker.Shared.Logs;

// Row shapes for the log screens, lifted out of the WebApi endpoint files so the REST endpoints, the shared
// query/write services and the MCP tools all return one shape. The derived-half wrappers (MileageLog, ServiceLog,
// ExpenseLog, …) stay in the endpoints — they combine these rows with figures from IDerivedMetricsService.

public sealed record MileageReadingItem(int Id, DateOnly ReadingDate, int Mileage, MileageOrigin Origin, string? Notes);

public sealed record ServiceRecordItem(
    int Id,
    DateOnly ServiceDate,
    string Type,
    int Mileage,
    string? Garage,
    string? WorkDone,
    string? PartsReplaced,
    decimal? Cost,
    DateOnly? NextDueDate,
    int? NextDueMileage,
    string? Notes);

/// <param name="PsiSpare">
/// Nullable because "not checked is not zero" — the workbook's spare-tyre-pressure check has never been logged.
/// </param>
public sealed record TyreReadingItem(
    int Id,
    DateOnly ReadingDate,
    int? Mileage,
    decimal? PsiFrontLeft,
    decimal? PsiFrontRight,
    decimal? PsiRearLeft,
    decimal? PsiRearRight,
    decimal? PsiSpare,
    decimal? TreadFrontLeft,
    decimal? TreadFrontRight,
    decimal? TreadRearLeft,
    decimal? TreadRearRight,
    string? Location,
    string? Tool,
    string? Notes);

public sealed record WashItem(
    int Id,
    DateOnly WashDate,
    string? Location,
    string? WashType,
    decimal? Cost,
    int? Mileage,
    string? Notes);

public sealed record EquipmentItemDto(
    int Id,
    string Name,
    string? Category,
    DateOnly? PurchasedDate,
    string? SourceVendor,
    decimal? Cost,
    string? StoredAt,
    EquipmentStatus Status,
    string? Notes);
