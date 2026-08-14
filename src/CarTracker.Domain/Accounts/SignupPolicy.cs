namespace CarTracker.Domain.Accounts;

/// <summary>Who may become an account on this deployment.</summary>
/// <remarks>
/// <para>
/// <b>Two comma-separated scalars rather than two <c>string[]</c>s, and the reason is the environment-variable
/// binder rather than taste.</b> An array binds from indexed keys (<c>Signup__AllowedDomains__0</c>), so a
/// compose file that lists the key with an empty default — exactly how <c>Lookup__*</c> is written a few lines
/// away — writes one <i>empty</i> element. The list is then non-empty but holds <c>""</c>, and a rule asked
/// "does any entry match this address's domain?" over it can answer yes for every address alive. The closed
/// default would have become an open door with nothing in the config file looking wrong. A scalar has no such
/// state: blank is blank.
/// </para>
/// <para>
/// An <b>empty allowlist means closed</b>. That is the fail-safe direction and the opposite of the natural
/// reading, which is why it is stated in <c>.env.example</c>, <c>deploy/docker-compose.yml</c> and the README
/// Quickstart as well as here. Note the polarity against its neighbour in that compose file: a blank
/// <c>Lookup:</c> means the lookup is <i>off</i>, a blank <c>Signup:</c> means the door is <i>shut</i>.
/// </para>
/// </remarks>
public sealed class SignupOptions
{
    /// <summary>Comma-separated addresses admitted exactly. Blank admits nobody.</summary>
    public string? AllowedEmails { get; set; }

    /// <summary>Comma-separated domains ("example.com"), admitting every address at them. Blank admits nobody.</summary>
    public string? AllowedDomains { get; set; }
}

/// <summary>
/// The invitation door: whether an address may be provisioned into a new account.
/// </summary>
/// <remarks>
/// <para>
/// A pure decision over parsed configuration, deliberately separated from the provisioning it gates so it can
/// be tested exhaustively without a database, an identity provider or a request. The half that matters most —
/// that no combination of blanks, stray commas or a bare <c>"@"</c> admits a stranger — is untestable at any
/// other layer.
/// </para>
/// <para>
/// It is asked <b>only about an unseen subject</b>. An account that already exists is resolved by its external
/// id and never re-checked, so tightening or emptying the allowlist later shuts the door on newcomers without
/// locking out the people already inside — which is what an invitation list is, and not what a permission check
/// would be.
/// </para>
/// </remarks>
public sealed class SignupPolicy
{
    /// <summary>
    /// The RFC 9457 <c>type</c> a refusal is reported under, so the client can tell "not invited" from every
    /// other 403 and render the panel that explains it rather than a generic error.
    /// </summary>
    /// <remarks>
    /// A bare token rather than a URI: there is no documentation page behind it to dereference, and inventing a
    /// URL that 404s would be worse than honest. What matters is that it is stable and matched exactly.
    /// </remarks>
    public const string NotInvitedProblemType = "signup-not-invited";

    private readonly string[] _emails;
    private readonly string[] _domains;

    public SignupPolicy(SignupOptions options)
    {
        _emails = Split(options.AllowedEmails);

        // "@example.com" and "example.com" are the same instruction and both get written; taking the '@' off
        // makes them so. An entry of nothing but "@" would leave an empty string that matched every address,
        // so it drops out here rather than becoming the fail-open case this whole class exists to avoid.
        _domains = [.. Split(options.AllowedDomains).Select(d => d.TrimStart('@')).Where(d => d.Length > 0)];
    }

    /// <summary>True when <paramref name="email"/> may be provisioned into a new account.</summary>
    /// <param name="emailVerified">
    /// Whether the identity provider has confirmed the person controls that address.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>A null or unreadable address is refused, never admitted.</b> The address is not always knowable — the
    /// access token carries no <c>email</c> claim on this tenant, so it is fetched from the identity provider,
    /// and an unconfigured or unreachable Management API leaves it null. That is the case where a
    /// "can't check, let them in" default would quietly open the door on the one deployment least able to
    /// notice, so the unknown address is simply not on the list.
    /// </para>
    /// <para>
    /// <b>An unverified address is refused too, and without it the list is not a door.</b> On a database
    /// connection a self-registering stranger types their own address, so an address alone is a claim rather
    /// than evidence: <c>AllowedDomains=example.com</c> would admit anyone willing to register as
    /// <c>anything@example.com</c>, and the deployment would look invitation-only while being open to the
    /// internet. What makes the address mean something is the tenant having sent a mail to it and seen the link
    /// followed (or a social connection asserting it). So the two conditions are one check, in one place — an
    /// allowlist that could be satisfied by typing is not an allowlist.
    /// </para>
    /// <para>
    /// The consequence to know before pointing this at a tenant: a connection that never verifies addresses
    /// admits nobody, whatever the list says. That is the fail-safe direction and the same one every other
    /// unknown takes here.
    /// </para>
    /// </remarks>
    public bool Admits(string? email, bool emailVerified)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        if (!emailVerified) return false;

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
    /// The closed default is therefore structural — there is no branch that says "if the list is empty, refuse",
    /// because an empty list has nothing to match against.
    /// </remarks>
    private static string[] Split(string? value) =>
        value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
}
