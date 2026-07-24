using System.Security.Claims;
using CarTracker.Data;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Authentication;

/// <summary>
/// Resolves the authenticated principal to a local <see cref="User"/> and pins it on the request-scoped
/// <see cref="CurrentUserAccessor"/> that <see cref="CarTrackerDbContext"/>'s vehicle query filter reads.
/// </summary>
/// <remarks>
/// <para>
/// Runs <b>after</b> authorization, so both the web principal (the Auth0 JWT, established by the fallback
/// policy) and the MCP principal (the assistant token, established by the <c>McpRead</c> policy) are populated
/// by the time it runs — the two paths carry the owner differently and this is the one place that reconciles
/// them.
/// </para>
/// <list type="bullet">
/// <item>Auth0 principal → look up the <see cref="User"/> by its <c>sub</c> claim, provisioning one on first
/// sight. The very first user to ever arrive adopts every pre-multi-user (unowned) vehicle.</item>
/// <item>Assistant token → read the owner the token already carries (<see cref="AssistantClaims.UserId"/>).</item>
/// <item>Anything else (API key, anonymous) → no resolved user, which the filter reads as "no vehicles".</item>
/// </list>
/// </remarks>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        CurrentUserAccessor accessor,
        CarTrackerDbContext db,
        TimeProvider clock)
    {
        var principal = context.User;

        if (principal.Identity?.IsAuthenticated != true)
        {
            // Anonymous (e.g. /api/meta). Not a system context — resolves to no vehicles, though anonymous
            // endpoints do not query them.
            accessor.SetOwner(null);
        }
        else if (int.TryParse(principal.FindFirst(AssistantClaims.UserId)?.Value, out var tokenOwnerId))
        {
            accessor.SetOwner(tokenOwnerId);
        }
        else if ((principal.FindFirst("sub") ?? principal.FindFirst(ClaimTypes.NameIdentifier)) is { Value: var sub })
        {
            accessor.SetOwner(await ResolveAuth0UserAsync(db, clock, principal, sub, context.RequestAborted));
        }
        else
        {
            // Authenticated but neither an Auth0 subject nor an owned token — an API-key principal. No vehicles.
            accessor.SetOwner(null);
        }

        await next(context);
    }

    private static async Task<int> ResolveAuth0UserAsync(
        CarTrackerDbContext db,
        TimeProvider clock,
        ClaimsPrincipal principal,
        string sub,
        CancellationToken cancellationToken)
    {
        var existing = await db.Users.SingleOrDefaultAsync(u => u.ExternalId == sub, cancellationToken);
        if (existing is not null) return existing.Id;

        // Access tokens carry `sub` always; email/name only when the tenant is configured to add them (an Auth0
        // Action). Fall back to the subject so Email — which is required — is never empty.
        var email = principal.FindFirst("email")?.Value ?? principal.FindFirst(ClaimTypes.Email)?.Value ?? sub;
        var name = principal.FindFirst("name")?.Value ?? principal.FindFirst(ClaimTypes.Name)?.Value;

        var user = new User
        {
            ExternalId = sub,
            Email = email,
            DisplayName = name,
            CreatedAt = clock.GetUtcNow(),
        };
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost a race to create the same subject — the other request's row wins; use it.
            db.Entry(user).State = EntityState.Detached;
            return (await db.Users.SingleAsync(u => u.ExternalId == sub, cancellationToken)).Id;
        }

        // The first user to ever sign in adopts the pre-multi-user vehicles (the founding BT53). Guarded by the
        // count so a later user never sweeps up someone else's unowned rows; IgnoreQueryFilters because the
        // owner is not set yet and the filter would otherwise hide the very rows being claimed.
        if (await db.Users.CountAsync(cancellationToken) == 1)
        {
            await db.Vehicles.IgnoreQueryFilters()
                .Where(v => v.OwnerId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.OwnerId, user.Id), cancellationToken);
        }

        return user.Id;
    }
}
