namespace CarTracker.Domain;

/// <summary>
/// Facts about this deployment that the domain needs and cannot work out for itself. Bound from the <c>App</c>
/// section.
/// </summary>
/// <remarks>
/// <para>
/// <b>A blank <c>App:PublicOrigin</c> means filing links are path-only.</b> <c>offer_to_file</c> then returns a
/// <c>/file?...</c> path and tells the owner to open Cambelt and go to it, rather than a clickable URL. Nothing
/// else changes. The polarity is stated again in <c>deploy/.env.example</c> and both compose files.
/// </para>
/// <para>
/// Configured rather than read from the request, because behind the gateway the request's host is whatever the
/// proxy forwarded, and a link handed to an MCP client must name the address the owner's phone can reach.
/// </para>
/// </remarks>
public sealed class AppOptions
{
    /// <summary>The address the app is reached at, such as <c>https://cambelt.app</c>. Blank means path-only links.</summary>
    public string? PublicOrigin { get; set; }

    /// <summary>
    /// <see cref="PublicOrigin"/> with surrounding whitespace and trailing slashes trimmed, or null when nothing
    /// is left. Nothing else is validated: a malformed value shows on the admin Deployment panel rather than
    /// failing boot.
    /// </summary>
    public string? Origin =>
        PublicOrigin?.Trim().TrimEnd('/') is { Length: > 0 } origin ? origin : null;
}
