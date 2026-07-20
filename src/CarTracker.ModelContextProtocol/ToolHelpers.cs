using CarTracker.Domain;
using CarTracker.Domain.Writes;
using ModelContextProtocol;

namespace CarTracker.ModelContextProtocol;

/// <summary>
/// Shared plumbing for the tools: resolve the optional <c>vehicle</c> argument, or fail with a message the model
/// can act on. An unknown vehicle is an error, never a guess (README §5.2).
/// </summary>
internal static class ToolHelpers
{
    public static async Task<VehicleRef> ResolveVehicleAsync(
        VehicleResolver resolver,
        string? vehicle,
        CancellationToken cancellationToken)
    {
        var resolved = await resolver.ResolveAsync(vehicle, cancellationToken);
        if (resolved is not null) return resolved;

        throw new McpException(vehicle is null or ""
            ? "No vehicle was given and there is no default vehicle — the garage is empty. Add a car first."
            : $"No vehicle matches '{vehicle}'. Call list_vehicles to see the registrations and pick one.");
    }

    /// <summary>
    /// Turns a shared <see cref="WriteResult{T}"/> into a tool result: the value plus a success sentence (with any
    /// integrity flags appended, since monotonicity is flagged not rejected), or an <see cref="McpException"/>
    /// carrying the refusal so the model can correct itself.
    /// </summary>
    public static McpResult<T> ToResult<T>(WriteResult<T> result, string success)
    {
        switch (result.Status)
        {
            case WriteStatus.Created:
            case WriteStatus.Updated:
                var flagNote = result.Flags.Count > 0
                    ? " Flagged (recorded anyway): " + string.Join("; ", result.Flags.Select(f => f.Message)) + "."
                    : string.Empty;
                return new McpResult<T>(success + flagNote, result.Value!);

            case WriteStatus.Validation:
                throw new McpException("Rejected — " + string.Join(" ", result.Errors!.SelectMany(e => e.Value)));

            case WriteStatus.Conflict:
                throw new McpException($"{result.ConflictTitle}: {result.ConflictDetail}");

            default:
                throw new McpException("The item was not found.");
        }
    }
}
