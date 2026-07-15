# Technical Stack

- application_framework: ASP.NET Core (.NET 10)
- database_system: PostgreSQL 17
- javascript_framework: React 19 (Vite)
- import_strategy: node
- css_framework: TailwindCSS 4.x
- ui_component_library: Radix UI primitives via shadcn/ui
- fonts_provider: Self-hosted `.woff2` under `public/fonts/`, subset to Latin, served from `'self'` (Oswald, Inter, JetBrains Mono) — DEC-010
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
- **Fonts are self-hosted, not CDN-loaded** — and *self-hosted* is the requirement, not *inlined* (DEC-010).
  The design artifacts inline base64 because they are single self-contained files. The app decodes them to
  `.woff2` and sets `font-src 'self'`, which preserves the CSP property exactly while gaining separate caching
  and dropping ~33% of base64 overhead. The field manual's CDN fonts silently degrade to system faces under a
  strict CSP; that is the regression to avoid, and `'self'` avoids it.
- **Tokens are one semantic layer**, ported from `archive/dashboard-full-claude-design/theme.css`: `--bg`,
  `--surface`, `--fg`, `--muted`, `--line`, `--head-bg`, `--sand`, `--accent`, `--ok`, `--soon`, `--due`,
  `--info` and their `-wash` variants, wired to Tailwind with `@theme inline` so dark mode still resolves at
  runtime.

  **Corrected 2026-07-15.** This line previously claimed the tokens use the field manual's raw palette names
  (`--ink`, `--paper`, `--green`, `--rust`…) — the exact claim DEC-005 retracted on 2026-07-14 and which this
  file was never updated to match, so the two documents said opposite things for a day. Both are wrong anyway:
  verified against all three files, **neither design concept contains a raw-palette variable**. The palette
  exists only in `archive/…green-lane-field-manual.html`; the concepts inherited it **as hex values, not as
  variables**. There is one layer, not two, and nothing to flatten.
- **`--accent` is structural only — never status.** The original concept said so in a comment beside the token;
  the new `theme.css` dropped it. Restore it in `tokens.css`: it is the rule that keeps orange (rules,
  eyebrows, section marks) off the green/amber/rust status axis, and losing the comment is how it gets broken.
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
