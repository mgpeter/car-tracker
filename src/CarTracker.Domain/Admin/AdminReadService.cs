using CarTracker.Data;
using CarTracker.Domain.Accounts;
using CarTracker.Shared;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Domain.Admin;

/// <summary>Reads across every account, for the operator surface and nothing else.</summary>
/// <remarks>
/// <para>
/// <b>This is the only file permitted to call <c>IgnoreQueryFilters()</c> for the admin surface, and that is a
/// rule rather than an observation.</b> An administrator's request is provisioned exactly like anyone else's,
/// so <see cref="ICurrentUserAccessor"/> is pinned to <i>their</i> owner id and the vehicle query filter is
/// live. Every cross-owner query has to opt out of it. Keeping the opt-outs in one class means the widening is
/// one reviewable file rather than a habit spreading through handlers, and a diff that adds
/// <c>IgnoreQueryFilters()</c> anywhere else is a diff worth stopping.
/// </para>
/// <para>
/// <b>Deliberately not <see cref="ICurrentUserAccessor.BypassOwnership"/>.</b> Setting it would widen the whole
/// request, including code with no idea it is running under an administrator, and it is a runtime flag rather
/// than a compile-time one so nothing would flag the reach. Targeted opt-outs on named queries stay visible.
/// </para>
/// <para>
/// <b>Counts and aggregates only.</b> Nothing here projects a log row, a document, an anomaly message or a
/// chat transcript, and the registrations it does return are masked. The surface is an operations tool, not a
/// way to read other people's records, and that boundary is cheap to hold now and expensive to recover later.
/// </para>
/// <para>
/// Which tables need the opt-out and which do not: <c>users</c>, <c>chat_usage</c>,
/// <c>vehicle_lookup_usage</c>, <c>assistant_tokens</c> and <c>assistant_write_audits</c> carry no filter and
/// are read directly. <c>vehicles</c> is filtered. <c>documents</c> and <c>data_anomalies</c> are unfiltered
/// but keyed by vehicle, so reaching an owner means joining to a vehicle - and the join side must ignore
/// filters too, or it contributes the predicate the join was meant to escape.
/// </para>
/// </remarks>
public sealed class AdminReadService(CarTrackerDbContext db, PlanOptions plans, Clock clock)
{
    /// <summary>The most accounts one call will return. Beyond this the list needs paging, which it has not got.</summary>
    public const int MaxAccounts = 500;

    private readonly EmailAllowlist _comped = new(plans.CompEmails, plans.CompDomains);

    /// <summary>Every account, newest first, with the figures the list renders.</summary>
    /// <remarks>
    /// Grouped queries stitched together in memory on the user id, never a loop issuing a query per account.
    /// At this scale the difference is invisible; written as a loop it becomes an N+1 nobody revisits until it
    /// is a hundred accounts and slow.
    /// </remarks>
    public async Task<AdminUserList> ListAccountsAsync(int limit, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit, 1, MaxAccounts);

        var total = await db.Users.CountAsync(cancellationToken);

        var users = await db.Users
            .OrderByDescending(u => u.CreatedAt)
            .ThenByDescending(u => u.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

        var ids = users.Select(u => u.Id).ToList();
        var facts = await FactsForAsync(ids, cancellationToken);

        return new AdminUserList(total, users.Count, users.Select(u => RowFor(u, facts)).ToList());
    }

    /// <summary>One account, or null when no such id exists.</summary>
    public async Task<AdminUserDetail?> AccountAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null) return null;

        var ids = new List<int> { userId };
        var facts = await FactsForAsync(ids, cancellationToken);

        var vehicles = await db.Vehicles
            .IgnoreQueryFilters()
            .Where(v => v.OwnerId == userId)
            .OrderBy(v => v.CreatedAt)
            .Select(v => new { v.Registration, v.Make, v.Model, v.Year, v.Status, v.IsDefault, v.CreatedAt })
            .ToListAsync(cancellationToken);

        var from = clock.Today().AddDays(-29);

        var usage = await db.ChatUsage
            .Where(c => c.OwnerId == userId && c.Day >= from)
            .OrderBy(c => c.Day)
            .Select(c => new AdminChatUsageDay(
                c.Day, c.InputTokens, c.OutputTokens, c.CacheReadTokens, c.CacheWriteTokens, c.Turns))
            .ToListAsync(cancellationToken);

        // The secret is not projected. AssistantToken.TokenHash never leaves the database on this surface,
        // which is the rule the account export set and there is no reason for an operator screen to relax it.
        var tokens = await db.AssistantTokens
            .Where(t => t.OwnerId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new AdminAssistantTokenRow(
                t.Name, t.Scope.ToString(), t.CreatedAt, t.LastUsedAt, t.ReadCount, t.WriteCount, t.RevokedAt))
            .ToListAsync(cancellationToken);

        var documentBytes = await db.Documents
            .Where(d => db.Vehicles.IgnoreQueryFilters().Any(v => v.Id == d.VehicleId && v.OwnerId == userId))
            .SumAsync(d => (long?)d.SizeBytes, cancellationToken) ?? 0L;

        // The kind, and deliberately not the message. A count by kind says whether the write paths are
        // misbehaving for somebody; the message would quote their data back at an operator who has no
        // business reading it.
        var anomalies = await db.DataAnomalies
            .Where(a => a.Status == AnomalyStatus.Open
                        && db.Vehicles.IgnoreQueryFilters().Any(v => v.Id == a.VehicleId && v.OwnerId == userId))
            .GroupBy(a => a.Kind)
            .Select(g => new { Kind = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new AdminUserDetail(
            RowFor(user, facts),
            vehicles.Select(v => new AdminVehicleRow(
                RegistrationMask.Mask(v.Registration), v.Make, v.Model, v.Year, v.Status, v.IsDefault, v.CreatedAt)).ToList(),
            usage,
            tokens,
            documentBytes,
            anomalies.ToDictionary(a => a.Kind.ToString(), a => a.Count));
    }

    /// <summary>Deployment-wide spend over the last <paramref name="days"/> days, and the totals beside it.</summary>
    public async Task<AdminUsage> UsageAsync(int days, int topAccounts = 10, CancellationToken cancellationToken = default)
    {
        var window = Math.Clamp(days, 1, 90);
        var today = clock.Today();
        var from = today.AddDays(-(window - 1));

        var rows = await db.ChatUsage
            .Where(c => c.Day >= from)
            .Select(c => new
            {
                c.Day, c.OwnerId, c.InputTokens, c.OutputTokens, c.CacheReadTokens, c.CacheWriteTokens, c.Turns,
            })
            .ToListAsync(cancellationToken);

        var todayRows = rows.Where(r => r.Day == today).ToList();

        var todayTotals = new AdminChatUsageDay(
            today,
            todayRows.Sum(r => r.InputTokens),
            todayRows.Sum(r => r.OutputTokens),
            todayRows.Sum(r => r.CacheReadTokens),
            todayRows.Sum(r => r.CacheWriteTokens),
            todayRows.Sum(r => r.Turns));

        var byDay = rows
            .GroupBy(r => r.Day)
            .OrderBy(g => g.Key)
            .Select(g => new AdminUsageDay(
                g.Key,
                g.Sum(r => r.InputTokens + r.OutputTokens + r.CacheReadTokens + r.CacheWriteTokens),
                g.Sum(r => r.Turns),
                g.Select(r => r.OwnerId).Distinct().Count()))
            .ToList();

        var spenders = rows
            .GroupBy(r => r.OwnerId)
            .Select(g => new
            {
                OwnerId = g.Key,
                Tokens = g.Sum(r => r.InputTokens + r.OutputTokens + r.CacheReadTokens + r.CacheWriteTokens),
            })
            .Where(s => s.Tokens > 0)
            .OrderByDescending(s => s.Tokens)
            .Take(Math.Max(1, topAccounts))
            .ToList();

        var spenderIds = spenders.Select(s => s.OwnerId).ToList();
        var addresses = await db.Users
            .Where(u => spenderIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

        var lookupsToday = await db.VehicleLookupUsage
            .Where(l => l.Day == today)
            .SumAsync(l => (int?)l.Lookups, cancellationToken) ?? 0;

        var totals = new AdminUsageTotals(
            await db.Users.CountAsync(cancellationToken),
            await db.Vehicles.IgnoreQueryFilters().CountAsync(cancellationToken),
            await db.Documents.CountAsync(cancellationToken),
            await db.Documents.SumAsync(d => (long?)d.SizeBytes, cancellationToken) ?? 0L,
            await db.AssistantTokens.CountAsync(t => t.RevokedAt == null, cancellationToken));

        return new AdminUsage(
            todayTotals,
            todayRows.Select(r => r.OwnerId).Distinct().Count(),
            byDay,
            spenders.Select(s => new AdminTopAccount(
                s.OwnerId, addresses.GetValueOrDefault(s.OwnerId, "(unknown)"), s.Tokens)).ToList(),
            lookupsToday,
            totals);
    }

    /// <summary>The database half of the posture. The configuration half is assembled at the endpoint.</summary>
    public async Task<AdminDatabaseDiagnostics> DatabaseDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault();
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).Count();

        return new AdminDatabaseDiagnostics(
            applied,
            pending,
            await db.PendingIdentityDeletions.CountAsync(cancellationToken));
    }

    /// <summary>Everything the list needs about a set of accounts, in one pass per table.</summary>
    private async Task<AccountFacts> FactsForAsync(List<int> ids, CancellationToken cancellationToken)
    {
        var today = clock.Today();
        var from = today.AddDays(-29);

        var vehicles = await db.Vehicles
            .IgnoreQueryFilters()
            .Where(v => v.OwnerId != null && ids.Contains(v.OwnerId.Value))
            .OrderBy(v => v.CreatedAt)
            .Select(v => new { OwnerId = v.OwnerId!.Value, v.Registration })
            .ToListAsync(cancellationToken);

        var chatToday = await db.ChatUsage
            .Where(c => c.Day == today && ids.Contains(c.OwnerId))
            .Select(c => new
            {
                c.OwnerId,
                Total = c.InputTokens + c.OutputTokens + c.CacheReadTokens + c.CacheWriteTokens,
            })
            .ToListAsync(cancellationToken);

        var chatWindow = await db.ChatUsage
            .Where(c => c.Day >= from && ids.Contains(c.OwnerId))
            .Select(c => new
            {
                c.OwnerId,
                Total = c.InputTokens + c.OutputTokens + c.CacheReadTokens + c.CacheWriteTokens,
                c.Turns,
            })
            .ToListAsync(cancellationToken);

        var documents = await db.Documents
            .Where(d => db.Vehicles.IgnoreQueryFilters()
                .Any(v => v.Id == d.VehicleId && v.OwnerId != null && ids.Contains(v.OwnerId.Value)))
            .Select(d => new
            {
                OwnerId = db.Vehicles.IgnoreQueryFilters()
                    .Where(v => v.Id == d.VehicleId).Select(v => v.OwnerId!.Value).First(),
            })
            .ToListAsync(cancellationToken);

        var anomalies = await db.DataAnomalies
            .Where(a => a.Status == AnomalyStatus.Open
                        && db.Vehicles.IgnoreQueryFilters()
                            .Any(v => v.Id == a.VehicleId && v.OwnerId != null && ids.Contains(v.OwnerId.Value)))
            .Select(a => new
            {
                OwnerId = db.Vehicles.IgnoreQueryFilters()
                    .Where(v => v.Id == a.VehicleId).Select(v => v.OwnerId!.Value).First(),
            })
            .ToListAsync(cancellationToken);

        var lookups = await db.VehicleLookupUsage
            .Where(l => l.Day == today && ids.Contains(l.OwnerId))
            .Select(l => new { l.OwnerId, l.Lookups })
            .ToListAsync(cancellationToken);

        var tokens = await db.AssistantTokens
            .Where(t => t.OwnerId != null && ids.Contains(t.OwnerId.Value) && t.RevokedAt == null)
            .Select(t => new { OwnerId = t.OwnerId!.Value })
            .ToListAsync(cancellationToken);

        return new AccountFacts(
            vehicles.GroupBy(v => v.OwnerId).ToDictionary(g => g.Key, g => g.Select(v => v.Registration).ToList()),
            chatToday.GroupBy(c => c.OwnerId).ToDictionary(g => g.Key, g => g.Sum(c => c.Total)),
            chatWindow.GroupBy(c => c.OwnerId).ToDictionary(g => g.Key, g => g.Sum(c => c.Total)),
            chatWindow.GroupBy(c => c.OwnerId).ToDictionary(g => g.Key, g => g.Sum(c => c.Turns)),
            documents.GroupBy(d => d.OwnerId).ToDictionary(g => g.Key, g => g.Count()),
            anomalies.GroupBy(a => a.OwnerId).ToDictionary(g => g.Key, g => g.Count()),
            lookups.GroupBy(l => l.OwnerId).ToDictionary(g => g.Key, g => g.Sum(l => l.Lookups)),
            tokens.GroupBy(t => t.OwnerId).ToDictionary(g => g.Key, g => g.Count()));
    }

    private AdminUserRow RowFor(User user, AccountFacts facts)
    {
        // The same resolver the entitlement path calls, so the plan an operator reads is the plan the account
        // holder is actually on. A second copy of the ladder here would be the drift this extraction exists to
        // prevent, and it would be invisible: both answers would look plausible.
        var resolution = PlanResolver.Resolve(
            _comped, user.PlanOverride, user.Email, user.ExternalId, user.EmailVerified);

        var registrations = facts.Registrations.GetValueOrDefault(user.Id) ?? [];

        return new AdminUserRow(
            user.Id,
            user.Email,
            user.EmailVerified,
            user.DisplayName,
            user.CreatedAt,
            user.LastSeenAt,
            resolution.Plan,
            resolution.Reason,
            user.PlanOverride,
            registrations.Count,
            registrations.Select(RegistrationMask.Mask).ToList(),
            facts.ChatToday.GetValueOrDefault(user.Id),
            facts.ChatWindow.GetValueOrDefault(user.Id),
            facts.ChatTurns.GetValueOrDefault(user.Id),
            facts.Documents.GetValueOrDefault(user.Id),
            facts.Lookups.GetValueOrDefault(user.Id),
            facts.Tokens.GetValueOrDefault(user.Id),
            facts.Anomalies.GetValueOrDefault(user.Id));
    }

    private sealed record AccountFacts(
        Dictionary<int, List<string>> Registrations,
        Dictionary<int, long> ChatToday,
        Dictionary<int, long> ChatWindow,
        Dictionary<int, int> ChatTurns,
        Dictionary<int, int> Documents,
        Dictionary<int, int> Anomalies,
        Dictionary<int, int> Lookups,
        Dictionary<int, int> Tokens);
}
