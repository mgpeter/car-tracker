namespace CarTracker.Domain.Legal;

/// <summary>
/// Who is responsible for the data this deployment holds, and how to reach them.
/// </summary>
/// <remarks>
/// Null when the deployment publishes nothing. Sent to the client as one nullable block on
/// <c>GET /api/meta</c>, so "not published" has a single representation on both sides.
/// </remarks>
/// <param name="ControllerName">The person or organisation accountable for the data.</param>
/// <param name="ControllerContact">An address a data subject can write to.</param>
/// <param name="ControllerAddress">A postal address, or null. Optional everywhere.</param>
/// <param name="Jurisdiction">Whose law the terms are read under. Never null; defaults to the UK.</param>
/// <param name="Version">
/// Which version of the committed prose this build carries. Not configurable - see <see cref="LegalVersion"/>.
/// It rides here so the pages and <c>users.terms_version</c> read one definition.
/// </param>
/// <param name="HostingSummary">
/// One sentence naming where this deployment runs and where its backups go, or null. It cannot be derived: the
/// host left this repository in DEC-020, so the application genuinely does not know what machine it is on.
/// </param>
public sealed record LegalPublication(
    string ControllerName,
    string ControllerContact,
    string? ControllerAddress,
    string Jurisdiction,
    string? HostingSummary,
    string Version);

/// <summary>
/// Whether this deployment publishes a privacy policy, a cookie notice and terms, and what they say about who
/// is responsible.
/// </summary>
/// <remarks>
/// <para>
/// <b>A blank section means the documents are NOT published, which is the reverse of the polarity next door.</b>
/// A blank <c>Signup:</c> section means the door is open (DEC-022); a blank <c>Lookup:</c> or <c>Chat:</c>
/// section means that capability is off. This one joins the second group, and the reason it is worth stating
/// rather than assuming is DEC-024: for a legal document the fail-safe direction is not arguable. A page naming
/// the wrong controller makes a false statement about who is accountable to whom, and this repository is
/// deployed by people who are not its author - a NAS install must not tell somebody's household to write to a
/// stranger about their data.
/// </para>
/// <para>
/// The same sentence is in <c>deploy/.env.example</c>, <c>docker-compose.yml</c>, the README and the boot
/// posture line, because a polarity that lives in exactly one place is one nobody reads.
/// </para>
/// <para>
/// <b>There is no version key here, deliberately.</b> A deployment cannot version text it did not write, and a
/// configurable version number over fixed text is a false claim with a configuration key in front of it. The
/// version is <see cref="LegalVersion.Current"/>, a constant, and it rides out on <see cref="Publication"/>.
/// </para>
/// </remarks>
public sealed class LegalOptions
{
    /// <summary>The controller's name. Blank publishes nothing.</summary>
    public string? ControllerName { get; set; }

    /// <summary>An address a data subject can write to. Blank publishes nothing.</summary>
    public string? ControllerContact { get; set; }

    /// <summary>A postal address. Optional; blank omits it from the pages.</summary>
    public string? ControllerAddress { get; set; }

    /// <summary>Whose law the terms are read under. Blank means <c>United Kingdom</c>.</summary>
    public string? Jurisdiction { get; set; }

    /// <summary>
    /// One sentence naming where this runs and where its backups go. Optional; blank omits the paragraph
    /// entirely rather than printing a heading with nothing under it.
    /// </summary>
    public string? HostingSummary { get; set; }

    private const string DefaultJurisdiction = "United Kingdom";

    /// <summary>
    /// Whether this deployment has said enough to publish. Both a name and a contact are required: a document
    /// naming a controller nobody can write to is not one, and a contact with no controller names nobody.
    /// </summary>
    public bool IsPublished =>
        !string.IsNullOrWhiteSpace(ControllerName) && !string.IsNullOrWhiteSpace(ControllerContact);

    /// <summary>The parsed jurisdiction, never blank.</summary>
    public string ResolvedJurisdiction =>
        string.IsNullOrWhiteSpace(Jurisdiction) ? DefaultJurisdiction : Jurisdiction.Trim();

    /// <summary>
    /// What the client is told, or null when nothing is published.
    /// </summary>
    /// <remarks>
    /// Trimming happens here rather than in a component, so what reaches a page is what a page renders and no
    /// future consumer has to remember. A blank optional value becomes null rather than an empty string, for
    /// the same reason: an empty string is truthy in enough places to eventually paint a heading above nothing.
    /// </remarks>
    public LegalPublication? Publication =>
        IsPublished
            ? new LegalPublication(
                ControllerName!.Trim(),
                ControllerContact!.Trim(),
                Blank(ControllerAddress),
                ResolvedJurisdiction,
                Blank(HostingSummary),
                LegalVersion.Current)
            : null;

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
