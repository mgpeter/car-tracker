using CarTracker.Shared;

namespace CarTracker.Domain.Writes;

/// <summary>
/// Which surface is executing the current tool call — <see cref="EntrySource.Mcp"/> under <c>/mcp</c>,
/// <see cref="EntrySource.Chat"/> under the in-app assistant.
/// </summary>
/// <remarks>
/// <para>
/// The tools used to hold <c>private const EntrySource Source = EntrySource.Mcp</c>. That was right while one
/// surface invoked them and wrong the moment a second did: the same method now writes rows whose provenance
/// differs, and the audit block exists precisely to record that difference.
/// </para>
/// <para>
/// <b>Resolved from DI, never a tool argument.</b> The tools take it as a parameter the way they take
/// <c>VehicleResolver</c> — the container supplies it, so it does not appear in the tool's JSON schema and the
/// model cannot set it. A model-settable source would let an assistant claim a figure it read off a photograph
/// had been typed by a person, which is the one thing this column must never be able to say falsely.
/// </para>
/// <para>
/// Scoped and mutable, mirroring <c>CurrentUserAccessor</c>: the default is <see cref="EntrySource.Mcp"/>, so
/// every existing call site and test behaves exactly as before, and the chat's request scope sets
/// <see cref="EntrySource.Chat"/> before invoking anything.
/// </para>
/// </remarks>
public sealed class WriteSurface
{
    /// <summary>The surface to stamp on rows written by the current call. Defaults to <see cref="EntrySource.Mcp"/>.</summary>
    public EntrySource Source { get; private set; } = EntrySource.Mcp;

    /// <summary>Pins the surface for this scope. Called once, by whoever owns the request.</summary>
    public void Set(EntrySource source)
    {
        if (source is default(EntrySource))
        {
            // The enum has no zero member on purpose; letting one in here would defeat that from the outside.
            throw new ArgumentOutOfRangeException(nameof(source), "A write surface must name a real EntrySource.");
        }

        Source = source;
    }
}
