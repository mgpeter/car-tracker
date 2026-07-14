namespace CarTracker.Shared;

/// <summary>
/// Which surface wrote a record. Every mutable entity carries one, per README §6.
/// </summary>
public enum EntrySource
{
    // Deliberately no zero member. default(EntrySource) must not be a valid value, so that a caller
    // who forgets to set Source is detectable rather than silently attributed to whichever member
    // happened to be first. README §5.3 requires every MCP write to be attributable; that guarantee
    // is only worth having if an unset value cannot masquerade as a real one.
    Web = 1,
    Mcp = 2,
    Import = 3,
    Seed = 4,
}
