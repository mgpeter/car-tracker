namespace CarTracker.Domain.Accounts.Import;

/// <summary>
/// What to call an imported car whose plate the account already owns.
/// </summary>
/// <remarks>
/// <para>
/// <c>ix_vehicles_registration</c> is unique on <c>(OwnerId, upper(replace(registration, ' ', '')))</c>, so a
/// registration the account already owns cannot be inserted as it stands. The chosen behaviour is to import it
/// under a modified one - <c>BT53 AKJ</c> becomes <c>BT53 AKJ-2</c> - rather than to refuse, because a
/// refusal makes "take on a car whose history already exists" impossible for anyone who happens to own the
/// same plate, and because the whole point of the import is that the seller's car lands beside yours.
/// </para>
/// <para>
/// <b>The cost, stated rather than buried.</b> A registration is a real-world identifier and a rewritten one
/// is fictional: <c>GET /api/vehicles/lookup/{reg}</c> will not resolve it, and an assistant asked about
/// "BT53 AKJ" now has two cars to choose between, one of which is not a car anybody owns. The mitigations are
/// that the plate is editable in the preview before anything is written, and that the vehicle's notes record
/// what it was cloned from. This is the sharpest edge in the spec and the preview exists largely because of it.
/// </para>
/// <para>
/// <b>And it gives up an idempotency guard that came free.</b> Refusing on collision would have made the
/// uniqueness index refuse a second import of the same file; renaming means importing twice silently succeeds,
/// producing <c>-2</c> and then <c>-3</c> copies of everything. The preview compensates by leading with the
/// count, which is why that warning is first rather than beside each row.
/// </para>
/// </remarks>
public static class RegistrationProposer
{
    /// <summary>The width of <c>vehicles.registration</c>, and of the computed normalised column beside it.</summary>
    public const int MaxLength = 16;

    /// <summary>The stored generated column's rule, in C#, so the two cannot disagree about what collides.</summary>
    public static string Normalise(string registration) =>
        registration.Replace(" ", string.Empty).ToUpperInvariant();

    /// <summary>
    /// <paramref name="registration"/> itself when it is free, or the first free variant of it.
    /// </summary>
    /// <param name="taken">
    /// Every <b>normalised</b> registration that is already spoken for - the account's own, plus the ones
    /// earlier vehicles in this same import have claimed. Compared normalised because that is what the index
    /// compares: <c>BT53AKJ</c> and <c>bt53 akj</c> are one plate.
    /// </param>
    public static string Propose(string registration, IReadOnlySet<string> taken)
    {
        var trimmed = registration.Trim();
        if (!taken.Contains(Normalise(trimmed))) return trimmed;

        // From 2, because the original is the first of them. The bound is a formality - it would take fourteen
        // thousand copies of one plate to reach it - and it is here so the loop is provably finite rather than
        // trusted to be.
        for (var n = 2; n < 10_000; n++)
        {
            var candidate = WithSuffix(trimmed, $"-{n}");
            if (!taken.Contains(Normalise(candidate))) return candidate;
        }

        throw new InvalidOperationException(
            $"Cannot propose a free registration based on '{registration}': every variant up to -9999 is taken.");
    }

    /// <summary>
    /// The plate with the suffix on it, truncating the base rather than overflowing the column.
    /// </summary>
    /// <remarks>
    /// <c>Registration</c> is <c>varchar(16)</c> and so is the computed normalised column, so a 16-character
    /// plate with <c>-2</c> appended is not a long registration - it is a failed insert. Truncating the base is
    /// the lossy half of an already-fictional plate, and the vehicle's notes carry the real one.
    /// </remarks>
    private static string WithSuffix(string registration, string suffix)
    {
        var room = MaxLength - suffix.Length;
        var stem = registration.Length <= room ? registration : registration[..room].TrimEnd();
        return stem + suffix;
    }
}
