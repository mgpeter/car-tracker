namespace CarTracker.Domain.Accounts;

/// <summary>
/// A comma-separated list of addresses and domains, and the question "is this address on it?".
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <see cref="SignupPolicy"/> when a second list appeared (<see cref="PlanOptions"/>'s comp
/// list). The parsing is the half whose correctness matters - a stray comma, a line of spaces or a bare
/// <c>"@"</c> must parse to <i>nothing</i> rather than to an entry that matches every address alive - and a
/// rule that subtle must not exist twice, because the copy is where it will be got wrong.
/// </para>
/// <para>
/// <b>Two comma-separated scalars rather than two <c>string[]</c>s, and the reason is the environment-variable
/// binder rather than taste.</b> An array binds from indexed keys (<c>Signup__AllowedDomains__0</c>), so a
/// compose file that lists the key with an empty default - exactly how <c>Lookup__*</c> is written a few lines
/// away - writes one <i>empty</i> element. The list is then non-empty but holds <c>""</c>, and a rule asked
/// "does any entry match this address's domain?" over it can answer yes for everybody. A scalar has no such
/// state: blank is blank.
/// </para>
/// <para>
/// <b>An empty list matches nobody</b>, and structurally rather than by a branch: there is no
/// "if the list is empty, refuse", because an empty list has nothing to match against. Every caller therefore
/// gets the fail-safe direction for free.
/// </para>
/// </remarks>
public sealed class EmailAllowlist
{
    private readonly string[] _emails;
    private readonly string[] _domains;

    /// <param name="emails">Comma-separated addresses, matched exactly and case-insensitively.</param>
    /// <param name="domains">
    /// Comma-separated domains. <c>"@example.com"</c> and <c>"example.com"</c> are the same instruction and both
    /// get written, so the <c>'@'</c> comes off here. An entry of nothing but <c>"@"</c> would leave an empty
    /// string matching every address, so it drops out rather than becoming the fail-open case this type exists
    /// to avoid.
    /// </param>
    public EmailAllowlist(string? emails, string? domains)
    {
        _emails = Split(emails);
        _domains = [.. Split(domains).Select(d => d.TrimStart('@')).Where(d => d.Length > 0)];
    }

    /// <summary>The empty list, which admits nobody. For a caller with nothing configured.</summary>
    public static EmailAllowlist Empty { get; } = new(null, null);

    /// <summary>How many addresses this list matches exactly.</summary>
    /// <remarks>
    /// For posture logging, and read from the parsed arrays rather than re-split from the raw strings on
    /// purpose: the number reported has to be the number the list actually matches against. A trailing comma, a
    /// value of "," or a bare "@" all parse to nothing here, and a count derived a second way would say
    /// "1 entry" about a list that matches nobody - which is the one thing an operator reading that line is
    /// trying to rule out.
    /// </remarks>
    public int EmailCount => _emails.Length;

    /// <summary>How many domains this list matches every address at.</summary>
    public int DomainCount => _domains.Length;

    /// <summary>True when nothing at all is listed, so no address can match.</summary>
    public bool IsEmpty => _emails.Length == 0 && _domains.Length == 0;

    /// <summary>True when <paramref name="email"/> is named, or sits at a named domain.</summary>
    /// <remarks>
    /// A null or blank address matches nothing. It is not always knowable - the access token carries no
    /// <c>email</c> claim on this tenant, so it is fetched from the identity provider and an unconfigured or
    /// unreachable Management API leaves it null - and "can't check, let them in" is the default that would
    /// quietly open the door on the deployment least able to notice.
    /// </remarks>
    public bool Contains(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;

        var address = email.Trim();

        if (_emails.Any(e => string.Equals(e, address, StringComparison.OrdinalIgnoreCase))) return true;

        // Last '@', not first: the local part of an address may legally contain one inside quotes, and the
        // domain is always what follows the final separator.
        var at = address.LastIndexOf('@');
        if (at < 0 || at == address.Length - 1) return false;

        var domain = address[(at + 1)..];
        return _domains.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase));
    }

    /// <remarks>
    /// <c>RemoveEmptyEntries</c> with <c>TrimEntries</c> is the load-bearing pair: a trailing comma, a line of
    /// spaces or a value of "," parses to nothing at all rather than to an entry that matches an empty domain.
    /// </remarks>
    private static string[] Split(string? value) =>
        value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
}
