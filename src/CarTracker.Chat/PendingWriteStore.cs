using System.Security.Cryptography;
using CarTracker.Data;
using Microsoft.Extensions.Caching.Memory;

namespace CarTracker.Chat;

/// <summary>What the server remembers about a write it has proposed but not performed.</summary>
/// <param name="OwnerId">Who it was proposed to. An id belonging to anyone else does not exist.</param>
/// <param name="ToolCallId">The suspension in the transcript this answers.</param>
/// <param name="Tool">The tool that will run. <b>Read from here, never from the request.</b></param>
/// <param name="Vehicle">The vehicle in scope when it was proposed, for the panel to show.</param>
public sealed record PendingWriteRecord(int OwnerId, string ToolCallId, string Tool, string? Vehicle);

/// <summary>
/// The server-held half of confirm-before-write: an opaque id, and what it stands for.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the whole authorisation.</b> An earlier revision of the spec matched a client-supplied id against
/// a <c>tool_use</c> block in the client-supplied transcript and called that a guard — it validated the request
/// against itself, so a crafted POST could invent an assistant turn proposing <c>delete_service</c> and confirm
/// it. The tool name lives here, the request has no field for it, and the transcript is treated as what it is:
/// untrusted input that is replayed to the model and authorises nothing.
/// </para>
/// <para>
/// <b>Ten minutes, and expiry is a refusal rather than a silent re-proposal.</b> A draft the owner last looked
/// at ten minutes ago is a draft about a screen they have left; re-running it because the id still parses would
/// write something nobody currently means.
/// </para>
/// <para>
/// In memory, unlike the spending ledger, and the difference is deliberate: a restart that forgets a
/// half-finished draft costs the owner one repeated sentence, while a restart that forgets a day's spending
/// costs money. State is persisted where losing it is expensive, not everywhere.
/// </para>
/// </remarks>
public sealed class PendingWriteStore(IMemoryCache cache)
{
    /// <summary>How long a draft stays confirmable.</summary>
    public static TimeSpan Lifetime { get; } = TimeSpan.FromMinutes(10);

    public string Remember(PendingWriteRecord record)
    {
        // Opaque and unguessable rather than sequential: the owner check is the guard, and an id that leaks how
        // many writes the deployment has proposed is an invitation to try the neighbouring ones.
        var id = "pw_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

        cache.Set(id, record, Lifetime);

        return id;
    }

    /// <summary>
    /// The record behind an id, or null when there is none — <b>including when it belongs to someone else</b>.
    /// </summary>
    /// <remarks>
    /// A foreign id is null rather than a distinct refusal, so it presents exactly as an expired or invented
    /// one. That is the same shape a cross-owner vehicle takes: not found, because for this account it is not.
    /// </remarks>
    public PendingWriteRecord? Find(string id, ICurrentUserAccessor currentUser) =>
        cache.TryGetValue(id, out PendingWriteRecord? record)
        && record is not null
        && currentUser.OwnerId is { } ownerId
        && record.OwnerId == ownerId
            ? record
            : null;

    /// <summary>Drops a draft once it has been answered. A suspension is answered once or not at all.</summary>
    public void Forget(string id) => cache.Remove(id);
}
