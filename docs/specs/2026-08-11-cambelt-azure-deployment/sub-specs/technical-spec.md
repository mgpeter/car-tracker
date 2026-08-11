# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-08-11-cambelt-azure-deployment/spec.md

Everything inside the repository. The Azure resources are in
@docs/specs/2026-08-11-cambelt-azure-deployment/sub-specs/infrastructure-spec.md

---

## Part 1 — The rename

### What changes

| File | Current | Becomes |
|---|---|---|
| `src/CarTracker.WebApp/index.html:9` | `<title>cartracker-webapp</title>` | `<title>Cambelt</title>` |
| `src/CarTracker.WebApp/src/shell/TopNav.tsx:52` | `Car Tracker` | `Cambelt` |
| `src/CarTracker.WebApp/src/auth/LandingPage.tsx:35` | eyebrow `Car Tracker` | `Cambelt` |
| `src/CarTracker.WebApp/src/screens/GaragePage.tsx:41` | eyebrow `Car Tracker · self-hosted` | `Cambelt` |
| `src/CarTracker.WebApp/public/favicon.svg` | current mark | a mark that suits the name |
| `README.md:1` | `# Car Tracker` | `# Cambelt` |
| `docs/**` | prose references | Cambelt, where the *product* is meant |

Two of these are pre-existing defects rather than rename collateral:

- **`index.html:9` is the Vite scaffold default.** It has never been set. It is the browser tab, the bookmark
  name and the value a link preview falls back to.
- **`GaragePage.tsx:41` claims "self-hosted"**, which becomes false on Azure. CLAUDE.md records the stale
  self-hosted line as removed with the landing-page rewrite; that removed a different one, on the garage
  footer. This survived, and `LandingPage.test.tsx`'s jargon guard would have caught it had it lived on the
  landing page — consider whether that guard's word list should extend to the garage.

### What does not change, and why

Nothing internal. This is the load-bearing half of the decision and needs to be written down where the next
person looks, because "rename the app" reads as "rename everything":

| Identifier | Why it stays |
|---|---|
| `CarTracker.*` — nine project namespaces | Invisible to users. A rename touches every file, every `using`, the solution, both Dockerfiles and CI, for no user-visible gain |
| `cartracker-webapi`, `cartracker-gateway` images | Renaming breaks the running NAS deployment until its `.env` is updated, and orphans the published tag history |
| `cartrackerdb` | Renaming needs a migration or a fresh database |
| `cartracker.api` — the Auth0 API audience | Changing it **invalidates every existing access token** and requires reconfiguring the tenant and `lib/authConfig.ts` |
| `cartracker.settings` — the localStorage key | Changing it **silently resets everyone's theme and MPG/L-100 km preference**. A rename is not worth a preference reset the user cannot connect to a cause |
| `CARTRACKER_CONNECTION` | Documented in CLAUDE.md and used by `dotnet ef` |

The tension is real and should be acknowledged rather than hidden: the codebase will say `CarTracker` while
the product says Cambelt. That is the normal state of a renamed product, and the alternative costs invalidated
tokens and reset preferences for a difference no user can see.

---

## Part 2 — Caddy in front of the gateway

A `caddy` service in `deploy/docker-compose.yml`, on 80 and 443, reverse-proxying to `gateway:8080`, with a
`Caddyfile` and volumes for its certificate and config data on `${DATA_ROOT}`.

```
cambelt.app {
    reverse_proxy gateway:8080
}
```

Caddy obtains and renews Let's Encrypt certificates automatically. Its data volume **must** be a bind mount on
`${DATA_ROOT}` like the others — losing the certificate store on a container recreate means re-issuing on
every restart and meeting Let's Encrypt's rate limits at the worst moment.

### The gateway needs no changes, and the reason is already in the file

`docker-compose.yml` already sets, with comments explaining both:

- `Kestrel__EndpointDefaults__Protocols: "Http1"` — because "a fronting proxy (Tailscale serve, or any reverse
  proxy) may forward over HTTP/2 cleartext", which the in-container Kestrel mishandles into a 502.
- `ReverseProxy__Clusters__webapi__HttpRequest__Version: "1.1"` and `VersionPolicy: "RequestVersionExact"` for
  the gateway→webapi hop.

Caddy is exactly the "any reverse proxy" that comment anticipates. **Reuse this, do not re-solve it** — and if
a 502 appears after adding Caddy, these two settings are the first place to look, not the last.

### Streaming

`/mcp` is Streamable HTTP with long-lived responses. Caddy does not buffer proxied responses by default, so
this should behave as it does locally — but it is the one thing that local testing cannot prove, so it is an
explicit verification step rather than an assumption. This is also the specific reason Cloudflare's proxy is
left off: its request cap and buffering behaviour are a second variable to eliminate, and the orange cloud can
be switched on later once `/mcp` is known-good through Caddy alone.

---

## Part 3 — Compose and configuration changes

| Setting | NAS today | Azure |
|---|---|---|
| `GATEWAY_PORT` | `8082`, published on the host | Bound to `127.0.0.1` only; Caddy is the sole public listener |
| `DATA_ROOT` | `/volume1/docker/cartracker` | `/srv/cambelt` |
| Auth0 origin | `http://synologynas:8082` | `https://cambelt.app` |
| Secrets | Hand-written `.env` | Written by cloud-init from Key Vault |

`deploy/.env.example` gains the Azure values as documented alternatives rather than replacing the Synology
ones — both deployments are real.

### Auth0

Register `https://cambelt.app` in **Allowed Callback URLs**, **Allowed Logout URLs** and **Allowed Web
Origins**. Keep the NAS origin registered while both run.

**The client needs no rebuild.** `redirect_uri` is computed from `window.location.origin`, which
`deploy/Dockerfile.gateway` documents as deliberate ("the build is origin-agnostic"). Verify the built CSP's
`connect-src` still names the Auth0 tenant — that value *is* baked at build time, and it is the one piece of
this that a new origin could break.

The login page will still show `usualexpat.uk.auth0.com`. Known, out of scope, and worth a line in the
deployment doc so it is not rediscovered as a bug.

### Watchtower stays

Auto-updating a public production app from `:latest` deserves scrutiny, and survives it: since 2026-08-09 CI
**publishes nothing unless `VERSION` changed**, so `:latest` moves only on a deliberate release. Watchtower is
therefore a deployment mechanism triggered by an intentional act, not a continuous pull of whatever is newest.

Postgres, the backup sidecar and Caddy stay unlabelled and are never auto-updated — the database and the thing
holding the certificates are updated by hand or not at all.

Pinning `TAG` to a version in `.env` remains available for freezing a deploy, and the deployment doc should say
so next to the Watchtower section.

---

## Part 4 — Documentation

**`docs/deployment-azure.md`**, modelled on `docs/deployment-synology.md`'s structure: one-time setup (Azure,
Cloudflare, Auth0, GitHub), first deploy, releasing, backups and restore, auto-updates, verifying persistence,
troubleshooting.

`docs/deployment-synology.md` **stays** — it documents a working deployment. Add a line at the top of each
pointing at the other.

`docs/mcp-connect.md` gains the `https://cambelt.app/mcp` endpoint. Worth stating in the spec that this is the
first time the MCP connection recipe can offer an address reachable from outside the LAN, which is most of the
practical point of the whole exercise.

Update `README.md` §6 and `docs/product/roadmap.md:208-210` to record HTTPS as met, and CLAUDE.md's state of
play. Record a DEC covering the Azure/VM/Bicep/Caddy choices together, including the priced rejection of
Container Apps and App Service, and the plain statement that Azure is not the cheapest option and was chosen
anyway.
