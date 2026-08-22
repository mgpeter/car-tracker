using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.Domain.Accounts;
using Microsoft.EntityFrameworkCore;

namespace CarTracker.Chat;

/// <summary>Why a turn was refused, and when it stops being refused.</summary>
/// <param name="Scope">Whose ceiling it was — the words the 429 says back to the owner.</param>
public sealed record ChatBudgetRefusal(string Scope, long Spent, long Limit, DateTimeOffset ResetsAt);

/// <summary>
/// The spending guard, asked before every model call and told what each one cost.
/// </summary>
/// <remarks>
/// An interface because the loop tests script the model and must not need a database to assert that a refused
/// turn makes no request — and because "the chat is off for this account" is a policy, which is easier to be
/// sure about when it can be stated in one line of a fake.
/// </remarks>
public interface IChatBudget
{
    /// <summary>
    /// Null when the turn may proceed.
    /// </summary>
    /// <exception cref="ChatNotEntitledException">
    /// The account's plan does not include the assistant at all. Thrown rather than returned, because it is a
    /// different answer from "spent": there is no figure to report and no reset to wait for, and rendering it
    /// as a budget refusal would tell somebody to come back tomorrow for a feature they do not have.
    /// </exception>
    Task<ChatBudgetRefusal?> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Records what a completed turn cost. Never refuses — the turn already happened.</summary>
    Task RecordAsync(ChatTurnUsage usage, CancellationToken cancellationToken = default);
}

/// <summary>
/// The daily ceiling, per account and across the deployment, kept in the database.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two ceilings, because they answer different fears.</b> The per-owner one stops a single account running up
/// a bill on its own; the global one stops a deployment doing it collectively, which the per-owner limit cannot
/// bound without knowing how many accounts there will be. Either can refuse alone.
/// </para>
/// <para>
/// <b>Zero means off</b>, the same fail-safe direction the sign-up allowlist takes and the opposite of the
/// natural reading — so it is stated in <c>.env.example</c>, the README and this comment. A misread that turns
/// the chat off costs a support question; the other misread costs money.
/// </para>
/// <para>
/// The owner comes from <see cref="ICurrentUserAccessor"/>, the same accessor the vehicle query filter reads,
/// so a turn cannot be charged to one account while reading another's data. <b>No resolved owner refuses</b>:
/// an unattributable turn is one nobody is accountable for.
/// </para>
/// </remarks>
public sealed class ChatBudget(
    CarTrackerDbContext context,
    ChatSettings settings,
    ICurrentUserAccessor currentUser,
    IAccountEntitlements entitlements,
    Clock clock) : IChatBudget
{
    public async Task<ChatBudgetRefusal?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var today = clock.Today();
        var resetsAt = clock.StartOfNextDay();

        if (currentUser.OwnerId is not { } ownerId)
        {
            return new ChatBudgetRefusal("account", 0, 0, resetsAt);
        }

        // Asked before the ledger is read, and before the model is called. A plan without the assistant is not
        // an exhausted allowance - see ChatNotEntitledException.
        var allowances = await entitlements.AllowancesAsync(cancellationToken);

        if (!allowances.ChatEnabled) throw new ChatNotEntitledException();

        // The plan's own ceiling where it names one; otherwise the deployment's, which is the key an operator
        // already sets and the only one the paid tier reads. Two sections naming one ceiling is how the two
        // come to disagree, so the paid tier deliberately names none.
        var ceiling = allowances.DailyChatTokens ?? settings.PerOwnerCeiling;

        if (ceiling <= 0)
        {
            // Off, not exhausted. Reported as a spend of zero against a limit of zero, which is exactly what it
            // is, and the endpoint says so rather than implying tomorrow will be different.
            return new ChatBudgetRefusal("account", 0, 0, resetsAt);
        }

        var mine = await context.ChatUsage
            .Where(u => u.OwnerId == ownerId && u.Day == today)
            .Select(u => u.InputTokens + u.OutputTokens + u.CacheWriteTokens + u.CacheReadTokens)
            .SingleOrDefaultAsync(cancellationToken);

        if (mine >= ceiling)
        {
            return new ChatBudgetRefusal("account", mine, ceiling, resetsAt);
        }

        if (settings.GlobalCeiling <= 0) return null;

        var everyone = await context.ChatUsage
            .Where(u => u.Day == today)
            .SumAsync(u => u.InputTokens + u.OutputTokens + u.CacheWriteTokens + u.CacheReadTokens, cancellationToken);

        return everyone >= settings.GlobalCeiling
            ? new ChatBudgetRefusal("deployment", everyone, settings.GlobalCeiling, resetsAt)
            : null;
    }

    public async Task RecordAsync(ChatTurnUsage usage, CancellationToken cancellationToken = default)
    {
        // Unreachable behind CheckAsync, which refuses an unattributable turn outright. Guarded anyway, because
        // the alternative to skipping the record is charging one account for another's turn.
        if (currentUser.OwnerId is not { } ownerId) return;

        var today = clock.Today();

        var row = await context.ChatUsage
            .SingleOrDefaultAsync(u => u.OwnerId == ownerId && u.Day == today, cancellationToken);

        if (row is null)
        {
            row = new ChatUsage { OwnerId = ownerId, Day = today };
            context.ChatUsage.Add(row);
        }

        row.InputTokens += usage.InputTokens;
        row.OutputTokens += usage.OutputTokens;
        row.CacheWriteTokens += usage.CacheWriteTokens;
        row.CacheReadTokens += usage.CacheReadTokens;
        row.Turns++;

        await context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Thrown instead of calling the model when a daily ceiling is reached. The endpoints render it as 429.
/// </summary>
/// <remarks>
/// An exception rather than a returned outcome, deliberately: it is raised inside
/// <see cref="ChatConversationService.ContinueAsync"/>, which every path into the loop goes through, so a new
/// endpoint cannot forget to check the budget the way it could forget to read a flag.
/// </remarks>
public sealed class ChatBudgetExceededException(ChatBudgetRefusal refusal)
    : Exception($"The {refusal.Scope} daily chat allowance is spent ({refusal.Spent:N0} of {refusal.Limit:N0} tokens).")
{
    public ChatBudgetRefusal Refusal { get; } = refusal;
}

/// <summary>
/// Thrown instead of calling the model when the account's plan does not include the assistant.
/// </summary>
/// <remarks>
/// <para>
/// <b>A different refusal from an exhausted allowance, and it must stay different.</b> A spent budget has a
/// figure, a ceiling and a reset time, and "come back tomorrow" is true. This one has none of those: nothing
/// resets, and the answer is a plan rather than a clock. Collapsing the two would send somebody back every
/// morning to a feature they were never going to get.
/// </para>
/// <para>
/// Raised from the same choke point as the budget refusal - <see cref="ChatConversationService.ContinueAsync"/>,
/// which every path into the loop passes through - so a new endpoint cannot forget it the way it could forget
/// to read a flag. <b>Defence in depth rather than the primary control</b>: the entry point is not rendered for
/// an unentitled account at all, so reaching this means somebody called the API directly.
/// </para>
/// </remarks>
public sealed class ChatNotEntitledException()
    : Exception("This account's plan does not include the assistant.");
