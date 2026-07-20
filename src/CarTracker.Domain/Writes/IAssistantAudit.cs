namespace CarTracker.Domain.Writes;

/// <summary>
/// Records an assistant write to the audit trail (README §5.1 — "every write listed verbatim"). The
/// implementation lives in the WebApi, where it can read the current request's authenticated token; in tests and
/// non-MCP callers it is a no-op, so a write service never has to care who called it.
/// </summary>
public interface IAssistantAudit
{
    Task RecordWriteAsync(string tool, int? vehicleId, string summary, CancellationToken cancellationToken = default);
}

/// <summary>The default: does nothing. Used wherever there is no MCP request in play (the web endpoints, tests).</summary>
public sealed class NullAssistantAudit : IAssistantAudit
{
    public Task RecordWriteAsync(string tool, int? vehicleId, string summary, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
