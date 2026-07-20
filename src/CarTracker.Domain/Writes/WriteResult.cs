using CarTracker.Shared.Logs;

namespace CarTracker.Domain.Writes;

/// <summary>
/// How a write turned out, independent of transport. The web endpoint maps this to an HTTP status; the MCP tool
/// maps it to a success envelope or an error string. Both callers share the outcome so a rule (a refusal, a
/// validation failure) reads the same whichever surface hit it.
/// </summary>
public enum WriteStatus
{
    Created,
    Updated,
    Validation,
    Conflict,
    NotFound,
}

/// <summary>
/// The result of a shared write. Carries the created/updated value on success, a per-field error map on a
/// validation failure (the same <c>Dictionary&lt;string,string[]&gt;</c> the endpoints already surface as an RFC
/// 9457 <c>errors</c> map), or a title+detail on a conflict.
/// </summary>
public sealed record WriteResult<T>(
    WriteStatus Status,
    T? Value,
    IReadOnlyDictionary<string, string[]>? Errors,
    string? ConflictTitle,
    string? ConflictDetail)
{
    public bool IsSuccess => Status is WriteStatus.Created or WriteStatus.Updated;

    /// <summary>
    /// Integrity flags the write raised (a below-odometer reading, an implausible MPG). Empty on most writes; a
    /// caller relays them so "logged, but flagged" survives to the surface. Monotonicity is flagged, never
    /// rejected (§5.3), so a flag never means the write failed.
    /// </summary>
    public IReadOnlyList<AnomalyFlag> Flags { get; init; } = [];

    public static WriteResult<T> Created(T value) => new(WriteStatus.Created, value, null, null, null);

    public static WriteResult<T> Created(T value, IReadOnlyList<AnomalyFlag> flags) =>
        new(WriteStatus.Created, value, null, null, null) { Flags = flags };

    public static WriteResult<T> Updated(T value) => new(WriteStatus.Updated, value, null, null, null);

    public static WriteResult<T> Updated(T value, IReadOnlyList<AnomalyFlag> flags) =>
        new(WriteStatus.Updated, value, null, null, null) { Flags = flags };

    public static WriteResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(WriteStatus.Validation, default, errors, null, null);

    public static WriteResult<T> Invalid(string field, string message) =>
        Invalid(new Dictionary<string, string[]> { [field] = [message] });

    public static WriteResult<T> Conflict(string title, string detail) =>
        new(WriteStatus.Conflict, default, null, title, detail);

    public static WriteResult<T> NotFound() => new(WriteStatus.NotFound, default, null, null, null);
}
