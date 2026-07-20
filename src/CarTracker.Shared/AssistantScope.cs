namespace CarTracker.Shared;

/// <summary>
/// What an assistant (MCP) token may do. Read-only reaches the read tools; read-write also reaches the write
/// tools. The two are separate tokens so a token pasted somewhere casual cannot mutate data (README §5.1).
/// </summary>
public enum AssistantScope
{
    ReadOnly = 1,
    ReadWrite = 2,
}
