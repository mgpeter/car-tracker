using System.Security.Cryptography;
using CarTracker.Data;
using Microsoft.Extensions.Caching.Memory;

namespace CarTracker.Domain.Accounts.Import;

/// <summary>What the server is holding between a preview and its commit.</summary>
/// <param name="OwnerId">Who it was previewed for. An id belonging to anyone else does not exist.</param>
/// <param name="Payload">
/// The parsed file. <b>The commit does not re-send it</b>, which is the whole point: a commit carrying its own
/// payload would validate the request against itself and could write something the preview never described.
/// </param>
/// <param name="Preview">
/// What the person was shown. The commit's decisions refer to vehicles by their index in it, and the
/// proposals it holds are the defaults an override replaces.
/// </param>
public sealed record PendingImport(int OwnerId, ImportPayload Payload, ImportPreview Preview);

/// <summary>
/// The server-held half of preview-then-commit: an opaque id, and the file it stands for.
/// </summary>
/// <remarks>
/// <para>
/// <c>PendingWriteStore</c> from the chat, with the same two rules. The id is unguessable rather than
/// sequential, because the owner check is the guard and an id that leaks how many imports a deployment has
/// seen is an invitation to try the neighbouring ones. And a foreign id is <c>null</c> rather than a distinct
/// refusal, so it presents exactly as an expired or invented one - telling them apart would confirm the id is
/// real.
/// </para>
/// <para>
/// <b>In memory, and the contrast with <c>chat_usage</c> is the point.</b> That needed a table because
/// Watchtower recreates the container minutes after every release and an in-memory counter would hand out a
/// fresh daily allowance. A lost preview costs a re-upload. State is persisted where losing it is expensive,
/// not everywhere - and the front end degrades an expired preview to "upload it again" rather than to a dead
/// button, which is what makes memory the cheap choice rather than the careless one.
/// </para>
/// <para>
/// <b>Fifteen minutes, not the chat's ten.</b> A chat draft is about a screen the owner is looking at now; an
/// import preview is a panel someone reads, checks three proposed registrations against their own garage, and
/// possibly edits. The extra five minutes are for the reading.
/// </para>
/// </remarks>
public sealed class PendingImportStore(IMemoryCache cache)
{
    /// <summary>How long a preview stays committable.</summary>
    public static TimeSpan Lifetime { get; } = TimeSpan.FromMinutes(15);

    public string Remember(PendingImport pending)
    {
        var id = "imp_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

        cache.Set(id, pending, Lifetime);

        return id;
    }

    /// <summary>
    /// The file behind an id, or null when there is none - <b>including when it belongs to someone else</b>.
    /// </summary>
    public PendingImport? Find(string id, ICurrentUserAccessor currentUser) =>
        cache.TryGetValue(id, out PendingImport? pending)
        && pending is not null
        && currentUser.OwnerId is { } ownerId
        && pending.OwnerId == ownerId
            ? pending
            : null;

    /// <summary>
    /// Drops a preview once it has been imported.
    /// </summary>
    /// <remarks>
    /// Called on success only. A correctable refusal - an override registration that collides - leaves the id
    /// standing, so fixing one plate does not cost a re-upload of the whole file.
    /// </remarks>
    public void Forget(string id) => cache.Remove(id);
}
