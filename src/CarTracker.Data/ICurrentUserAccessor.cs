namespace CarTracker.Data;

/// <summary>
/// Carries the current request's resolved owner to <see cref="CarTrackerDbContext"/>'s vehicle query filter, so
/// every vehicle read is scoped to the signed-in user without each call site remembering to filter.
/// </summary>
/// <remarks>
/// A request-scoped concern. There are three states, and the filter reads them as:
/// <list type="bullet">
/// <item><see cref="BypassOwnership"/> — no request context at all (a background job, a design-time tool, a
/// direct-constructed test context with no accessor): filtering is off, every vehicle is visible.</item>
/// <item>a resolved <see cref="OwnerId"/> — the signed-in user: only their vehicles are visible.</item>
/// <item>a request with no resolved user (an API-key or anonymous principal, or an unprovisioned identity):
/// <see cref="OwnerId"/> is null and <see cref="BypassOwnership"/> is false, which matches <b>no</b> vehicle —
/// the safe default is to see nothing, not everything.</item>
/// </list>
/// </remarks>
public interface ICurrentUserAccessor
{
    /// <summary>True when ownership filtering is off because there is no request context (system/background).</summary>
    bool BypassOwnership { get; }

    /// <summary>The signed-in user's id, or null when there is a request but no resolved user (blocks everything).</summary>
    int? OwnerId { get; }
}

/// <summary>
/// The mutable per-request implementation. Defaults to <see cref="BypassOwnership"/> = true so a context that
/// no middleware ever touches (a background job, a test) sees every vehicle; the request pipeline calls
/// <see cref="SetOwner"/> exactly once, which turns filtering on for that request.
/// </summary>
public sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    public bool BypassOwnership { get; private set; } = true;

    public int? OwnerId { get; private set; }

    /// <summary>Pins this request to an owner (or to no owner, which sees nothing). Turns the filter on.</summary>
    public void SetOwner(int? ownerId)
    {
        BypassOwnership = false;
        OwnerId = ownerId;
    }
}
