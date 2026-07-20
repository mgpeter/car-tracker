using System.Security.Cryptography;
using CarTracker.Data;
using CarTracker.Shared;
using CarTracker.WebApi.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>
/// The ASSISTANT ACCESS panel's API — create, list and revoke the scoped MCP tokens, and read the write-audit
/// trail. Guarded by the web front-end's <c>X-Api-Key</c> (the fallback policy), because this is the owner
/// managing the assistant's keys from Settings, not the assistant itself (README §5.1).
/// </summary>
public static class AssistantEndpoints
{
    public static IEndpointRouteBuilder MapAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/assistant").WithTags("Assistant access");

        group.MapGet("/tokens", ListTokensAsync)
            .WithName("ListAssistantTokens")
            .WithSummary("Every assistant token, with its scope, last-used time and usage counts. Never the secret.");

        group.MapPost("/tokens", CreateTokenAsync)
            .WithName("CreateAssistantToken")
            .WithSummary("Creates a scoped token and returns its secret ONCE. Store it now — it is not recoverable.");

        group.MapDelete("/tokens/{id:int}", RevokeTokenAsync)
            .WithName("RevokeAssistantToken")
            .WithSummary("Revokes a token. It authenticates nothing afterwards; the row stays for the audit trail.");

        group.MapGet("/audit", ListAuditAsync)
            .WithName("ListAssistantAudit")
            .WithSummary("The write-audit trail — every change the assistant made, newest first. Reads are counted on the token, not listed.");

        return app;
    }

    private static async Task<Ok<List<AssistantTokenView>>> ListTokensAsync(
        CarTrackerDbContext context, CancellationToken cancellationToken)
    {
        var tokens = await context.AssistantTokens
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new AssistantTokenView(
                t.Id, t.Name, t.Scope, t.CreatedAt, t.LastUsedAt, t.RevokedAt, t.ReadCount, t.WriteCount))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(tokens);
    }

    private static async Task<Results<Created<CreatedTokenResponse>, ValidationProblem>> CreateTokenAsync(
        CreateAssistantTokenRequest request, CarTrackerDbContext context, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["A token needs a name so it can be recognised to revoke it."],
            });
        }

        // A 256-bit URL-safe secret, shown once. The client stores it; we store only its hash.
        var secret = "ct_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var token = new AssistantToken
        {
            Name = request.Name.Trim(),
            TokenHash = AssistantTokenHasher.Hash(secret),
            Scope = request.Scope,
            CreatedAt = timeProvider.GetUtcNow(),
        };

        context.AssistantTokens.Add(token);
        await context.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"/api/assistant/tokens/{token.Id}",
            new CreatedTokenResponse(token.Id, token.Name, token.Scope, secret));
    }

    private static async Task<Results<NoContent, NotFound<ProblemDetails>>> RevokeTokenAsync(
        int id, CarTrackerDbContext context, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var token = await context.AssistantTokens.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (token is null)
        {
            return TypedResults.NotFound(new ProblemDetails
            {
                Title = "Token not found",
                Detail = $"No assistant token {id}.",
                Status = StatusCodes.Status404NotFound,
            });
        }

        token.RevokedAt ??= timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<AssistantAuditView>>> ListAuditAsync(
        CarTrackerDbContext context, CancellationToken cancellationToken, int limit = 100)
    {
        var audits = await context.AssistantWriteAudits
            .AsNoTracking()
            .OrderByDescending(a => a.TimestampUtc)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(a => new AssistantAuditView(a.Id, a.TokenId, a.Tool, a.VehicleId, a.Summary, a.TimestampUtc))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(audits);
    }
}

public sealed record AssistantTokenView(
    int Id,
    string Name,
    AssistantScope Scope,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt,
    int ReadCount,
    int WriteCount);

public sealed record CreateAssistantTokenRequest(string Name, AssistantScope Scope = AssistantScope.ReadOnly);

/// <param name="Secret">Shown once. It is not stored and cannot be recovered — copy it now.</param>
public sealed record CreatedTokenResponse(int Id, string Name, AssistantScope Scope, string Secret);

public sealed record AssistantAuditView(int Id, int TokenId, string Tool, int? VehicleId, string Summary, DateTimeOffset TimestampUtc);
