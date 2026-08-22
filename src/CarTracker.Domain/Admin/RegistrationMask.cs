namespace CarTracker.Domain.Admin;

/// <summary>Turns a registration into something an operator can read without reading the plate.</summary>
/// <remarks>
/// <para>
/// <b>Data minimisation, not a security control, and the spec says so out loud.</b> The operator has database
/// access anyway, so nothing here keeps a determined administrator from a registration. What it buys is a
/// screen that can be opened, screenshotted and pasted into a support thread without spreading other people's
/// plates, and friction against idle curiosity. An address plus <c>BT** **J</c> is still enough to correlate
/// somebody's support email with their row, which is the only workflow that needs it.
/// </para>
/// <para>
/// <b>It runs server-side and no admin response ever carries a full registration.</b> Masking in the browser
/// would not be masking: the data would already have left the server, and "the page does not display it" is a
/// claim about rendering rather than about disclosure.
/// </para>
/// </remarks>
public static class RegistrationMask
{
    private const char Hidden = '*';

    /// <summary>Keep the first two characters and the last one; hide the rest.</summary>
    /// <remarks>
    /// Spaces and hyphens survive so the result still reads as a plate and an import suffix stays visible -
    /// <c>BT53 AKJ-2</c> becomes <c>BT** ***-2</c>, which is what distinguishes two rows that differ only by
    /// it. Anything shorter than four characters is hidden entirely, because keeping two of three reveals it.
    /// </remarks>
    public static string Mask(string? registration)
    {
        if (string.IsNullOrWhiteSpace(registration)) return string.Empty;

        var trimmed = registration.Trim();
        if (trimmed.Length < 4) return new string(Hidden, trimmed.Length);

        var masked = trimmed.ToCharArray();
        var hidAnything = false;

        for (var i = 2; i < masked.Length - 1; i++)
        {
            if (masked[i] is ' ' or '-') continue;

            masked[i] = Hidden;
            hidAnything = true;
        }

        // The fail-safe, and the reason the loop counts rather than just running. A value whose whole middle
        // is separators - not a plate any authority issues, but nothing here validates that - would come back
        // byte-for-byte unchanged, so the one thing this function exists to prevent would silently not happen.
        // If the rule could hide nothing, hide everything.
        return hidAnything ? new string(masked) : new string(Hidden, trimmed.Length);
    }
}
