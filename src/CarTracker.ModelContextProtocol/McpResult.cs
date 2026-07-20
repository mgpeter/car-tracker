namespace CarTracker.ModelContextProtocol;

/// <summary>
/// The shape every MCP tool returns: a machine-parseable <typeparamref name="T"/> plus a one-line human
/// summary the model can relay verbatim.
/// </summary>
/// <remarks>
/// README §5's design note asks each tool to return "structured JSON plus a short human summary string" — so
/// the assistant has both an object to reason over and a sentence to say. Wrapping every result in one envelope
/// keeps that contract uniform across the whole tool surface.
/// </remarks>
public sealed record McpResult<T>(string Summary, T Data);
