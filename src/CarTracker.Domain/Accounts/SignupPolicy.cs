namespace CarTracker.Domain.Accounts;

/// <summary>Whether this deployment lets strangers in.</summary>
public enum SignupMode
{
    /// <summary>
    /// Anyone the identity provider authenticates gets an account. What an account may then <i>spend</i> is
    /// decided by <see cref="PlanOptions"/>, not by who they are.
    /// </summary>
    Open = 0,

    /// <summary>
    /// Only a verified address on the allowlist is provisioned. For a private deployment - the NAS, a fresh
    /// checkout somebody is dogfooding, an instance with one household on it.
    /// </summary>
    InviteOnly = 1,
}

/// <summary>Who may become an account on this deployment.</summary>
/// <remarks>
/// <para>
/// <b>The polarity flipped in 0.24.0 (DEC-022), and it flipped in the dangerous direction, so read this
/// before assuming.</b> A blank <c>Signup:</c> section used to mean the door was <i>shut</i>; it now means the
/// door is <i>open</i>. The reasoning is that the door stopped being what protects the deployment: an account
/// on its own costs nothing, and the three surfaces that do cost something - the assistant's model tokens, the
/// documents volume, the DVLA quota - are each bounded by a plan allowance that a stranger does not have. The
/// same sentence is written in <c>.env.example</c>, <c>deploy/docker-compose.yml</c> and the README Quickstart.
/// </para>
/// <para>
/// Note the polarity against the neighbours in that compose file, all three of which differ: a blank
/// <c>Lookup:</c> means the lookup is <i>off</i>, a blank <c>Chat:</c> means the assistant is <i>off</i>, and a
/// blank <c>Signup:</c> now means the door is <i>open</i>.
/// </para>
/// </remarks>
public sealed class SignupOptions
{
    /// <summary>
    /// <c>"Open"</c> or <c>"InviteOnly"</c>. Blank means <see cref="SignupMode.Open"/> - see the type's
    /// remarks, because that default is the reverse of what this file used to do.
    /// </summary>
    /// <remarks>
    /// <b>A string rather than the enum, and it is load-bearing rather than sloppy.</b> The compose file writes
    /// every key it knows about, so an unset variable arrives as <c>""</c> - and the configuration binder
    /// refuses <c>""</c> for an enum outright, taking the whole application down at boot over a key nobody
    /// filled in. That is the trap <c>ChatSettings.DailyTokensPerOwner</c> records, in the one place where
    /// falling into it means a deployment that will not start. <see cref="Resolved"/> does the parsing, and it
    /// is strict about a value that is present and wrong.
    /// </remarks>
    public string? Mode { get; set; }

    /// <summary>The parsed <see cref="Mode"/>.</summary>
    /// <remarks>
    /// Blank is <see cref="SignupMode.Open"/>, the shipped default. <b>A non-blank value that does not parse
    /// throws</b>, deliberately: somebody who wrote <c>InvitOnly</c> meant to shut the door, and silently
    /// leaving it open because of a typo is the one outcome nothing downstream could ever detect.
    /// </remarks>
    public SignupMode Resolved =>
        string.IsNullOrWhiteSpace(Mode)
            ? SignupMode.Open
            : Enum.TryParse<SignupMode>(Mode.Trim(), ignoreCase: true, out var parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"Signup:Mode is '{Mode}', which is not a sign-up mode. Use 'Open' or 'InviteOnly', or "
                    + "leave it blank for Open.");

    /// <summary>
    /// Comma-separated addresses admitted exactly, under <see cref="SignupMode.InviteOnly"/>. Read for nothing
    /// in <see cref="SignupMode.Open"/>. Blank admits nobody.
    /// </summary>
    public string? AllowedEmails { get; set; }

    /// <summary>
    /// Comma-separated domains ("example.com"), admitting every verified address at them. Blank admits nobody.
    /// </summary>
    public string? AllowedDomains { get; set; }
}

/// <summary>
/// The front door: whether an address may be provisioned into a new account.
/// </summary>
/// <remarks>
/// <para>
/// A pure decision over parsed configuration, deliberately separated from the provisioning it gates so it can
/// be tested exhaustively without a database, an identity provider or a request. The half that matters most —
/// that under <see cref="SignupMode.InviteOnly"/> no combination of blanks, stray commas or a bare <c>"@"</c>
/// admits a stranger - is untestable at any other layer.
/// </para>
/// <para>
/// It is asked <b>only about an unseen subject</b>. An account that already exists is resolved by its external
/// id and never re-checked, so shutting the door later stops newcomers without locking out the people already
/// inside - which is what a door is, and not what a permission check would be. Entitlement, the thing that is
/// re-read on every request, is <see cref="IAccountEntitlements"/> and lives one layer down.
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

    private readonly EmailAllowlist _allowed;

    public SignupPolicy(SignupOptions options)
    {
        Mode = options.Resolved;
        _allowed = new EmailAllowlist(options.AllowedEmails, options.AllowedDomains);
    }

    /// <summary>Open, or invitation-only.</summary>
    public SignupMode Mode { get; }

    /// <summary>How many addresses this door admits exactly. Meaningless while <see cref="Mode"/> is Open.</summary>
    public int AllowedEmailCount => _allowed.EmailCount;

    /// <summary>How many domains this door admits every verified address at.</summary>
    public int AllowedDomainCount => _allowed.DomainCount;

    /// <summary>
    /// True when nobody new can be admitted at all: invitation-only with nothing listed.
    /// </summary>
    public bool IsClosed => Mode is SignupMode.InviteOnly && _allowed.IsEmpty;

    /// <summary>True when <paramref name="email"/> may be provisioned into a new account.</summary>
    /// <param name="emailVerified">
    /// Whether the identity provider has confirmed the person controls that address.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>Under <see cref="SignupMode.Open"/> this admits everyone, including an address it cannot read.</b>
    /// That is the point of the mode and not an oversight: the identity provider has already authenticated
    /// somebody, the address is only ever used to <i>identify</i> the account here, and a deployment with no
    /// <c>Auth0:Management:</c> credential must still be able to create one.
    /// <see cref="AccountProvisioner"/> stores the subject in <c>Email</c> in that case and repairs it later.
    /// </para>
    /// <para>
    /// <b>Under <see cref="SignupMode.InviteOnly"/>, an unverified address is refused and without that the list
    /// is not a door.</b> On a database connection a self-registering stranger types their own address, so an
    /// address alone is a claim rather than evidence: <c>AllowedDomains=example.com</c> would admit anyone
    /// willing to register as <c>anything@example.com</c>, and the deployment would look invitation-only while
    /// being open to the internet. What makes the address mean something is the tenant having sent a mail to it
    /// and seen the link followed. So the two conditions are one check, in one place - an allowlist that could
    /// be satisfied by typing is not an allowlist. The consequence to know before pointing an invitation-only
    /// deployment at a tenant: a connection that never verifies addresses admits nobody, whatever the list says.
    /// </para>
    /// </remarks>
    public bool Admits(string? email, bool emailVerified)
    {
        if (Mode is SignupMode.Open) return true;

        return emailVerified && _allowed.Contains(email);
    }
}
