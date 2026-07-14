using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// Implemented by every mutable entity. README §6 requires created/updated timestamps and a source
/// on all of them.
/// </summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset UpdatedAt { get; set; }

    EntrySource Source { get; set; }
}
