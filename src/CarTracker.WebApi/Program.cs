using System.Text.Json.Serialization;
using CarTracker.Data;
using CarTracker.Domain;
using CarTracker.ModelContextProtocol;
using CarTracker.WebApi.Authentication;
using CarTracker.WebApi.Endpoints;
using CarTracker.WebApi.OpenApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Resolved into CarTrackerDbContext's constructor and into Clock. Registering it here is what lets the
// context take a TimeProvider at all — see the AddDbContext note below.
builder.Services.AddSingleton(TimeProvider.System);

// AddDbContext, not Aspire's AddNpgsqlDbContext: the latter enables context pooling, and a pooled context may
// only take DbContextOptions<T>. CarTrackerDbContext also takes a TimeProvider, which plain AddDbContext
// resolves from DI. EnrichNpgsqlDbContext then adds back what Aspire would have contributed — retries, health
// check, logging and telemetry — and must come after the registration it enriches.
builder.Services.AddDbContext<CarTrackerDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("cartrackerdb")));
builder.EnrichNpgsqlDbContext<CarTrackerDbContext>();

// The shared brain (README §4). The MCP host calls the same registration, so a metric cannot disagree with
// itself across surfaces.
builder.Services.AddCarTrackerDomain();

// The in-process MCP server (README §5, DEC-004/DEC-014). Tools live in CarTracker.ModelContextProtocol and
// resolve the same domain services registered above; mapped at /mcp below.
builder.Services.AddCarTrackerMcp();

// The real audit sink (overrides the domain's no-op): attributes each write to the request's token.
builder.Services.AddScoped<CarTracker.Domain.Writes.IAssistantAudit, CarTracker.WebApi.Authentication.AssistantAudit>();

// Reminders (README §4 "phase 1.5"): the pluggable channels and the hosted digest job. The in-app badge is the
// only adapter for this cut — email, push and Assistant·MCP are named registration points DEC-006 leaves open.
builder.Services.AddSingleton<CarTracker.Domain.Reminders.INotificationChannel, CarTracker.WebApi.Reminders.InAppBadgeChannel>();
builder.Services.AddHostedService<CarTracker.WebApi.Reminders.RemindersBackgroundService>();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationOptions.Scheme)
    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationOptions.Scheme,
        // Bind here, not via a bare Configure<T>(section). AddScheme registers the options as *named*
        // options keyed by the scheme name, and AuthenticationHandler reads them with .Get(Scheme.Name) —
        // so configuring the default unnamed instance leaves the handler seeing a null key and rejecting
        // every request. The section and the scheme sharing the name "ApiKey" makes that easy to miss.
        options => builder.Configuration.GetSection(ApiKeyAuthenticationOptions.Scheme).Bind(options))
    // The assistant's scoped bearer tokens (README §5.1, DEC-014) — a separate scheme from the web api-key,
    // guarding /mcp. It coexists with ApiKey; the MCP policies below select it explicitly.
    .AddScheme<AuthenticationSchemeOptions, AssistantTokenAuthenticationHandler>(
        AssistantTokenAuthenticationHandler.Scheme, _ => { });

// Authenticated by default (the fallback). An endpoint that should be open says so with .AllowAnonymous().
// The MCP policies check the scope *claim*, not the scheme — the seam a future Auth0/JWT scheme drops into
// unchanged (DEC-014): map its permissions to the same claims and the tools do not change.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy("McpRead", policy => policy
        .AddAuthenticationSchemes(AssistantTokenAuthenticationHandler.Scheme)
        .RequireClaim(AssistantClaims.Scope, AssistantClaims.ScopeRead));

    options.AddPolicy("McpWrite", policy => policy
        .AddAuthenticationSchemes(AssistantTokenAuthenticationHandler.Scheme)
        .RequireClaim(AssistantClaims.Scope, AssistantClaims.ScopeWrite));
});

// The MCP write-audit filter and the token handler read the current request's principal.
builder.Services.AddHttpContextAccessor();

// Enums cross the wire as strings ("Petrol", not 1) — the same choice the schema makes, and for the same
// reasons: a payload stays readable, a client need not know ordinals, and inserting an enum member cannot
// silently change what an existing value means. It also makes the generated TypeScript a union of literals
// rather than a bare number.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<ApiKeySecuritySchemeTransformer>();
    options.AddSchemaTransformer<NumericTypeSchemaTransformer>();
});

var app = builder.Build();

// Development only, deliberately. Aspire creates an empty database each time its volume is new, so without
// this the first request fails with 'relation "vehicles" does not exist' and the dev loop needs a manual
// step. In production, applying schema changes is a decision someone makes — not something the app does to
// itself on boot, where a rolling deploy would race two instances into the same migration.
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<CarTrackerDbContext>().Database.MigrateAsync();
}

app.MapDefaultEndpoints();

app.UseAuthentication();
app.UseAuthorization();

// /openapi/v1.json and /scalar are both reached through the Gateway, which routes them explicitly.
app.MapOpenApi().AllowAnonymous();
app.MapScalarApiReference(options => options.AddPreferredSecuritySchemes(ApiKeyAuthenticationOptions.Scheme)).AllowAnonymous();

app.MapMetaEndpoints();
app.MapVehicleEndpoints();
app.MapFuelEndpoints();
app.MapServiceEndpoints();
app.MapReferenceEndpoints();
app.MapAnomalyEndpoints();
app.MapTaskEndpoints();
app.MapIssueEndpoints();
app.MapLogEndpoints();
app.MapMileageEndpoints();
app.MapChecksEndpoints();
app.MapExpenseEndpoints();
app.MapBudgetEndpoints();
app.MapReminderEndpoints();
app.MapAssistantEndpoints();

// The MCP Streamable HTTP endpoint at /mcp (README §5). Authenticated by the fallback policy today; Phase 4
// task 3 scopes it to the McpRead token.
app.MapCarTrackerMcp();

app.Run();
