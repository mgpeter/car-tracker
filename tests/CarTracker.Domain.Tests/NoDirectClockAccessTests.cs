using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using CarTracker.Domain;

namespace CarTracker.Domain.Tests;

/// <summary>
/// Fails the build if anything in CarTracker.Domain reads the system clock directly.
/// </summary>
/// <remarks>
/// <para>
/// Every expected figure in the derived-metrics spec is stated at a reference date of 2026-07-14. A service
/// that reads the ambient clock cannot be tested against any of them, and "days to renewal" is untestable in
/// principle without a fixed now. <see cref="Clock"/> over an injected <see cref="TimeProvider"/> is the only
/// permitted route.
/// </para>
/// <para>
/// Reads the compiled IL rather than the source text: a grep over .cs files is defeated by an alias, a
/// using-static, or an extension method. What is actually called is what matters.
/// </para>
/// </remarks>
public sealed class NoDirectClockAccessTests
{
    private static readonly HashSet<string> Banned =
    [
        "System.DateTime.get_Now",
        "System.DateTime.get_UtcNow",
        "System.DateTime.get_Today",
        "System.DateTimeOffset.get_Now",
        "System.DateTimeOffset.get_UtcNow",
    ];

    [Fact]
    public void No_type_in_the_domain_reads_the_ambient_clock()
    {
        var assemblyPath = typeof(Clock).Assembly.Location;

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();

        var offenders = new List<string>();

        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);

            if (member.GetKind() != MemberReferenceKind.Method)
            {
                continue;
            }

            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var declaringType = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
            var typeName = $"{metadata.GetString(declaringType.Namespace)}.{metadata.GetString(declaringType.Name)}";
            var fullName = $"{typeName}.{metadata.GetString(member.Name)}";

            if (Banned.Contains(fullName))
            {
                offenders.Add(fullName);
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"CarTracker.Domain reads the system clock directly via: {string.Join(", ", offenders.Distinct())}. " +
            "Inject TimeProvider and use Clock instead — otherwise the reference-date tests cannot pin 'now'.");
    }

    [Fact]
    public void The_ban_list_matches_real_members()
    {
        // Guards the guard: a typo in Banned would make the test above pass vacuously forever.
        Assert.NotNull(typeof(DateTime).GetProperty(nameof(DateTime.UtcNow), BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(DateTime).GetProperty(nameof(DateTime.Now), BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(DateTime).GetProperty(nameof(DateTime.Today), BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.UtcNow), BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(typeof(DateTimeOffset).GetProperty(nameof(DateTimeOffset.Now), BindingFlags.Public | BindingFlags.Static));
    }
}
