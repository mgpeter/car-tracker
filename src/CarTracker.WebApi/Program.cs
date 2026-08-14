using System.Text.Json.Serialization;
using CarTracker.Chat;
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
//
// The timeouts are load-bearing, not tuning. Postgres waits for a lock FOREVER by default, and nothing else in
// the stack bounds a request: no command timeout, no gateway timeout, no MCP call timeout. So a single session
// holding an ACCESS EXCLUSIVE lock on one table (an interrupted `dotnet ef database update`, a psql/DBeaver
// window left `idle in transaction` after a DDL statement, a second app instance racing the startup migration)
// makes every tool that touches THAT table hang indefinitely while every other tool answers instantly — which
// is precisely how it presents to the assistant: `list_tyre_readings` and `log_tyre_reading` time out,
// `get_reference` returns straight away. A hang carries no diagnostic; a fast, named failure does.
//
// lock_timeout is the short one because waiting on a lock is never productive here — this schema's writes are
// single-row and sub-millisecond, so a 5s wait already means contention, not queueing. statement_timeout is the
// wider backstop for a query that runs away rather than blocks. Migrations run under these too, deliberately:
// a migration that cannot take its lock within 5s is racing another instance, and failing loudly beats the
// silent forever-wait the comment on MigrateAsync below warns about.
var connectionString = new Npgsql.NpgsqlConnectionStringBuilder(
    builder.Configuration.GetConnectionString("cartrackerdb"))
{
    // -c options are applied by the server at connection start; appended so anything Aspire already set stands.
    Options = string.Join(' ', new[] { "-c lock_timeout=5000", "-c statement_timeout=30000" }),
}.ConnectionString;

builder.Services.AddDbContext<CarTrackerDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.CommandTimeout(30)));
builder.EnrichNpgsqlDbContext<CarTrackerDbContext>();

// The shared brain (README §4). The MCP host calls the same registration, so a metric cannot disagree with
// itself across surfaces.
builder.Services.AddCarTrackerDomain();

// The in-process MCP server (README §5, DEC-004/DEC-014). Tools live in CarTracker.ModelContextProtocol and
// resolve the same domain services registered above; mapped at /mcp below.
builder.Services.AddCarTrackerMcp();

// The in-app chat (DEC-019). It defines no tools of its own — it sends the catalogue registered on the line
// above — so it must come after both. With no Chat:ApiKey this registers settings and nothing else, the way the
// DVLA lookup below does: a capability that cannot work is not offered rather than failing on first use.
builder.Services.AddCarTrackerChat(builder.Configuration);

// The real audit sink (overrides the domain's no-op): attributes each write to the request's token.
builder.Services.AddScoped<CarTracker.Domain.Writes.IAssistantAudit, CarTracker.WebApi.Authentication.AssistantAudit>();

// The request's resolved owner, read by CarTrackerDbContext's vehicle query filter. One scoped instance backs
// both the concrete type (the middleware mutates it) and the interface (the context reads it).
builder.Services.AddScoped<CarTracker.Data.CurrentUserAccessor>();
builder.Services.AddScoped<CarTracker.Data.ICurrentUserAccessor>(
    sp => sp.GetRequiredService<CarTracker.Data.CurrentUserAccessor>());

// Registration lookup (DEC-015). The credentials are server-side only and absent by default — a fresh checkout
// has no DVLA key, and the feature degrades to "not configured" rather than the app refusing to start. Bound
// from Lookup:* (user-secrets in dev, the host's secret store in prod), never committed appsettings.
var lookupOptions = new CarTracker.Domain.Lookup.VehicleLookupOptions();
builder.Configuration.GetSection("Lookup").Bind(lookupOptions);
builder.Services.AddSingleton(lookupOptions);
builder.Services.AddScoped<CarTracker.Domain.Lookup.IVehicleLookupService, CarTracker.WebApi.Lookup.DvlaVehicleLookupService>();

// Short timeouts: someone is waiting on a sheet with a cursor in it, and a slow DVLA must fail to manual entry
// rather than hang the add-car flow.
builder.Services.AddHttpClient(CarTracker.WebApi.Lookup.DvlaVehicleLookupService.VesClient, c =>
{
    c.BaseAddress = new Uri(lookupOptions.VesBaseUrl);
    c.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddHttpClient(CarTracker.WebApi.Lookup.DvlaVehicleLookupService.MotClient, c =>
{
    c.BaseAddress = new Uri(lookupOptions.MotBaseUrl);
    c.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddHttpClient(CarTracker.WebApi.Lookup.DvlaVehicleLookupService.MotTokenClient, c =>
{
    c.Timeout = TimeSpan.FromSeconds(8);
});

// Where uploaded documents live (DEC-005 — bytes on a mounted volume, path on the row). Resolved to an absolute
// path here so the domain takes no dependency on the hosting stack for one string. Relative values resolve
// against the content root, which is what makes the dev default work with no configuration at all; the
// container overrides it with the mount point via Documents__RootPath.
builder.Services.AddSingleton(new CarTracker.Domain.Documents.DocumentStorageOptions(
    Path.GetFullPath(
        builder.Configuration.GetValue<string>("Documents:RootPath") ?? "documents-data",
        builder.Environment.ContentRootPath)));
builder.Services.AddSingleton<CarTracker.Domain.Documents.DocumentStore>();

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

// The invitation door and what a new account is allowed to inherit (DEC-018). Singletons, because
// UseMiddleware constructs from the root provider and these are read on the way to provisioning an account;
// AccountProvisioner itself is scoped, since it takes the request's DbContext. Bound as objects rather than
// through IOptions for the same reason VehicleLookupOptions above is: one instance, read directly, no
// change-token machinery for configuration that cannot change without a restart.
//
// AN EMPTY ALLOWLIST MEANS CLOSED. Both Signup keys blank — the committed default, and every fresh checkout —
// admits nobody new, while existing accounts keep working. The opposite is the natural reading, which is why it
// is written here, in .env.example, in deploy/docker-compose.yml and in the README Quickstart.
var signupOptions = new CarTracker.Domain.Accounts.SignupOptions();
builder.Configuration.GetSection("Signup").Bind(signupOptions);
var signupPolicy = new CarTracker.Domain.Accounts.SignupPolicy(signupOptions);
builder.Services.AddSingleton(signupPolicy);

// A refusal writes no row by design, so without this the tenant is asked about an uninvited subject on every
// single request they make. Singleton because the whole point is that it outlives the request that filled it;
// it can only ever refuse, never admit, so a stale entry costs a stranger a minute and nothing else.
builder.Services.AddSingleton<CarTracker.Domain.Accounts.SignupRefusalCache>();

// DEC-016 retired: adoption of pre-multi-user vehicles is a named external id, not "whoever signs in first".
var ownershipOptions = new CarTracker.Domain.Accounts.OwnershipOptions();
builder.Configuration.GetSection("Ownership").Bind(ownershipOptions);
builder.Services.AddSingleton(ownershipOptions);

// The Management API credential — how the server learns a person's real email address, which the access token
// does not carry. Seeded with the authority already resolved above so one tenant is configured once; Bind only
// overwrites what the configuration actually names.
var managementOptions = new CarTracker.Domain.Accounts.Auth0ManagementOptions { Authority = auth0Authority };
builder.Configuration.GetSection("Auth0:Management").Bind(managementOptions);
builder.Services.AddSingleton(managementOptions);
builder.Services.AddSingleton<CarTracker.Domain.Accounts.IIdentityProviderClient, CarTracker.WebApi.Accounts.Auth0ManagementClient>();
builder.Services.AddScoped<CarTracker.Domain.Accounts.AccountProvisioner>();

// Works the pending_identity_deletions queue: a deletion whose Auth0 call failed leaves a live login with no
// data behind it, and this is what stops that being permanent. Same registration shape as the reminders job.
builder.Services.AddHostedService<CarTracker.WebApi.Accounts.IdentityDeletionRetryService>();

// Short timeouts for the same reason the DVLA clients have them: someone is waiting on a login, and a slow
// tenant must fail to "not invited" rather than hang the first request of a session.
builder.Services.AddHttpClient(CarTracker.WebApi.Accounts.Auth0ManagementClient.ManagementClient, c =>
{
    c.BaseAddress = new Uri(managementOptions.ManagementBaseUrl);
    c.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddHttpClient(CarTracker.WebApi.Accounts.Auth0ManagementClient.TokenClient, c =>
{
    c.Timeout = TimeSpan.FromSeconds(8);
});

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
// NB: decimal rounding is deliberately NOT added here. A custom JsonConverter<decimal> defeats the OpenAPI
// generator's type introspection — every decimal property would emit as an empty `{}` schema (→ `unknown` in the
// generated TypeScript), a non-additive contract break. The web app formats numbers in JS, so the raw tail is a
// non-issue on the REST surface; the assistant reported it, so the rounding lives on the MCP tool serializer only
// (see McpServerRegistration). Both surfaces read the same DTOs; only the JSON writer differs.

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<ApiKeySecuritySchemeTransformer>();
    options.AddSchemaTransformer<NumericTypeSchemaTransformer>();
});

var app = builder.Build();

// Whether the invitation door is open, stated once per boot — in every environment, because the deployment it
// matters on is the one nobody is watching a console for.
//
// It exists because the absence of this line cost a live debugging session. The Management credential reached a
// container empty (the compose file beside the .env was a copy that predated the keys), and the *only* signal
// anywhere was an Information line emitted per refused sign-in, inside the container's log, saying an address
// could not be resolved. From outside, an invited person with a verified address was simply told they were not
// invited — a true sentence about a deployment whose door had never opened. A posture the operator believes is
// one thing and is provably another is worth a line at boot; a fact this cheap to state should not need a
// container-log dig to discover.
//
// Warning when the door is shut, and that is not the "cries wolf" hazard Auth0ManagementClient guards against
// with its per-refusal Information: this fires once per container rather than once per stranger, and on a
// deployment meant to be open it is the one thing worth interrupting for. A genuinely closed deployment — a
// fresh checkout, CI, a private instance — logs it once a restart and can ignore it, which is the correct cost.
//
// Counts, never the addresses: the diagnostic question is "did anything load", and 0-vs-2 answers it. They come
// from SignupPolicy's own parsed arrays, so the number here is the number the door matches against — a stray
// comma that parses to nothing must not be reported as an entry that admits somebody.
{
    var doorShut = !managementOptions.IsConfigured || signupPolicy.IsClosed;
    var summary =
        "Sign-up posture: Management credential {Management}, allowlist {Emails} address(es) + {Domains} "
        + "domain(s), unowned-vehicle adoption {Adoption}.{Consequence}";
    object?[] values =
    [
        managementOptions.IsConfigured ? "configured" : "NOT configured (Auth0:Management:ClientId/ClientSecret)",
        signupPolicy.AllowedEmailCount,
        signupPolicy.AllowedDomainCount,
        string.IsNullOrWhiteSpace(ownershipOptions.ClaimUnownedVehiclesFor) ? "off" : "armed for one subject",
        doorShut
            ? " NOBODY NEW CAN BE ADMITTED — an address that cannot be read is on no list, and an empty allowlist"
              + " means closed. Existing accounts are unaffected."
            : string.Empty,
    ];

    if (doorShut) app.Logger.LogWarning(summary, values);
    else app.Logger.LogInformation(summary, values);
}

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

// Apply migrations on startup in development, or in any environment that opts in with
// ApplyMigrationsOnStartup=true. Dev needs it because Aspire creates an empty database each run, so without it
// the first request fails with 'relation "vehicles" does not exist'. In production it stays OFF by default —
// a rolling deploy would race two instances into the same migration — but the single-instance NAS deployment
// sets the flag so an auto-updated container brings its own schema forward on boot.
if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("ApplyMigrationsOnStartup"))
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
app.MapDocumentEndpoints();
app.MapBudgetEndpoints();
app.MapReminderEndpoints();
app.MapAssistantEndpoints();
// The account itself, not a vehicle: what it holds, what it can take away, and how it ends. Web-login only —
// deliberately no MCP tool for any of it (see AccountEndpoints).
app.MapAccountEndpoints();
app.MapAccountExportEndpoints();
app.MapChatEndpoints();

// The MCP Streamable HTTP endpoint at /mcp (README §5). Authenticated by the fallback policy today; Phase 4
// task 3 scopes it to the McpRead token.
app.MapCarTrackerMcp();

app.Run();
