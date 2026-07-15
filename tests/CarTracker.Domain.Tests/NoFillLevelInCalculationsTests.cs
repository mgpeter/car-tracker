using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using CarTracker.Shared;

namespace CarTracker.Domain.Tests;

/// <summary>
/// Fails the build if anything in CarTracker.Domain reads <see cref="FillLevel"/>.
/// </summary>
/// <remarks>
/// <para>
/// Fill level is descriptive. Litres is a receipt figure accurate to 2dp; "the tank was about half" is a
/// glance at a needle. Letting the second gate the first made a soft observation the arbiter of a hard one.
/// Whether an MPG is trustworthy is now decided by whether the number is physically plausible — see
/// <see cref="Calculators.FuelEconomyCalculator.MinPlausibleMpg"/>.
/// </para>
/// <para>
/// Reads the compiled IL rather than source text, for the same reason as
/// <see cref="NoDirectClockAccessTests"/>: a grep is defeated by an alias or a using-static, and what is
/// actually referenced is what matters.
/// </para>
/// </remarks>
public sealed class NoFillLevelInCalculationsTests
{
    [Fact]
    public void No_type_in_the_domain_references_FillLevel()
    {
        var assemblyPath = typeof(Clock).Assembly.Location;

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();

        var offenders = new List<string>();

        // A type reference is the broadest catch: reading the property, comparing the enum, or even naming the
        // type in a signature all leave one behind.
        foreach (var handle in metadata.TypeReferences)
        {
            var typeReference = metadata.GetTypeReference(handle);
            var name = metadata.GetString(typeReference.Name);

            if (name is nameof(FillLevel))
            {
                offenders.Add(name);
            }
        }

        foreach (var handle in metadata.MemberReferences)
        {
            var member = metadata.GetMemberReference(handle);

            if (member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var declaringType = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);

            if (metadata.GetString(declaringType.Name) is nameof(FillLevel))
            {
                offenders.Add($"{nameof(FillLevel)}.{metadata.GetString(member.Name)}");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"CarTracker.Domain references FillLevel via: {string.Join(", ", offenders.Distinct())}. " +
            "Fill level is descriptive — no calculation may depend on it. MPG rests on litres, and its " +
            "trustworthiness on the plausibility band.");
    }

    [Fact]
    public void FillLevel_still_exists_for_its_descriptive_use()
    {
        // Guards the guard: if the enum were deleted, the test above would pass vacuously forever.
        Assert.Equal(3, Enum.GetValues<FillLevel>().Length);
    }
}
