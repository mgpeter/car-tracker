var builder = DistributedApplication.CreateBuilder(args);

// A stable password, persisted to user-secrets on first run.
//
// Not cosmetic: WithDataVolume() keeps the data directory between runs, but AddPostgres generates a fresh
// random password each run when you don't supply one. Postgres only reads the password on first
// initialisation, so from the second run onwards the generated password no longer matches the volume and
// every connection fails authentication — the health check never passes and everything with a WaitFor on it
// hangs forever, with no error in the AppHost log. A stable parameter keeps the two in step.
var postgresPassword = builder.AddParameter("postgres-password", secret: true);

// The Postgres version, named here rather than inherited. Left implicit, AddPostgres takes whatever
// Aspire.Hosting.PostgreSQL defaults to - 18.3, and a Debian build, on 13.4.6 - so dev ran a different image
// and a different patch level from the test suite and from the deployment, with nothing naming a version
// anywhere. The same tag string is in tests/CarTracker.Data.Tests/PostgresFixture.cs and
// deploy/docker-compose.yml: one fact, three files, moved by hand.
//
// WithImageTag must come BEFORE WithDataVolume. Aspire reads the configured tag to choose the container-side
// data path, because Postgres 18 moved PGDATA to /var/lib/postgresql/<major>/docker and its VOLUME to the
// parent. Set the tag second and the volume mounts at the 17 path, where the server does not fail: it
// initialises a fresh cluster on the container layer, reports healthy, and hands you an empty database.
//
// The volume is named, and named anew, because the old unnamed one holds a cluster initialised by the Debian
// default image. A cluster records its locale and collation provider, and a musl-based alpine build is not a
// safe reader of a glibc-initialised directory; reusing it would present as the same silent hang the
// paragraph above describes. A new name leaves the old volume orphaned rather than broken.
var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithImageTag("18-alpine")
    .WithDataVolume("cartracker-pgdata-18");

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
