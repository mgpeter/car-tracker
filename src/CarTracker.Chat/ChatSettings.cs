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
    /// <summary>The provider credential. Null or blank turns the whole feature off.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The model. Defaults to Sonnet 5, which sits in the same high-resolution vision tier as Opus 5 (2576 px
    /// long edge) at roughly 40% of the cost — so it is the one to beat, and task 8.1 measures both against
    /// BT53's own paperwork before this default is called settled.
    /// </summary>
    public string Model { get; set; } = "claude-sonnet-5";

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

    /// <summary>True when this deployment can run the chat at all.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
