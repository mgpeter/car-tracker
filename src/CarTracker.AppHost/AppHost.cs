var builder = DistributedApplication.CreateBuilder(args);

// A stable password, persisted to user-secrets on first run.
//
// Not cosmetic: WithDataVolume() keeps the data directory between runs, but AddPostgres generates a fresh
// random password each run when you don't supply one. Postgres only reads the password on first
// initialisation, so from the second run onwards the generated password no longer matches the volume and
// every connection fails authentication — the health check never passes and everything with a WaitFor on it
// hangs forever, with no error in the AppHost log. A stable parameter keeps the two in step.
var postgresPassword = builder.AddParameter("postgres-password", secret: true);

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume();

var database = postgres.AddDatabase("cartrackerdb");

var webApi = builder.AddProject<Projects.CarTracker_WebApi>("webapi")
    .WithReference(database)
    .WaitFor(database);

// Vite dev server. AddViteApp runs `npm run dev -- --port <dynamic>` and installs packages itself.
var webApp = builder.AddViteApp("webapp", "../CarTracker.WebApp");

// The single public origin. Everything the browser touches goes through here, in development exactly as on
// the NAS — one origin, so CORS never enters the picture (DEC-009).
//
// WithReference injects the service address; WaitFor orders startup. Both are needed: without WaitFor the
// gateway happily serves 502s while the things behind it are still booting.
builder.AddProject<Projects.CarTracker_Gateway>("gateway")
    .WithReference(webApi)
    .WaitFor(webApi)
    .WithReference(webApp)
    .WaitFor(webApp)
    .WithExternalHttpEndpoints();

builder.Build().Run();
