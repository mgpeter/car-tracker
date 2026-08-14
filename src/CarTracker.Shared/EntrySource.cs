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

    /// <summary>
    /// The in-app chat assistant: figures a model read off a photograph or a PDF, which a signed-in human then
    /// confirmed on a draft card.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="Web"/>, even though the owner does press Save in the web app. The numbers in
    /// a chat-drafted row were read by a model rather than typed by a person, and "which surface produced this?"
    /// is exactly the question the audit block exists to answer when a litre count later looks odd. Deliberately
    /// not <see cref="Mcp"/> either: the tools are shared but the surfaces are not — an MCP write is unattended
    /// and carries a scoped bearer token that <c>AssistantWriteAudit</c> records, and a chat write has neither,
    /// so folding them together would leave chat writes looking like audited MCP writes with no audit row.
    /// </remarks>
    Chat = 5,
}
