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

// The request's resolved owner, read by CarTrackerDbContext's vehicle query filter. One scoped instance backs
// both the concrete type (the middleware mutates it) and the interface (the context reads it).
builder.Services.AddScoped<CarTracker.Data.CurrentUserAccessor>();
builder.Services.AddScoped<CarTracker.Data.ICurrentUserAccessor>(
    sp => sp.GetRequiredService<CarTracker.Data.CurrentUserAccessor>());

// Reminders (README §4 "phase 1.5"): the pluggable channels and the hosted digest job. The in-app badge is the
// only adapter for this cut — email, push and Assistant·MCP are named registration points DEC-006 leaves open.
builder.Services.AddSingleton<CarTracker.Domain.Reminders.INotificationChannel, CarTracker.WebApi.Reminders.InAppBadgeChannel>();
builder.Services.AddHostedService<CarTracker.WebApi.Reminders.RemindersBackgroundService>();

// Non-secret and known (the tenant's issuer origin and the API identifier), so they default here exactly as the
// SPA's authConfig.ts does — the API validates tokens with no configuration, and a different tenant overrides
// via Auth0:Authority / Auth0:Audience. Baking the default also means a stale or missing appsettings copy cannot
// silently leave Authority null and disable token validation, which surfaces only as a 401 (IDX10204).
var auth0Authority = builder.Configuration["Auth0:Authority"] ?? "https://usualexpat.uk.auth0.com/";
var auth0Audience = builder.Configuration["Auth0:Audience"] ?? "cartracker.api";

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
        AssistantTokenAuthenticationHandler.Scheme, _ => { })
    // The interactive multi-user login (README §6). Auth0-issued JWTs are validated against the tenant's JWKS
    // (signature, issuer, audience, expiry) discovered from Authority. This is the web front-end's auth path,
    // replacing the shared X-Api-Key. MapInboundClaims stays off so the raw `sub`/`email` claim names survive
    // for CurrentUserMiddleware to read.
    .AddJwtBearer("Auth0", options =>
    {
        options.Authority = auth0Authority;
        options.Audience = auth0Audience;
        options.MapInboundClaims = false;
        // Surface *why* a token was rejected in the API logs — a valid-looking token that 401s is otherwise
        // undiagnosable (the reason is buried in the WWW-Authenticate header). Common causes: the API cannot
        // reach the tenant's JWKS (IDX20803), an audience/issuer mismatch, or an expired token.
        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Auth0")
                    .LogWarning(context.Exception, "Auth0 JWT validation failed: {Reason}", context.Exception.Message);
                return Task.CompletedTask;
            },
        };
    });

// Authenticated by default (the fallback), now via the Auth0 scheme — the interactive web login is the way in.
// An endpoint that should be open says so with .AllowAnonymous(); /mcp overrides with its own token policy
// below. The legacy X-Api-Key scheme stays registered (it fronts nothing sensitive now — meta and the docs are
// anonymous) but no longer satisfies the fallback, so it grants no vehicle access on its own.
// The MCP policies check the scope *claim*, not the scheme — the seam the Auth0/JWT scheme could also drop into
// (DEC-014): give a JWT the same scope claims and the tools would not change.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder("Auth0")
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

// Startup diagnostic for the Auth0 wiring (development only, so it never delays a production boot on an external
// call). A 401 with IDX10204/IDX20803 gives no hint whether the Authority is wrong or the tenant's discovery
// document (the JWKS the signature check needs) is simply unreachable from this process — so prove both at boot.
// A common cause of "unreachable" on Windows is a Hyper-V/Docker ephemeral-port reservation (WSAEACCES 10013),
// which is a host-networking problem, not an app one: net stop winnat / net start winnat clears it.
if (app.Environment.IsDevelopment())
{
    // Boot-time reachability check: token validation needs the tenant's JWKS, so a process that cannot reach
    // Auth0 will 401 every request with a misleading IDX10204. If this fails with a socket-permission error
    // (WSAEACCES 10013), it is the host blocking this executable's outbound access — a per-app firewall/AV
    // (e.g. Bitdefender) treating the freshly-built exe as untrusted — not the app. Allow the exe (or run via
    // `dotnet` with UseAppHost=false). Development-only so it never delays a production boot.
    try
    {
        using var probe = new HttpClient();
        var discovery = await probe.GetStringAsync($"{auth0Authority.TrimEnd('/')}/.well-known/openid-configuration");
        app.Logger.LogInformation("Auth0 discovery reachable ({Length} bytes); token validation is wired.", discovery.Length);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Auth0 discovery is NOT reachable from this process — every token will 401. If this is a socket-permission error, a per-app firewall/AV is blocking this executable's outbound access.");
    }
}

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

// Resolves the authorized principal to a local user and pins it on the request, so the vehicle query filter
// scopes every read to its owner. After UseAuthorization deliberately: that is where both the Auth0 (fallback)
// and assistant-token (McpRead) principals are established.
app.UseMiddleware<CarTracker.WebApi.Authentication.CurrentUserMiddleware>();

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
