using CarTracker.Chat;
using CarTracker.Data;
using CarTracker.Domain.Accounts;
using CarTracker.Domain.Admin;
using CarTracker.Domain.Documents;
using CarTracker.Domain.Lookup;
using CarTracker.Shared;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.WebApi.Endpoints;

/// <summary>The operator surface (DEC-023). Counts, aggregates, posture, and one write.</summary>
/// <remarks>
/// <para>
/// <b>The group carries <c>.RequireAuthorization("AdminRead")</c>, which is the first explicit
/// <c>RequireAuthorization</c> in this codebase.</b> Every other group rides the global fallback policy and
/// the only existing overrides are <c>.AllowAnonymous()</c>. Said here so a later reader takes it as a
/// deliberate exception rather than as a pattern to copy onto groups that do not need one.
/// </para>
/// <para>
/// An assistant-token bearer gets <b>401, not 403</b>. The admin policies name the <c>Auth0</c> scheme only, so
/// the JwtBearer handler challenges at the door rather than the request reaching a policy that would refuse
/// it - the behaviour <see cref="AccountEndpoints"/> documents, kept identical because widening a scheme
/// purely so a credential can be refused more politely is a bad trade.
/// </para>
/// <para>
/// <b>Nothing here reads anybody's records.</b> No fuel log, no service history, no document, no anomaly
/// message, no chat transcript, and no full registration. That is a boundary rather than an omission: an
/// operator screen that can read one account's logs is a tool for reading other people's data wearing an
/// operations badge, and refusing it while nobody has asked costs nothing.
/// </para>
/// </remarks>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin")
            .WithTags("Admin")
            .RequireAuthorization("AdminRead");

        group.MapGet("/users", async (
                AdminReadService admin,
                CancellationToken cancellationToken,
                int? limit = null) =>
            {
                return await admin.ListAccountsAsync(limit ?? 200, cancellationToken);
            })
            .WithName("ListAdminUsers")
            .WithSummary("Every account, newest first. Unpaged and clamped at 500; the response says which.");

        group.MapGet("/users/{id:int}", async Task<Results<Ok<AdminUserDetail>, NotFound>> (
                int id,
                AdminReadService admin,
                CancellationToken cancellationToken) =>
            {
                var detail = await admin.AccountAsync(id, cancellationToken);
                return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail);
            })
            .WithName("GetAdminUser")
            .WithSummary("One account: its vehicles, token spend by day, assistant tokens and open anomalies by kind.");

        group.MapGet("/usage", async (
                AdminReadService admin,
                ChatSettings chat,
                CancellationToken cancellationToken,
                int? days = null) =>
            {
                var usage = await admin.UsageAsync(days ?? 30, cancellationToken: cancellationToken);

                // The ceilings ride with the figures so the numerator and its denominator come from one place.
                // Resolved rather than sent as the nullable they are stored as, which is the rule
                // MetaEndpoints already applies to the per-account allowance: what a client needs is a number.
                return new AdminUsageResponse(
                    usage, chat.PerOwnerCeiling, chat.GlobalCeiling, chat.IsConfigured);
            })
            .WithName("GetAdminUsage")
            .WithSummary("Deployment-wide token spend against the ceilings actually in force, plus a daily series.");

        group.MapGet("/diagnostics", async (
                AdminReadService admin,
                SignupPolicy signup,
                PlanOptions plans,
                ChatSettings chat,
                VehicleLookupOptions lookup,
                IIdentityProviderClient identity,
                OwnershipOptions ownership,
                DocumentStorageOptions documents,
                CarTracker.Domain.Legal.LegalOptions legal,
                TimeProvider clock,
                CancellationToken cancellationToken) =>
            {
                var database = await admin.DatabaseDiagnosticsAsync(cancellationToken);
                var comped = new EmailAllowlist(plans.CompEmails, plans.CompDomains);

                return new AdminDiagnosticsResponse(
                    Version: BuildInfo.Version,
                    Environment: System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown",
                    ServerTimeUtc: clock.GetUtcNow(),
                    TimeZone: TimeZoneInfo.Local.Id,
                    Signup: new AdminSignupPosture(
                        signup.Mode.ToString(),
                        signup.AllowedEmailCount,
                        signup.AllowedDomainCount,
                        signup.IsClosed,
                        // The same condition the 0.24.1 boot warning reports: the door is open and an
                        // invitation list is being read for nothing. It is the shape every pre-0.24.0
                        // deployment arrives in, because a populated allowlist was the door until then.
                        AllowlistIsInert: signup.Mode is SignupMode.Open
                                          && (signup.AllowedEmailCount > 0 || signup.AllowedDomainCount > 0)),
                    Plans: new AdminPlanPosture(
                        comped.EmailCount,
                        comped.DomainCount,
                        Allowances(plans, AccountPlan.Free, chat),
                        Allowances(plans, AccountPlan.Pro, chat)),
                    Chat: new AdminChatPosture(
                        chat.IsConfigured, chat.Model, chat.PerOwnerCeiling, chat.GlobalCeiling),
                    Lookup: new AdminLookupPosture(lookup.IsConfigured, lookup.IsMotConfigured),
                    Identity: new AdminIdentityPosture(identity.IsConfigured, database.PendingIdentityDeletions),
                    Ownership: new AdminOwnershipPosture(
                        !string.IsNullOrWhiteSpace(ownership.ClaimUnownedVehiclesFor)),
                    // The same condition the boot line warns about: open to strangers and publishing nothing
                    // about what happens to their data. Reported here too because a boot line helps only
                    // somebody already reading logs, which is the lesson 0.24.1 exists to record.
                    Legal: new AdminLegalPosture(
                        legal.IsPublished,
                        legal.Publication?.ControllerName,
                        legal.Publication?.ControllerAddress is not null,
                        legal.Publication?.HostingSummary is not null,
                        legal.ResolvedJurisdiction,
                        UnpublishedToStrangers: signup.Mode is SignupMode.Open && !legal.IsPublished),
                    Documents: DocumentsPosture(documents),
                    Database: new AdminDatabasePosture(
                        database.LastAppliedMigration, database.PendingMigrationCount));
            })
            .WithName("GetAdminDiagnostics")
            .WithSummary("What this container actually resolved. Booleans and counts; never a secret.");

        // Both plan routes additionally require admin:plan:write. Group and route policies combine with AND,
        // so a caller needs both permissions - deliberate, because an account that may change a plan may
        // certainly see the account it is changing, and requiring both means admin:plan:write alone is not a
        // back door into an id that was never listed.
        group.MapPut("/users/{id:int}/plan", async (
                int id,
                SetPlanOverrideRequest request,
                CarTrackerDbContext db,
                AdminReadService admin,
                ClaimsPrincipal actor,
                ILoggerFactory loggers,
                CancellationToken cancellationToken) =>
                await WritePlanAsync(id, request.Plan, db, admin, actor, loggers, cancellationToken))
            .RequireAuthorization("AdminPlanWrite")
            .WithName("SetAdminUserPlan")
            .WithSummary("Pin an account to a tier. Accepts Free as well as Pro - see the API spec for why.");

        group.MapDelete("/users/{id:int}/plan", async (
                int id,
                CarTrackerDbContext db,
                AdminReadService admin,
                ClaimsPrincipal actor,
                ILoggerFactory loggers,
                CancellationToken cancellationToken) =>
                await WritePlanAsync(id, null, db, admin, actor, loggers, cancellationToken))
            .RequireAuthorization("AdminPlanWrite")
            .WithName("ClearAdminUserPlan")
            .WithSummary("Clear the override so the account falls back to the comp list.");

        return app;
    }

    /// <summary>Sets or clears the override and returns the account as it now resolves.</summary>
    /// <remarks>
    /// Returns the row rather than 204 on both verbs, because the caller wants to see what the account
    /// resolved to <i>after</i> the change and a second request to find out would be a race with nothing.
    /// <para>
    /// <b>Nothing needs invalidating.</b> <see cref="AccountEntitlements"/> caches for the life of one request,
    /// so the target account's very next request resolves the new tier. That is the whole benefit over a config
    /// key and a restart, and it falls out of derive-on-read rather than being built.
    /// </para>
    /// </remarks>
    private static async Task<Results<Ok<AdminUserRow>, NotFound>> WritePlanAsync(
        int id,
        AccountPlan? plan,
        CarTrackerDbContext db,
        AdminReadService admin,
        ClaimsPrincipal actor,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null) return TypedResults.NotFound();

        // A DELETE against an account holding no override deleted nothing, and reporting success for that is
        // how a screen comes to show a state the server does not hold.
        if (plan is null && user.PlanOverride is null) return TypedResults.NotFound();

        var before = user.PlanOverride;
        user.PlanOverride = plan;
        await db.SaveChangesAsync(cancellationToken);

        // Logged, not audited, and the difference is worth naming: with one administrator a structured line
        // carrying who/what/before/after is proportionate. The second administrator is the point at which this
        // stops being an audit trail, and nothing will announce that moment (DEC-023).
        loggers.CreateLogger(typeof(AdminEndpoints)).LogInformation(
            "Admin {Actor} set plan override for account {UserId} from {Before} to {After}",
            actor.FindFirst("sub")?.Value ?? "(unknown)", id, before?.ToString() ?? "none", plan?.ToString() ?? "none");

        var detail = await admin.AccountAsync(id, cancellationToken);
        return detail is null ? TypedResults.NotFound() : TypedResults.Ok(detail.Account);
    }

    private static AdminPlanAllowances Allowances(PlanOptions plans, AccountPlan plan, ChatSettings chat)
    {
        var allowances = PlanResolver.Allowances(plans, plan);

        return new AdminPlanAllowances(
            allowances.ChatEnabled,
            allowances.DailyChatTokens ?? chat.PerOwnerCeiling,
            allowances.MaxDocuments,
            allowances.DailyVehicleLookups);
    }

    /// <summary>
    /// Whether the documents volume is actually there and writable, which a restore that forgot it is not.
    /// </summary>
    /// <remarks>
    /// DEC-020 names this as one of two things a shared host can break that this app cannot check for itself.
    /// It can check this one, so it does. The probe writes and deletes a temporary file rather than reading a
    /// permission bit, because a bind mount can be present, readable and read-only.
    /// </remarks>
    private static AdminDocumentsPosture DocumentsPosture(DocumentStorageOptions documents)
    {
        var exists = Directory.Exists(documents.RootPath);
        var writable = false;

        if (exists)
        {
            try
            {
                var probe = Path.Combine(documents.RootPath, $".write-probe-{Guid.NewGuid():N}");
                File.WriteAllBytes(probe, []);
                File.Delete(probe);
                writable = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                writable = false;
            }
        }

        return new AdminDocumentsPosture(documents.RootPath, exists, writable);
    }
}

/// <param name="Plan">The tier to pin the account to. <c>Free</c> is accepted and meaningful.</param>
public sealed record SetPlanOverrideRequest(AccountPlan Plan);

public sealed record AdminUsageResponse(
    AdminUsage Usage,
    long PerOwnerTokenCeiling,
    long DailyTokenCeiling,
    bool ChatConfigured);

public sealed record AdminSignupPosture(
    string Mode,
    int AllowedEmailCount,
    int AllowedDomainCount,
    bool IsClosed,
    bool AllowlistIsInert);

public sealed record AdminPlanAllowances(
    bool ChatEnabled,
    long DailyChatTokens,
    int MaxDocuments,
    int DailyVehicleLookups);

/// <param name="CompEmailCount">
/// A count, not the addresses. The user list already shows which accounts resolved to which plan and why,
/// which answers "is my address comped" better than printing the list would.
/// </param>
public sealed record AdminPlanPosture(
    int CompEmailCount,
    int CompDomainCount,
    AdminPlanAllowances Free,
    AdminPlanAllowances Pro);

public sealed record AdminChatPosture(bool Configured, string Model, long DailyTokensPerOwner, long DailyTokensGlobal);

public sealed record AdminLookupPosture(bool VesConfigured, bool MotConfigured);

public sealed record AdminIdentityPosture(bool ManagementConfigured, int PendingIdentityDeletions);

public sealed record AdminOwnershipPosture(bool ClaimUnownedVehiclesForConfigured);

/// <summary>
/// Whether this deployment publishes legal documents, and who they name (DEC-024).
/// </summary>
/// <remarks>
/// The controller's name is published to every stranger who opens <c>/privacy</c>, so naming it on an operator
/// surface discloses nothing new. The contact address is deliberately absent all the same: it is on the public
/// page for anyone who wants it, and this response's rule is that it carries the posture rather than the
/// content.
/// </remarks>
public sealed record AdminLegalPosture(
    bool Published,
    string? ControllerName,
    bool HasPostalAddress,
    bool HasHostingSummary,
    string Jurisdiction,
    bool UnpublishedToStrangers);

public sealed record AdminDocumentsPosture(string RootPath, bool Exists, bool Writable);

public sealed record AdminDatabasePosture(string? LastAppliedMigration, int PendingMigrationCount);

/// <summary>Every credential reduced to a boolean. No key, secret or connection string appears here.</summary>
public sealed record AdminDiagnosticsResponse(
    string Version,
    string Environment,
    DateTimeOffset ServerTimeUtc,
    string TimeZone,
    AdminSignupPosture Signup,
    AdminPlanPosture Plans,
    AdminChatPosture Chat,
    AdminLookupPosture Lookup,
    AdminIdentityPosture Identity,
    AdminOwnershipPosture Ownership,
    AdminLegalPosture Legal,
    AdminDocumentsPosture Documents,
    AdminDatabasePosture Database);
