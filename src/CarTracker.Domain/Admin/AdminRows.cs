using CarTracker.Shared;

namespace CarTracker.Domain.Admin;

/// <summary>One account as the operator surface sees it.</summary>
/// <param name="PlanOverride">
/// Null when no administrator has decided anything. Carried beside <paramref name="PlanReason"/> rather than
/// inferred from it, so the screen can tell "on Pro because somebody said so" from "on Pro because the comp
/// list matches" without reading the reason and guessing.
/// </param>
/// <param name="MaskedRegistrations">
/// Never full plates - see <see cref="RegistrationMask"/>. The masking happens here rather than in the client
/// because a value the browser has to hide is a value that has already left the server.
/// </param>
public sealed record AdminUserRow(
    int Id,
    string Email,
    bool EmailVerified,
    string? DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSeenAt,
    AccountPlan Plan,
    Accounts.PlanReason PlanReason,
    AccountPlan? PlanOverride,
    int VehicleCount,
    IReadOnlyList<string> MaskedRegistrations,
    long ChatTokensToday,
    long ChatTokens30d,
    int ChatTurns30d,
    int DocumentCount,
    int VehicleLookupsToday,
    int AssistantTokenCount,
    int OpenAnomalyCount);

/// <param name="TotalAccounts">
/// The unclamped count. Sent beside <paramref name="Returned"/> so a truncated list says it is truncated
/// rather than reading as the whole population - the least a deliberately unpaged endpoint can do.
/// </param>
public sealed record AdminUserList(int TotalAccounts, int Returned, IReadOnlyList<AdminUserRow> Users);

/// <summary>A car, identified well enough for a support conversation and no better.</summary>
/// <remarks>
/// Make, model and year are here and the plate is not, which is the line this surface draws: the pair
/// identifies a car to the person who owns it without identifying it to the DVLA.
/// </remarks>
public sealed record AdminVehicleRow(
    string MaskedRegistration,
    string Make,
    string Model,
    int Year,
    VehicleStatus Status,
    bool IsDefault,
    DateTimeOffset CreatedAt);

/// <summary>One day on the chat ledger. Days with no rows are omitted rather than zero-filled.</summary>
public sealed record AdminChatUsageDay(
    DateOnly Day,
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    int Turns)
{
    public long TotalTokens => InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens;
}

/// <summary>An assistant token, without its secret. <c>TokenHash</c> is not projected and never will be.</summary>
public sealed record AdminAssistantTokenRow(
    string Name,
    string Scope,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    int ReadCount,
    int WriteCount,
    DateTimeOffset? RevokedAt);

/// <summary>One account in detail. Counts and aggregates only - no log row is reachable from here.</summary>
public sealed record AdminUserDetail(
    AdminUserRow Account,
    IReadOnlyList<AdminVehicleRow> Vehicles,
    IReadOnlyList<AdminChatUsageDay> ChatUsageByDay,
    IReadOnlyList<AdminAssistantTokenRow> AssistantTokens,
    long DocumentBytes,
    IReadOnlyDictionary<string, int> OpenAnomaliesByKind);

/// <summary>Deployment-wide spend for one day.</summary>
public sealed record AdminUsageDay(DateOnly Day, long TotalTokens, int Turns, int AccountsActive);

/// <summary>The accounts costing the most over the window, capped by the caller.</summary>
public sealed record AdminTopAccount(int UserId, string Email, long Tokens);

public sealed record AdminUsageTotals(
    int Accounts,
    int Vehicles,
    int Documents,
    long DocumentBytes,
    int AssistantTokens);

/// <summary>What this deployment is spending, which nothing in the application showed before 0.26.0.</summary>
public sealed record AdminUsage(
    AdminChatUsageDay Today,
    int AccountsActiveToday,
    IReadOnlyList<AdminUsageDay> ByDay,
    IReadOnlyList<AdminTopAccount> TopAccounts,
    int VehicleLookupsToday,
    AdminUsageTotals Totals);

/// <summary>The half of the posture that comes from the database rather than from configuration.</summary>
/// <remarks>
/// <c>LastAppliedMigration</c> is the fastest answer to "is this container running the code I think it is",
/// which is the question behind both incidents this endpoint exists for.
/// </remarks>
public sealed record AdminDatabaseDiagnostics(
    string? LastAppliedMigration,
    int PendingMigrationCount,
    int PendingIdentityDeletions);
