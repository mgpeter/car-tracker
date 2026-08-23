namespace CarTracker.Domain.Legal;

/// <summary>
/// Which version of the published documents this build carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>A constant, not a configuration key.</b> A deployment cannot version text it did not write, and a
/// configurable version number over fixed text is a false claim with a config key in front of it (DEC-024).
/// </para>
/// <para>
/// <b>It lives here rather than beside the prose in the front-end bundle</b>, even though the prose is what it
/// versions, because two things read it: the pages render it, and <c>AccountProvisioner</c> stamps it on
/// <c>users.terms_version</c> at the moment an account is created. A copy in each is a copy that can disagree,
/// and the one it would misreport is which text somebody was actually shown. It reaches the client on
/// <c>meta.legal</c>.
/// </para>
/// <para>
/// <b>Move this when the wording changes in a way that matters</b>, and leave it alone for a typo: the point
/// of the column is to be able to say which text somebody agreed to.
/// </para>
/// </remarks>
public static class LegalVersion
{
    public const string Current = "2026-08-23";
}
