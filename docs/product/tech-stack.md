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
- database_hosting: PostgreSQL container in the same docker-compose stack
- asset_hosting: Local Docker volume, path stored on the Document entity
- deployment_solution: docker-compose (app + Postgres + reverse proxy); .NET Aspire for local orchestration
- code_repository_url: n/a (local only, no remote configured)

## Notes

- **ORM:** EF Core with explicit `IEntityTypeConfiguration<T>` configurations and explicit column types.
- **MCP server:** Hosted in-process in the same ASP.NET Core app over HTTP/SSE, using Microsoft Agent Framework. Not a separate deployable.
- **Fonts are inlined, not CDN-loaded.** `archive/dashboard-design-idea/dashboard.html` inlines them deliberately: under a strict CSP the CDN version silently falls back to system faces. Keep this property.
- **Palette is wired as Tailwind theme tokens** using the field-manual variable names (`--ink #1E241B`, `--paper #E8E2CF`, `--green #5E7A34`, `--orange #B85C29`, `--rust #A23B2E`, `--blue #3E6187`, `--sand #C9B588`) so the `archive/` HTML prototypes port over near-directly.
- **shadcn/ui is copy-in, not a dependency.** Components are owned and restyled to the field-manual identity; the library imposes no visual identity of its own.
- **Documents back up as a folder copy** alongside `pg_dump`, per spec §6. Choosing local-volume-plus-path over Postgres `bytea` keeps dumps light; MinIO was rejected as a third container for a single user.
- **HTTPS is mandatory** in any exposed deployment because the MCP endpoint carries a bearer token.
