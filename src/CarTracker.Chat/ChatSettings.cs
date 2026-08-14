namespace CarTracker.Chat;

/// <summary>
/// Everything the chat reads from configuration, bound from the <c>Chat:</c> section.
/// </summary>
/// <remarks>
/// <para>
/// <c>Chat:</c> rather than <c>Anthropic:</c> (DEC-019): a provider-named key under a provider-agnostic seam
/// would be wrong the day the seam is used, and this groups the chat's settings the way <c>Lookup:</c>,
/// <c>Signup:</c> and <c>Documents:</c> already group theirs.
/// </para>
/// <para>
/// <b>Absent means off, and that is the safe direction.</b> With no <see cref="ApiKey"/> the endpoints answer
/// 503 and <c>meta.chatConfigured</c> is false, so the shell renders no chat icon at all — the same rule the
/// DVLA lookup button follows, for the same reason: a control that cannot work is not offered. That is CI's
/// state and every fresh checkout's.
/// </para>
/// </remarks>
public sealed class ChatSettings
{
    /// <summary>See <see cref="DailyTokensPerOwner"/>. Applied when the setting is absent, not when it is zero.</summary>
    public const long DefaultDailyTokensPerOwner = 1_000_000;

    /// <summary>See <see cref="DailyTokensGlobal"/>.</summary>
    public const long DefaultDailyTokensGlobal = 5_000_000;

    /// <summary>The shipped model. See <see cref="Model"/>.</summary>
    public const string DefaultModel = "claude-sonnet-5";

    /// <summary>The provider credential. Null or blank turns the whole feature off.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The model. Defaults to Sonnet 5, which sits in the same high-resolution vision tier as Opus 5 (2576 px
    /// long edge) at roughly 40% of the cost — so it is the one to beat, and task 8.1 measures both against
    /// BT53's own paperwork before this default is called settled.
    /// </summary>
    public string Model { get; set; } = DefaultModel;

    /// <summary>
    /// Thinking depth. Not `high`: this is small-catalogue extraction, not long-horizon agentic work, and both
    /// candidate models are unusually strong at the lower levels. After caching, this is the cost lever.
    /// </summary>
    public string Effort { get; set; } = "medium";

    /// <summary>Output ceiling per turn. Sized with headroom because thinking counts against it.</summary>
    public int MaxOutputTokens { get; set; } = 8_192;

    /// <summary>
    /// How many times the loop may go round in one request before it stops and says so.
    /// </summary>
    /// <remarks>
    /// A cost control as much as a correctness one: an unbounded loop is the only way a single turn gets
    /// genuinely expensive.
    /// </remarks>
    public int MaxToolIterations { get; set; } = 8;

    /// <summary>
    /// One account's daily token allowance. <b>Zero turns the chat off for every account</b> — the fail-safe
    /// direction, and the opposite of the natural reading, so it is stated here, in <c>.env.example</c> and in
    /// the README.
    /// </summary>
    /// <remarks>
    /// <b>Nullable, and that is load-bearing rather than tidy.</b> The compose file writes every key it knows
    /// about, so an unset variable arrives as an empty string — which the configuration binder converts to null
    /// for a nullable target and refuses outright for a plain <c>long</c>, taking the whole application down at
    /// boot over a key nobody filled in. Absent therefore means "the default"; <b>an explicit zero means off</b>.
    /// Counts every token the provider reported, cached prefix included, at full weight — see
    /// <c>ChatUsage.Total</c> for why. The tool catalogue is ~17k of that per turn, so this default is roughly
    /// 60 turns a day per account rather than a quantity of conversation. At Sonnet 5 prices, a day spent to
    /// this ceiling costs well under a pound, because the majority of it is read from cache at a tenth of list.
    /// </remarks>
    public long? DailyTokensPerOwner { get; set; }

    /// <summary>
    /// What the whole deployment may spend in a day, across every account. Zero turns it off for everyone.
    /// </summary>
    /// <remarks>
    /// A separate fear from the per-owner limit rather than a multiple of it: the per-owner ceiling cannot bound
    /// a deployment's bill without knowing how many accounts it will have, and this one does not care.
    /// </remarks>
    public long? DailyTokensGlobal { get; set; }

    /// <summary>What one account may actually spend today.</summary>
    public long PerOwnerCeiling => DailyTokensPerOwner ?? DefaultDailyTokensPerOwner;

    /// <summary>What the whole deployment may actually spend today.</summary>
    public long GlobalCeiling => DailyTokensGlobal ?? DefaultDailyTokensGlobal;

    /// <summary>True when this deployment can run the chat at all.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
