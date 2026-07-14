var builder = WebApplication.CreateBuilder(args);

// Brings AddServiceDiscovery(), which AddServiceDiscoveryDestinationResolver() below depends on.
builder.AddServiceDefaults();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.MapDefaultEndpoints();

// In development the SPA is proxied to the Vite dev server by a catch-all route in
// appsettings.Development.json. In production this app owns the built assets instead — a dev server and a
// static bundle are different things, so the mechanism differs even though the URLs do not.
if (!app.Environment.IsDevelopment())
{
    app.UseStaticFiles();
}

app.MapReverseProxy();

if (!app.Environment.IsDevelopment())
{
    // SPA deep links (/BT53AKJ/fuel) must reach index.html. Registered after MapReverseProxy so /api,
    // /scalar and /openapi win.
    app.MapFallbackToFile("index.html");
}

app.Run();
