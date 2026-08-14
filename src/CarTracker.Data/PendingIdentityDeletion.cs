namespace CarTracker.Data;

/// <summary>
/// An identity whose local account is already gone and whose login at the identity provider is not yet.
/// </summary>
/// <remarks>
/// <para>
/// Account deletion is data-first, identity-second, because the alternatives are worse: deleting the identity
/// first strands the data behind a login nobody can use, and calling the provider inside the transaction turns a
/// commit failure into the same stranding. The chosen order's failure mode is benign — an Auth0 login with no
/// data behind it, which provisions a fresh empty account if anyone signs in with it.
/// </para>
/// <para>
/// Benign is not erased, though, and the difference is exactly what a regulator asks about. So a failed identity
/// call is <b>recorded</b> rather than logged: this row is the promise that the removal will keep being
/// attempted until it succeeds, and the retry service is what keeps it.
/// </para>
/// <para>
/// <b>No foreign key to <see cref="User"/>.</b> The row's whole purpose is to outlive the user it names — it is
/// written in the same transaction that deletes them.
/// </para>
/// </remarks>
public sealed class PendingIdentityDeletion
{
    public int Id { get; set; }

    /// <summary>The Auth0 subject, as <see cref="User.ExternalId"/> held it. Unique: one attempt per identity.</summary>
    public required string ExternalId { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>How many times the provider has been asked. Diagnostic — nothing gives up on a count.</summary>
    public int Attempts { get; set; }

    /// <summary>Why the last attempt failed, or null if it has not been tried yet. Never the empty string.</summary>
    public string? LastError { get; set; }
}
