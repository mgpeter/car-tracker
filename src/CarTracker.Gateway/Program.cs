using CarTracker.Gateway;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Brings AddServiceDiscovery(), which AddServiceDiscoveryDestinationResolver() below depends on.
builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver()
    // Strip the identity headers a fronting Tailscale proxy (tailscale serve) injects on every request. Their
    // values can contain non-ASCII characters (a display name with diacritics), which .NET refuses to write to
    // an HTTP/1.1 backend — YARP throws before reaching the API and the request 502s. The API authenticates by
    // the Auth0 bearer, never these headers, so removing them on every route is both the fix and correct.
    .AddTransforms(context =>
    {
        context.AddRequestHeaderRemove("Tailscale-User-Name");
        context.AddRequestHeaderRemove("Tailscale-User-Login");
        context.AddRequestHeaderRemove("Tailscale-User-Profile-Pic");
    });

var app = builder.Build();

app.MapDefaultEndpoints();

// In development the SPA is proxied to the Vite dev server by a catch-all route in
// appsettings.Development.json. In production this app owns the built assets instead — a dev server and a
// static bundle are different things, so the mechanism differs even though the URLs do not.
if (!app.Environment.IsDevelopment())
{
    // Before UseStaticFiles, because it has to be in the pipeline by the time the document's response starts.
    // It sets the header on the SPA's HTML only, keyed off the content type - both UseStaticFiles (at /) and
    // MapFallbackToFile (at every deep link) serve index.html, so a path check would catch one and miss the
    // other. This is the policy that used to be a build-time <meta> tag; see SpaHosting for why it moved.
    app.UseSpaCsp();

    app.UseStaticFiles();
}

// Which Auth0 application this deployment uses, read from configuration and handed to the browser. Registered
// in both environments: the dev server proxies everything here through the catch-all below, so without it
// `npm run dev` through the gateway would 404 on /config.js. A literal path outranks that catch-all.
app.MapSpaConfig();

app.MapReverseProxy();

if (!app.Environment.IsDevelopment())
{
    // SPA deep links (/BT53AKJ/fuel) must reach index.html. Registered after MapReverseProxy so /api,
    // /scalar and /openapi win.
    app.MapFallbackToFile("index.html");
}

app.Run();
