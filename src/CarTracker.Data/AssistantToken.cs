using CarTracker.Shared;

namespace CarTracker.Data;

/// <summary>
/// A bearer token the assistant (MCP) authenticates with — the read-only / read-write scoped tokens of README
/// §5.1, a separate mechanism from the web front-end's static API key (DEC-009). The secret is shown once on
/// creation and only its hash is stored, so a leaked database yields no usable token.
/// </summary>
public sealed class AssistantToken
{
    public int Id { get; set; }

    /// <summary>A human label — "Claude Desktop", "phone shortcut" — so a token can be recognised to revoke it.</summary>
    public required string Name { get; set; }

    /// <summary>SHA-256 of the secret. The secret itself is never stored.</summary>
    public required string TokenHash { get; set; }

    public AssistantScope Scope { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>Set when revoked. A revoked token authenticates nothing; the row is kept for the audit trail.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Reads are counted, not listed (the design's wording).</summary>
    public int ReadCount { get; set; }

    public int WriteCount { get; set; }
}

/// <summary>
/// One assistant write, recorded verbatim for the ASSISTANT ACCESS panel — the observable half of
/// <c>source = "mcp"</c>. Reads are counted on the token, not listed here.
/// </summary>
public sealed class AssistantWriteAudit
{
    public int Id { get; set; }
    public int TokenId { get; set; }

    /// <summary>The tool name, e.g. <c>log_fuel_fillup</c>.</summary>
    public required string Tool { get; set; }

    public int? VehicleId { get; set; }

    /// <summary>A one-line summary of the change, as the tool reported it.</summary>
    public required string Summary { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }
}
