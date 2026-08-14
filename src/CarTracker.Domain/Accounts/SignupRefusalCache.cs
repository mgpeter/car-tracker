using System.Collections.Concurrent;

namespace CarTracker.Domain.Accounts;

/// <summary>
/// Remembers, briefly, that a subject was refused an account — so that being refused costs the identity
/// provider one lookup rather than one per request.
/// </summary>
/// <remarks>
/// <para>
/// <b>It exists because a refusal deliberately writes nothing down.</b> An admitted subject is remembered by
/// the <c>User</c> row it produced, and <see cref="AccountProvisioner"/> finds that row and asks the tenant
/// nothing ever again. A refused subject leaves no row — that is the design, and the right one — so the next
/// request repeats the whole lookup, and the request after that. The browser makes this easy to reach: the
/// access probe behind the "not yet invited" panel refetches on window focus, so someone who leaves the tab
/// open re-probes every time they come back to it.
/// </para>
/// <para>
/// The cost lands on a rate-limited tenant, and the consequence falls on the wrong person. A throttled
/// Management API answers nothing, an address that cannot be read is on no list, and so the visitor who is
/// hammering it is not the one shut out — <b>an invited newcomer signing in for the first time is</b>, because
/// their one lookup is the one that gets refused. A cache is the fix for that, and it has to be a cache of the
/// refusal itself, since there is nowhere else to put it.
/// </para>
/// <para>
/// <b>It can only ever refuse; it never admits.</b> The stored value is a refusal, so the worst a stale or
/// wrong entry can do is keep somebody out for <see cref="Window"/> — the same direction every other unknown
/// in the invitation door takes. The window is short for the one case that matters: a person who has just
/// verified their address, or just been invited, retries within a minute rather than being told to come back
/// tomorrow. (Adding them to the allowlist is a configuration change and so a restart, which empties this
/// anyway.)
/// </para>
/// </remarks>
public sealed class SignupRefusalCache(TimeProvider clock)
{
    /// <summary>How long a refusal is remembered for.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <remarks>
    /// The keys arrive from strangers, so the dictionary is fed by whoever can reach the sign-in page and needs
    /// a ceiling. Well above any real burst — a deployment refusing a thousand distinct subjects inside a minute
    /// has a problem this class is not the answer to.
    /// </remarks>
    private const int MaxEntries = 1_000;

    // Ordinal: an Auth0 subject is an opaque identifier, and two that differ in case are two different people —
    // the same reading AccountProvisioner's adoption check takes.
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>The remembered refusal for <paramref name="externalId"/>, or null if there is none live.</summary>
    public string? Refusal(string externalId)
    {
        if (!_entries.TryGetValue(externalId, out var entry)) return null;
        if (entry.ExpiresAt > clock.GetUtcNow()) return entry.Detail;

        _entries.TryRemove(externalId, out _);
        return null;
    }

    /// <summary>Remembers that <paramref name="externalId"/> was refused, with the words it was refused in.</summary>
    /// <remarks>
    /// The detail is stored rather than re-derived so the second answer is identical to the first: an
    /// unverified address and an uninvited one are refused for different reasons, and a cache that collapsed
    /// them would change the explanation on a page reload for no reason the reader could see.
    /// </remarks>
    public void Remember(string externalId, string detail)
    {
        var now = clock.GetUtcNow();
        _entries[externalId] = new Entry(detail, now + Window);

        if (_entries.Count <= MaxEntries) return;

        foreach (var (key, entry) in _entries)
        {
            if (entry.ExpiresAt <= now) _entries.TryRemove(key, out _);
        }

        // Still over the ceiling means the entries are all live, so there is nothing to reclaim politely.
        // Emptying it costs each of those subjects one more lookup; not emptying it is an unbounded dictionary
        // with a public sign-up form feeding it.
        if (_entries.Count > MaxEntries) _entries.Clear();
    }

    private readonly record struct Entry(string Detail, DateTimeOffset ExpiresAt);
}
