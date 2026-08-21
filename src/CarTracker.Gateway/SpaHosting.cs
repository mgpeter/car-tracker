using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CarTracker.Gateway;

/// <summary>
/// The two things the gateway does for the SPA beyond handing it bytes: telling it which Auth0 application to
/// use, and telling the browser what that application is allowed to talk to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both of these used to be compiled into the bundle, and that is what made the app un-self-hostable.</b>
/// <c>src/lib/authConfig.ts</c> reads <c>import.meta.env</c>, which Vite substitutes during <c>vite build</c>,
/// so the tenant, client id and audience were literals inside the JavaScript before the image existed. The
/// only way to point a deployment at a different Auth0 application was to build your own image. The API half
/// was already fine - it reads <c>Auth0:Authority</c> and <c>Auth0:Audience</c> at run time - so the browser
/// half was the whole of the problem.
/// </para>
/// <para>
/// <b>They live together in one file because they have to agree.</b> The policy's <c>connect-src</c> must
/// permit exactly the tenant the SPA is about to call. When those were two independent literals - one in
/// <c>authConfig.ts</c>, one in the CSP plugin - pointing a build at another tenant produced a login that
/// silently never completed: the browser refuses the token request with a console line and nothing else looks
/// wrong. Here they are read from one configuration section, in one place, at the same moment.
/// </para>
/// </remarks>
public static class SpaHosting
{
    /// <summary>The global the SPA looks for. Must match <c>src/lib/authConfig.ts</c>.</summary>
    private const string ConfigGlobal = "__CAMBELT_CONFIG__";

    /// <summary>
    /// The defaults, which are this project's own Auth0 application.
    /// </summary>
    /// <remarks>
    /// Baked in for the same reason <c>CarTracker.WebApi/Program.cs</c> bakes its Authority: an unset value
    /// should leave a working deployment rather than a subtly broken one, and these are public identifiers
    /// rather than secrets. It is also what makes this change a redeploy rather than a reconfiguration for the
    /// existing NAS - with nothing set, it gets exactly what it had before.
    /// </remarks>
    private const string DefaultDomain = "usualexpat.uk.auth0.com";
    private const string DefaultClientId = "AYVXSt9aa5rz4kHFYs3KZ5HqYfBNkPKp";
    private const string DefaultAudience = "cartracker.api";

    /// <summary>The pre-paint theme script the build injects, whose exact bytes the policy must hash.</summary>
    /// <remarks>
    /// Matched on the marker attribute rather than on position: it is the only thing in the document carrying
    /// it, and the build controls both ends. <c>Singleline</c> so the pattern spans the tag's content.
    /// </remarks>
    private static readonly Regex ThemeScript = new(
        """<script\b[^>]*\bdata-theme-preload\b[^>]*>(?<body>.*?)</script>""",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private sealed record SpaConfig(string Domain, string ClientId, string Audience);

    private static SpaConfig Read(IConfiguration configuration) => new(
        configuration["Auth0:Domain"] is { Length: > 0 } d ? d : DefaultDomain,
        configuration["Auth0:ClientId"] is { Length: > 0 } c ? c : DefaultClientId,
        configuration["Auth0:Audience"] is { Length: > 0 } a ? a : DefaultAudience);

    /// <summary>
    /// Serves <c>/config.js</c>: one line of JavaScript setting the global the SPA reads at first render.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A script rather than JSON, deliberately.</b> A classic <c>&lt;script src&gt;</c> in the head is
    /// render-blocking, so the value is simply there when the app mounts and <c>authConfig.ts</c> stays
    /// synchronous. Fetching JSON instead would mean the outermost component - <c>Auth0Provider</c>, which
    /// needs all three values at first render - waiting on a promise, and that means a bootstrap gate and a
    /// splash that do not otherwise need to exist. <c>script-src 'self'</c> already permits this.
    /// </para>
    /// <para>
    /// <b>Nothing shadows this route.</b> No YARP route is a catch-all in production, and the dev-only
    /// catch-all loses to a literal path at <c>Order = 0</c>. But <c>UseStaticFiles</c> runs first and is
    /// terminal, so <b>never add a <c>public/config.js</c> to the SPA</b>: a real file would silently win and
    /// every deployment would quietly get whatever it contained.
    /// </para>
    /// <para>
    /// <c>no-store</c> because a cached copy outlives a redeploy, and the symptom of that is a browser still
    /// talking to the previous tenant.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapSpaConfig(this IEndpointRouteBuilder app)
    {
        app.MapGet("/config.js", (IConfiguration configuration, HttpResponse response) =>
        {
            var config = Read(configuration);

            response.Headers.CacheControl = "no-store";

            // Serialised rather than interpolated, so a value containing a quote produces valid JavaScript
            // instead of a syntax error that blanks the whole config.
            return Results.Text(
                $"window.{ConfigGlobal}={JsonSerializer.Serialize(config, SpaConfigJson)};",
                "application/javascript; charset=utf-8");
        });

        return app;
    }

    /// <summary>camelCase, because the SPA reads <c>domain</c>, <c>clientId</c> and <c>audience</c>.</summary>
    private static readonly JsonSerializerOptions SpaConfigJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Emits the Content-Security-Policy on the SPA's HTML, as a response header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This replaced a <c>&lt;meta&gt;</c> tag written at build time</b>, which could not name a tenant the
    /// build did not know about. The policy has to be able to differ per deployment for the same reason the
    /// config does, and a meta tag is fixed the moment the bundle is built.
    /// </para>
    /// <para>
    /// <b>The build must therefore stop emitting the meta tag, and that is not a tidy-up.</b> Multiple
    /// policies <i>intersect</i> - the effective permission is what both allow. A surviving meta tag naming
    /// the build's tenant, beside a header naming the deployment's, leaves <c>connect-src</c> as <c>'self'</c>
    /// alone, and login then fails on precisely the deployments that configured themselves correctly.
    /// </para>
    /// <para>
    /// <b>Keyed on the response content type, not the path.</b> The document is served by
    /// <c>UseStaticFiles</c> at <c>/</c> and by <c>MapFallbackToFile</c> at every SPA deep link
    /// (<c>/bt53akj/fuel</c>), so a path check would cover the first and miss the rest. <c>OnStarting</c> is
    /// where the content type is finally known.
    /// </para>
    /// </remarks>
    public static IApplicationBuilder UseSpaCsp(this WebApplication app)
    {
        // Read once, at startup: the document does not change while the process is alive, and hashing it per
        // request would be work repeated for every deep link.
        var scriptHash = ThemeScriptHash(app.Environment.WebRootPath);
        var config = Read(app.Configuration);
        var policy = Policy(scriptHash, config.Domain);

        app.Logger.LogInformation(
            "SPA config: Auth0 tenant {Domain}, client {ClientId}, audience {Audience}. CSP is served as a "
            + "header on the document, with the pre-paint script hashed from the file being served.",
            config.Domain, config.ClientId, config.Audience);

        return app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                if (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
                {
                    context.Response.Headers.ContentSecurityPolicy = policy;
                }

                return Task.CompletedTask;
            });

            await next();
        });
    }

    /// <summary>
    /// The policy, which is the build-time one with its Auth0 origin made configurable.
    /// </summary>
    /// <remarks>
    /// Every clause below was reasoned about in <c>plugins/theme-csp.ts</c> before it moved here, and the
    /// notes are worth keeping with the code rather than in a file that no longer emits it.
    /// </remarks>
    private static string Policy(string scriptHash, string auth0Domain) => string.Join("; ",
    [
        "default-src 'self'",
        // The hash covers the pre-paint theme script. Vite's own bundle is an external 'self' module, and so
        // is /config.js.
        $"script-src 'self' '{scriptHash}'",
        // Tailwind emits a stylesheet; nothing carries inline style attributes needing a hash. 'unsafe-inline'
        // for styles is a real weakening, so it stays out until something demands it.
        "style-src 'self'",
        // DEC-010: with this, a CDN-loaded face fails loudly instead of silently degrading to a system font.
        "font-src 'self'",
        // `blob:` is load-bearing, not defensive. A bearer-authenticated app cannot serve bytes through a
        // plain <img src>, so document photos come through the authenticated fetch seam and render from
        // URL.createObjectURL - and 'self' does not cover the blob: scheme.
        "img-src 'self' data: blob:",
        // 'self' for the same-origin API through the gateway (DEC-009); the Auth0 tenant for the login's token
        // and silent-renewal XHR. Because the SPA uses refresh-token rotation rather than the hidden-iframe
        // flow, no frame-src to the tenant is needed - if that ever changes, add one on the same origin.
        $"connect-src 'self' https://{auth0Domain}",
        "object-src 'none'",
        "base-uri 'none'",
        "frame-ancestors 'none'",
        "form-action 'self'",
    ]);

    /// <summary>
    /// SHA-256 of the pre-paint theme script, taken from the very <c>index.html</c> this process serves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Hashed from the served file rather than carried across from the build, so the two cannot disagree.</b>
    /// The alternatives were both worse: recomputing it from a copy of the script in C# duplicates a string
    /// that must match another language byte for byte, and shipping the hash as a build artefact adds a file
    /// that can go missing or go stale. Hashing what is about to be served is correct by construction.
    /// </para>
    /// <para>
    /// <b>It throws rather than degrading.</b> A policy whose hash does not match refuses the script, and the
    /// script's whole job is to set the theme before first paint - so the symptom is a flash of the wrong
    /// theme on every cold load, which is exactly the sort of thing nobody reports and nobody notices in a
    /// screenshot. Failing at boot puts it in front of whoever deployed it.
    /// </para>
    /// </remarks>
    private static string ThemeScriptHash(string? webRootPath)
    {
        var indexPath = Path.Combine(webRootPath ?? string.Empty, "index.html");

        if (!File.Exists(indexPath))
        {
            throw new InvalidOperationException(
                $"Cannot serve the SPA: no index.html at '{indexPath}'. The gateway image is built by copying "
                + "the Vite output into wwwroot; an image without it serves nothing but proxied API routes.");
        }

        var match = ThemeScript.Match(File.ReadAllText(indexPath));

        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Cannot build the Content-Security-Policy: '{indexPath}' contains no script marked "
                + "data-theme-preload. That script is injected by the themeCsp() Vite plugin and is hashed "
                + "into script-src; without it the policy would refuse the pre-paint theme script and every "
                + "cold load would flash the wrong theme. Check plugins/theme-csp.ts still injects it.");
        }

        var body = match.Groups["body"].Value;

        return $"sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(body)))}";
    }
}
