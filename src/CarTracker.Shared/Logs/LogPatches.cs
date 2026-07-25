namespace CarTracker.Shared.Logs;

/// <summary>
/// Partial edits to the small logs — the shared shape behind both the REST <c>PATCH</c> endpoints and the MCP
/// <c>update_*</c> tools, so a correction runs one path whichever surface makes it. Every field optional: a null
/// leaves the stored value untouched.
/// </summary>
public sealed record MileagePatch(
    DateOnly? ReadingDate = null,
    int? Mileage = null,
    string? Notes = null);

/// <inheritdoc cref="MileagePatch"/>
public sealed record TyrePatch(
    DateOnly? ReadingDate = null,
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

/// <inheritdoc cref="MileagePatch"/>
public sealed record WashPatch(
    DateOnly? WashDate = null,
    string? Location = null,
    string? WashType = null,
    decimal? Cost = null,
    int? Mileage = null,
    string? Notes = null);

/// <inheritdoc cref="MileagePatch"/>
public sealed record EquipmentPatch(
    string? Name = null,
    EquipmentStatus? Status = null,
    string? Category = null,
    DateOnly? PurchasedDate = null,
    string? SourceVendor = null,
    decimal? Cost = null,
    string? StoredAt = null,
    string? Notes = null);
