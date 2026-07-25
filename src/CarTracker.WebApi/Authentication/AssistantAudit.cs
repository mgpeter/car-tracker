using CarTracker.Data;
using CarTracker.Domain.Writes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace CarTracker.WebApi.Authentication;

/// <summary>
/// Records an assistant write against the token that made it, resolved from the current request's principal. When
/// there is no assistant token on the request (a web endpoint, an unauthenticated path) it does nothing — the
/// write still happens, it is simply not attributed to a token.
/// </summary>
/// <remarks>
/// The audit row and the token's <c>WriteCount</c> bump are written on a <b>fresh DI scope</b> — a private
/// <see cref="CarTrackerDbContext"/> with its own connection — never the request's scoped context that the tool
/// just used. Sharing that context (and connection) coupled every request through the one hot
/// <c>assistant_tokens</c> row: a tool that wedged mid-write left an open transaction on the shared connection,
/// and the trailing audit — plus every later request's token bump — blocked behind it, freezing the whole MCP
/// server. An isolated context that opens, writes and commits in microseconds cannot hold that lock across a
/// tool's work.
/// </remarks>
public sealed class AssistantAudit(
    IHttpContextAccessor httpContextAccessor,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider)
    : IAssistantAudit
{
    public async Task RecordWriteAsync(string tool, int? vehicleId, string summary, CancellationToken cancellationToken = default)
    {
        var tokenClaim = httpContextAccessor.HttpContext?.User.FindFirst(AssistantClaims.TokenId)?.Value;
        if (!int.TryParse(tokenClaim, out var tokenId)) return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CarTrackerDbContext>();

        context.AssistantWriteAudits.Add(new AssistantWriteAudit
        {
            TokenId = tokenId,
            Tool = tool,
            VehicleId = vehicleId,
            Summary = summary,
            TimestampUtc = timeProvider.GetUtcNow(),
        });

        var token = await context.AssistantTokens.FindAsync([tokenId], cancellationToken);
        if (token is not null) token.WriteCount++;

        await context.SaveChangesAsync(cancellationToken);
    }
}
