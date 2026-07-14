using CarTracker.WebApi.Authentication;
using Microsoft.AspNetCore.OpenApi;
// Microsoft.OpenApi v2 flattened the old Microsoft.OpenApi.Models namespace — the types live here now.
using Microsoft.OpenApi;

namespace CarTracker.WebApi.OpenApi;

/// <summary>
/// Advertises the API-key scheme in the OpenAPI document.
/// </summary>
/// <remarks>
/// Microsoft.AspNetCore.OpenApi does not infer security schemes from authentication handlers, so without this
/// the document has no scheme for Scalar to bind an "Authenticate" box to — and every request from the Scalar
/// UI would 401 with no way to fix it from the page.
/// </remarks>
public sealed class ApiKeySecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[ApiKeyAuthenticationOptions.Scheme] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = ApiKeyAuthenticationOptions.HeaderName,
            Description = "The value of ApiKey:Value on the server. Sent as the X-Api-Key header.",
        };

        return Task.CompletedTask;
    }
}
