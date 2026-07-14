using CarTracker.WebApi.Authentication;
using CarTracker.WebApi.Endpoints;
using CarTracker.WebApi.OpenApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton(TimeProvider.System);

builder.Services
    .AddAuthentication(ApiKeyAuthenticationOptions.Scheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationOptions.Scheme,
        // Bind here, not via a bare Configure<T>(section). AddScheme registers the options as *named*
        // options keyed by the scheme name, and AuthenticationHandler reads them with .Get(Scheme.Name) —
        // so configuring the default unnamed instance leaves the handler seeing a null key and rejecting
        // every request. The section and the scheme sharing the name "ApiKey" makes that easy to miss.
        options => builder.Configuration.GetSection(ApiKeyAuthenticationOptions.Scheme).Bind(options));

// Authenticated by default. An endpoint that should be open has to say so with .AllowAnonymous(), which is
// the safe direction to be forgetful in.
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<ApiKeySecuritySchemeTransformer>());

var app = builder.Build();

app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

// /openapi/v1.json and /scalar are both reached through the Gateway, which routes them explicitly.
app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference(options => options.AddPreferredSecuritySchemes(ApiKeyAuthenticationOptions.Scheme)).AllowAnonymous();

app.MapMetaEndpoints();

app.Run();
