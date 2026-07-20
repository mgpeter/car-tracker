namespace CarTracker.Shared.Logs;

// Transport-independent write inputs for the log screens. The REST endpoint maps its request body to these; an
// MCP write tool maps its arguments to them — both then call the one write service, so an invariant (the tyre
// odometer shadow, the wash-location ensure) cannot fork between surfaces.

public sealed record MileageInput(DateOnly ReadingDate, int Mileage, string? Notes = null);

public sealed record TyreInput(
    DateOnly ReadingDate,
    int? Mileage = null,
    decimal? PsiFrontLeft = null,
    decimal? PsiFrontRight = null,
    decimal? PsiRearLeft = null,
    decimal? PsiRearRight = null,
    decimal? PsiSpare = null,
    decimal? TreadFrontLeft = null,
    decimal? TreadFrontRight = null,
    decimal? TreadRearLeft = null,
    decimal? TreadRearRight = null,
    string? Location = null,
    string? Tool = null,
    string? Notes = null);

public sealed record WashInput(
    DateOnly WashDate,
    string? Location = null,
    string? WashType = null,
    decimal? Cost = null,
    int? Mileage = null,
    string? Notes = null);

public sealed record EquipmentInput(
    string Name,
    EquipmentStatus Status = EquipmentStatus.Owned,
    string? Category = null,
    DateOnly? PurchasedDate = null,
    string? SourceVendor = null,
    decimal? Cost = null,
    string? StoredAt = null,
    string? Notes = null);
