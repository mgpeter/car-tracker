using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using CarTracker.Domain.Lookup;
using CarTracker.Shared.Logs;

namespace CarTracker.WebApi.Lookup;

/// <summary>
/// Turns a registration into vehicle facts, by asking the DVLA and the DVSA.
/// </summary>
/// <remarks>
/// <para>
/// <b>The call is server-side and there is no other option.</b> The DVLA key must not reach a browser, and the
/// app's strict CSP forbids a browser→<c>api.gov.uk</c> fetch outright, so a client-side lookup could not work
/// even if the key were public.
/// </para>
/// <para>
/// <b>It degrades rather than failing.</b> Both upstreams need credentials a fresh checkout does not have, and
/// the whole feature is an accelerator for a form that works perfectly well by hand. An unconfigured deployment
/// answers <see cref="LookupOutcome.NotConfigured"/> and the sheet keeps its manual path. The same reasoning
/// covers a DVSA outage while VES is up: the identity fields come back, the MOT seed does not, and a partial
/// answer beats none.
/// </para>
/// <para>
/// <b>Short timeout, no retry storm.</b> Someone is waiting on a sheet with a cursor in it. A slow DVLA must
/// fail to manual entry quickly rather than hang the flow — the timeouts sit where the clients are registered.
/// </para>
/// </remarks>
public sealed class DvlaVehicleLookupService(
    IHttpClientFactory httpClientFactory,
    VehicleLookupOptions options,
    ILogger<DvlaVehicleLookupService> logger) : IVehicleLookupService
{
    public const string VesClient = "dvla-ves";
    public const string MotClient = "dvsa-mot";
    public const string MotTokenClient = "dvsa-mot-token";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<LookupResponse> LookupAsync(string registration, CancellationToken cancellationToken = default)
    {
        var reg = LookupMapping.Normalize(registration);
        if (reg.Length == 0)
            return new LookupResponse(LookupOutcome.NotFound, null, "A registration is needed to look one up.");

        if (!options.IsConfigured)
        {
            return new LookupResponse(LookupOutcome.NotConfigured, null,
                "Registration lookup is not configured on this deployment — no DVLA API key is set. "
                + "Enter the details manually.");
        }

        VesVehicle? ves;
        try
        {
            ves = await QueryVesAsync(reg, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Warning, not error: an unreachable DVLA is an outage upstream, not a fault here, and it has a
            // designed answer — the owner types the details in.
            logger.LogWarning(ex, "DVLA VES lookup failed for {Registration}", reg);
            return new LookupResponse(LookupOutcome.Unavailable, null,
                "DVLA lookup unavailable — enter the details manually.");
        }

        if (ves is null)
            return new LookupResponse(LookupOutcome.NotFound, null, $"No DVLA record for registration '{reg}'.");

        // The MOT half is best-effort. VES answering is enough to call the lookup a success; a missing MOT seed
        // just means the countdown starts once a pass is logged, which is the ordinary path anyway.
        DateOnly? motExpiry = null;
        string? motStatus = null;
        if (options.IsMotConfigured)
        {
            try
            {
                (motExpiry, motStatus) = await QueryMotAsync(reg, cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "DVSA MOT history lookup failed for {Registration}", reg);
            }
        }

        return new LookupResponse(LookupOutcome.Found, new VehicleLookupResult(
            Registration: reg,
            Make: ves.Make,
            // VES returns make and colour but not model — often absent or coarse. The owner types "Freelander 1"
            // themselves, which the sheet's hint says plainly.
            Model: null,
            Year: ves.YearOfManufacture,
            Colour: LookupMapping.Titlecase(ves.Colour),
            EngineSizeCc: ves.EngineCapacity,
            FuelType: LookupMapping.MapFuel(ves.FuelType),
            MotExpiry: motExpiry ?? ves.MotExpiryDate,
            MotStatus: motStatus ?? ves.MotStatus,
            TaxStatus: ves.TaxStatus,
            VedExpiry: ves.TaxDueDate,
            Source: "dvla"));
    }

    private async Task<VesVehicle?> QueryVesAsync(string reg, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(VesClient);
        client.DefaultRequestHeaders.TryAddWithoutValidation("x-api-key", options.VesApiKey);

        // VES is a POST that takes the reg in the body — unusual for a read, but it is their API, not ours.
        using var response = await client.PostAsJsonAsync(
            "/vehicle-enquiry/v1/vehicles", new { registrationNumber = reg }, ct);

        if (response.StatusCode is HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<VesVehicle>(Json, ct);
    }

    private async Task<(DateOnly? Expiry, string? Status)> QueryMotAsync(string reg, CancellationToken ct)
    {
        var token = await GetMotTokenAsync(ct);
        if (token is null) return (null, null);

        var client = httpClientFactory.CreateClient(MotClient);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-API-Key", options.MotApiKey);
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        using var response = await client.GetAsync($"/v1/trade/vehicles/registration/{reg}", ct);
        if (response.StatusCode is HttpStatusCode.NotFound) return (null, null);
        response.EnsureSuccessStatusCode();

        var history = await response.Content.ReadFromJsonAsync<MotVehicle>(Json, ct);

        // The most recent test's expiry, not the first in the list — DVSA's ordering is not guaranteed, and
        // taking the wrong one would seed a countdown from a years-old test.
        var latest = history?.MotTests?
            .Where(t => t.ExpiryDate is not null)
            .OrderByDescending(t => t.ExpiryDate)
            .FirstOrDefault();

        return (latest?.ExpiryDate, latest is null ? "No details held" : "Valid");
    }

    private async Task<string?> GetMotTokenAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(MotTokenClient);
        using var response = await client.PostAsync(options.MotTokenUrl, new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", options.MotClientId!),
            new KeyValuePair<string, string>("client_secret", options.MotClientSecret!),
            new KeyValuePair<string, string>("scope", options.MotScope),
        ]), ct);

        if (!response.IsSuccessStatusCode) return null;

        return (await response.Content.ReadFromJsonAsync<OAuthToken>(Json, ct))?.AccessToken;
    }

    // The upstream shapes, only as far as this feature reads them.
    internal sealed record VesVehicle(
        string? Make,
        string? Colour,
        int? YearOfManufacture,
        int? EngineCapacity,
        string? FuelType,
        string? TaxStatus,
        DateOnly? TaxDueDate,
        string? MotStatus,
        DateOnly? MotExpiryDate);

    internal sealed record MotVehicle([property: JsonPropertyName("motTests")] IReadOnlyList<MotTest>? MotTests);

    internal sealed record MotTest(DateOnly? ExpiryDate);

    private sealed record OAuthToken([property: JsonPropertyName("access_token")] string? AccessToken);
}
