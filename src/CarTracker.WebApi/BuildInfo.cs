using System.Reflection;

namespace CarTracker.WebApi;

/// <summary>The release this build came from, read once.</summary>
/// <remarks>
/// <para>
/// The value is the root <c>VERSION</c> file: <c>Directory.Build.props</c> reads it into <c>&lt;Version&gt;</c>,
/// so a build, a test run and a published image all report the same number. It used to be the SDK default of
/// <c>1.0.0</c> on every surface, which meant <c>GET /api/meta</c> disagreed with the tag on the image serving
/// it, and every account export was stamped <c>schemaVersion: "1.0.0"</c> whatever wrote it.
/// </para>
/// <para>
/// Three endpoints need it - meta, export and import - and each carried its own copy of this expression. One
/// read, so a file and the app it came from cannot describe the same build two ways. There is no
/// <c>?? "0.0.0"</c> fallback: the SDK always emits the attribute, so the old one was unreachable, and a build
/// that somehow lacked it should fail at startup rather than write files stamped with a version that never
/// shipped.
/// </para>
/// </remarks>
internal static class BuildInfo
{
    public static string Version { get; } =
        typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
}
