namespace CarTracker.Domain;

/// <summary>
/// Resolves "now" to a local date. The single point where the domain is allowed to ask what day it is.
/// </summary>
/// <remarks>
/// <para>
/// All date arithmetic in this domain is in <b>Europe/London local dates</b>. A renewal expires on a day, not
/// at an instant. Computing days-to-renewal in UTC puts the answer off by one for the last hour of every
/// summer day, and a countdown that flips a day early around a BST boundary is the kind of bug noticed exactly
/// once, in the worst way.
/// </para>
/// <para>
/// Calculators never see a <see cref="DateTimeOffset"/> — the conversion happens here, at the entry point, and
/// a <see cref="DateOnly"/> travels down.
/// </para>
/// </remarks>
public sealed class Clock(TimeProvider timeProvider)
{
    // IANA id. .NET 8+ resolves IANA ids on Windows too, so this works without the "GMT Standard Time"
    // Windows alias — and it stays correct if this ever runs in a Linux container, which it will.
    private static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public DateOnly Today() => DateOnly.FromDateTime(Now().DateTime);

    /// <summary>The current instant expressed in Europe/London.</summary>
    public DateTimeOffset Now() => TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), London);
}
