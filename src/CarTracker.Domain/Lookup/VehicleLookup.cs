using CarTracker.Shared;
using CarTracker.Shared.Logs;

namespace CarTracker.Domain.Lookup;

/// <summary>How a registration lookup ended. Each maps to a distinct status, because each needs a distinct answer.</summary>
public enum LookupOutcome
{
    /// <summary>At least one upstream knew the registration.</summary>
    Found = 1,

    /// <summary>Well-formed, and neither upstream has a record. "No such car", not "we could not ask".</summary>
    NotFound = 2,

    /// <summary>An upstream timed out, errored or throttled. Try again, or type it in.</summary>
    Unavailable = 3,

    /// <summary>
    /// No API credentials on this deployment. Distinct from <see cref="Unavailable"/> because it is permanent
    /// until someone provisions a key, and the message must say so rather than inviting a retry that cannot
    /// succeed.
    /// </summary>
    NotConfigured = 4,
}

public sealed record LookupResponse(LookupOutcome Outcome, VehicleLookupResult? Result, string? Detail = null);

/// <summary>
/// Resolves a registration to un-persisted vehicle facts. The implementation is infrastructure (it makes HTTP
/// calls) and lives in the WebApi; the seam is here so the domain owns the vocabulary and a future in-app
/// assistant can reach the same capability without going through the web layer.
/// </summary>
public interface IVehicleLookupService
{
    Task<LookupResponse> LookupAsync(string registration, CancellationToken cancellationToken = default);
}

/// <summary>Server-side credentials for the two upstreams. Never reaches the browser; never in committed config.</summary>
/// <remarks>
/// Two separate credentials because they are two separate services with different auth: DVLA VES takes a plain
/// API key header, DVSA MOT History uses OAuth client credentials. Absent values are the normal state of a
/// fresh checkout — the feature degrades to <see cref="LookupOutcome.NotConfigured"/> rather than the app
/// refusing to start, because it is an accelerator for a form that still works by hand.
/// </remarks>
public sealed class VehicleLookupOptions
{
    /// <summary>DVLA Vehicle Enquiry Service API key (<c>x-api-key</c> header).</summary>
    public string? VesApiKey { get; set; }

    public string VesBaseUrl { get; set; } = "https://driver-vehicle-licensing.api.gov.uk";

    /// <summary>DVSA MOT History API key, sent alongside the OAuth bearer.</summary>
    public string? MotApiKey { get; set; }

    public string MotBaseUrl { get; set; } = "https://history.mot.api.gov.uk";

    /// <summary>DVSA OAuth client credentials — the token endpoint, client id and secret.</summary>
    public string? MotTokenUrl { get; set; }
    public string? MotClientId { get; set; }
    public string? MotClientSecret { get; set; }
    public string MotScope { get; set; } = "https://tapi.dvsa.gov.uk/.default";

    /// <summary>True when VES can be called at all. The MOT half is independently optional.</summary>
    /// <remarks>
    /// VES alone is a useful lookup — make, colour, year, engine and tax all come back, and only the MOT seed
    /// is missing. Requiring both keys to use either would make the feature all-or-nothing for no reason.
    /// </remarks>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(VesApiKey);

    public bool IsMotConfigured =>
        !string.IsNullOrWhiteSpace(MotApiKey)
        && !string.IsNullOrWhiteSpace(MotTokenUrl)
        && !string.IsNullOrWhiteSpace(MotClientId)
        && !string.IsNullOrWhiteSpace(MotClientSecret);
}

/// <summary>
/// DVLA wording to the app's own vocabulary — the load-bearing half of the lookup, and the only half worth
/// testing in isolation. Everything else it does is an HTTP call.
/// </summary>
public static class LookupMapping
{
    /// <summary>
    /// VES fuel wording to <see cref="FuelType"/>. Unrecognised returns null rather than guessing.
    /// </summary>
    /// <remarks>
    /// Null leaves the sheet's own select standing at its default, where the owner can see and correct it. A
    /// guess would be invisible and would wrong every MPG figure derived from that car thereafter. The app
    /// models one Hybrid, so VES's several hybrid wordings all land there — growing the enum to match an
    /// upstream's taxonomy would be the tail wagging the dog.
    /// </remarks>
    public static FuelType? MapFuel(string? vesFuelType) => vesFuelType?.Trim().ToUpperInvariant() switch
    {
        "PETROL" => FuelType.Petrol,
        "DIESEL" => FuelType.Diesel,
        "ELECTRICITY" or "ELECTRIC" => FuelType.Electric,
        "HYBRID ELECTRIC" or "HYBRID ELECTRIC (CLEAN)" or "PLUG-IN HYBRID"
            or "PETROL/PLUG-IN ELECTRIC HYBRID" => FuelType.Hybrid,
        "GAS" or "GAS BI-FUEL" or "LPG" => FuelType.LPG,
        _ => null,
    };

    /// <summary>VES shouts its colours ("BLENHEIM SILVER"); the rest of the app does not.</summary>
    public static string? Titlecase(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

    /// <summary>
    /// The same normalisation the rest of the app applies to a plate, so "BT53 AKJ" and "bt53akj" resolve alike.
    /// </summary>
    public static string Normalize(string registration) =>
        new(registration.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}
