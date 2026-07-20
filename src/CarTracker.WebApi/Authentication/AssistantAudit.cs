using CarTracker.Data;
using CarTracker.Domain.Writes;
using Microsoft.AspNetCore.Http;

namespace CarTracker.WebApi.Authentication;

/// <summary>
/// Records an assistant write against the token that made it, resolved from the current request's principal. When
/// there is no assistant token on the request (a web endpoint, an unauthenticated path) it does nothing — the
/// write still happens, it is simply not attributed to a token.
/// </summary>
public sealed class AssistantAudit(IHttpContextAccessor httpContextAccessor, CarTrackerDbContext context, TimeProvider timeProvider)
    : IAssistantAudit
{
    public async Task RecordWriteAsync(string tool, int? vehicleId, string summary, CancellationToken cancellationToken = default)
    {
        var tokenClaim = httpContextAccessor.HttpContext?.User.FindFirst(AssistantClaims.TokenId)?.Value;
        if (!int.TryParse(tokenClaim, out var tokenId)) return;

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
