# Technical Stack

- application_framework: ASP.NET Core (.NET 10)
- database_system: PostgreSQL 17
- javascript_framework: React 19 (Vite)
- import_strategy: node
- css_framework: TailwindCSS 4.x
- ui_component_library: Radix UI primitives via shadcn/ui
- fonts_provider: Self-hosted, inlined as base64 data URIs (Oswald, Inter, JetBrains Mono)
- icon_library: Lucide
- application_hosting: Self-hosted Docker container
- database_hosting: PostgreSQL 17 container in the same docker-compose stack
- asset_hosting: Local Docker volume, path stored on the Document entity
- deployment_solution: docker-compose (gateway + API + Postgres); .NET Aspire 13.4.6 for local orchestration
- api_gateway: CarTracker.Gateway — YARP 2.3.0 + Microsoft.Extensions.ServiceDiscovery.Yarp 10.7.0 (DEC-009)
- api_documentation: Scalar.AspNetCore 2.16.11 at /scalar, over Microsoft.AspNetCore.OpenApi
- authentication: Static API key in configuration, sent as X-Api-Key (DEC-009)
- code_repository_url: n/a (local only, no remote configured)

## Notes

- **ORM:** EF Core with explicit `IEntityTypeConfiguration<T>` configurations and explicit column types.
- **MCP server:** Hosted in-process in the same ASP.NET Core app over HTTP/SSE, using Microsoft Agent Framework. Not a separate deployable.
- **Fonts are inlined, not CDN-loaded.** `archive/dashboard-design-idea/dashboard.html` inlines them deliberately: under a strict CSP the CDN version silently falls back to system faces. Keep this property.
- **Palette is wired as Tailwind theme tokens** using the field-manual variable names (`--ink #1E241B`, `--paper #E8E2CF`, `--green #5E7A34`, `--orange #B85C29`, `--rust #A23B2E`, `--blue #3E6187`, `--sand #C9B588`) so the `archive/` HTML prototypes port over near-directly.
- **shadcn/ui is copy-in, not a dependency.** Components are owned and restyled to the field-manual identity; the library imposes no visual identity of its own.
- **Documents back up as a folder copy** alongside `pg_dump`, per spec §6. Choosing local-volume-plus-path over Postgres `bytea` keeps dumps light; MinIO was rejected as a third container for a single user.
- **HTTPS is mandatory** in any exposed deployment because the MCP endpoint carries a bearer token.
- **One origin, so no CORS.** The gateway serves the app at `/` and proxies `/api`, `/scalar` and `/openapi` to
  the API — in development exactly as in production. CORS is absent by design, not by omission; if it ever
  becomes necessary, something has bypassed the gateway and that is the bug to fix.
- **Central package management with transitive pinning.** Note `Aspire.Hosting.AppHost` deliberately has **no**
  `PackageVersion`: `Aspire.AppHost.Sdk` injects it implicitly, and centrally versioning an implicit reference
  is NU1009. Its version comes from `global.json`.
- **`Microsoft.OpenApi` is pinned to 2.10.0** to override the vulnerable 2.0.0 that `Microsoft.AspNetCore.OpenApi`
  depends on (NU1903). Remove the pin once that dependency moves.
