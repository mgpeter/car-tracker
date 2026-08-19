# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-08-11-cambelt-azure-deployment/spec.md

Everything inside the repository. **The host left on 2026-08-18** (DEC-020): the VM, the proxy, the shared
PostgreSQL server and the backup pull belong to a separate hosting repository, so
`sub-specs/infrastructure-spec.md` and `sub-specs/backup-and-restore.md` are handover material rather than
work items. Parts 2 to 4 below were rewritten for that boundary; Part 1 shipped as written.

---

## Part 1 - The rename

> **Shipped 2026-08-17, and the table below undercounts it by two.** The garage *footer* prose still opened
> "Self-hosted, and your garage is yours", and `ChatSystemPrompt.cs` introduced the assistant as "the
> assistant inside Car Tracker" - both found by the guard test rather than by reading. See task 1.1.

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
  landing page - consider whether that guard's word list should extend to the garage.

### What does not change, and why

Nothing internal. This is the load-bearing half of the decision and needs to be written down where the next
person looks, because "rename the app" reads as "rename everything":

| Identifier | Why it stays |
|---|---|
| `CarTracker.*` - nine project namespaces | Invisible to users. A rename touches every file, every `using`, the solution, both Dockerfiles and CI, for no user-visible gain |
| `cartracker-webapi`, `cartracker-gateway` images | Renaming breaks the running NAS deployment until its `.env` is updated, and orphans the published tag history |
| `cartrackerdb` | Renaming needs a migration or a fresh database |
| `cartracker.api` - the Auth0 API audience | Changing it **invalidates every existing access token** and requires reconfiguring the tenant and `lib/authConfig.ts` |
| `cartracker.settings` - the localStorage key | Changing it **silently resets everyone's theme and MPG/L-100 km preference**. A rename is not worth a preference reset the user cannot connect to a cause |
| `CARTRACKER_CONNECTION` | Documented in CLAUDE.md and used by `dotnet ef` |

The tension is real and should be acknowledged rather than hidden: the codebase will say `CarTracker` while
the product says Cambelt. That is the normal state of a renamed product, and the alternative costs invalidated
tokens and reset preferences for a difference no user can see.

---

## Part 2 - The proxy is the host's, and what that leaves here

**Superseded 2026-08-18 (DEC-020).** This part used to add a `caddy` service to `deploy/docker-compose.yml`,
on 80 and 443, with its certificate store on `${DATA_ROOT}`. The proxy now belongs to the host, because a box
running several projects has one proxy with one certificate store, not one per tenant.

What the app does instead: the `gateway` joins the external **`edge`** network, publishes nothing, and the
host's proxy routes a hostname to `gateway:8080`. Under the `standalone` profile the old shape is still
available in full, Caddy included, which is what the NAS and a laptop run.

### The gateway needs no changes, and the reason is already in the file

`docker-compose.yml` already sets, with comments explaining both:

- `Kestrel__EndpointDefaults__Protocols: "Http1"` - because "a fronting proxy (Tailscale serve, or any reverse
  proxy) may forward over HTTP/2 cleartext", which the in-container Kestrel mishandles into a 502.
- `ReverseProxy__Clusters__webapi__HttpRequest__Version: "1.1"` and `VersionPolicy: "RequestVersionExact"` for
  the gateway to webapi hop.

Whatever the host puts in front is exactly the "any reverse proxy" that comment anticipates. **Reuse this, do
not re-solve it** - and if a 502 appears after the app goes behind a proxy, these two settings are the first
place to look, not the last.

### Streaming, which is the one thing the host can break invisibly

`/mcp` is Streamable HTTP with long-lived responses. Caddy does not buffer proxied responses by default and
neither does Traefik, so this should behave as it does locally - **but it is the one thing local testing
cannot prove**, so it is an explicit verification step rather than an assumption, and it is written into the
tenant contract rather than left in a spec the host's operator will never read.

It is also the specific reason to leave Cloudflare's orange cloud off at first: its request cap and buffering
behaviour are a second variable to eliminate, and the proxy can be switched on later once `/mcp` is known-good
through the origin proxy alone.

---

## Part 3 - The compose file as a tenant

| Setting | `standalone` profile (NAS, laptop) | Shared host |
|---|---|---|
| `postgres` | In the stack | Not in the stack; a database on the host's server |
| `caddy` | In the stack | Not in the stack; the host's proxy |
| `watchtower` | In the stack | Not in the stack; one per host |
| `db-backup` | In the stack | Not in the stack; one schedule per host, covering every project |
| `GATEWAY_PORT` | Published on the host | **Not published at all** |
| Networks | Default bridge | External `edge` and `data-cambelt` |
| `DATA_ROOT` | `/volume1/docker/cartracker` | `/srv/cambelt` |
| Secrets | Hand-written `.env` | Hand-written `.env`, whatever the host writes it from |

**The `standalone` profile is what keeps this from being a one-way door.** `docker compose --profile
standalone up -d` is today's stack, unchanged, so `docs/deployment-synology.md` documents something that still
works and a fresh checkout still comes up in one command.

### Two networks, not one

`edge` carries proxy traffic; `data-cambelt` carries database traffic. **Per-project data networks are the
point**: an app on the same host cannot open a socket to a neighbour's database at all. A single shared `data`
network would leave every project one leaked password away from every other project's data, which is isolation
given away for nothing, since the Postgres container can join any number of networks.

### A shared server is not a shared database

One database and one role per project (DEC-020). Two consequences the app has to respect:

- **Set `Maximum Pool Size` explicitly** in `CARTRACKER_CONNECTION`. Npgsql defaults to 100 per connection
  string and PostgreSQL defaults to 100 for the whole server, so two projects at defaults can exhaust it and
  the third gets "too many clients".
- **Nothing in the app assumes it owns the server**, and nothing should start doing so. Migrations run on
  startup in Development only, which is already the rule.

### Auth0

Register the public origin in **Allowed Callback URLs**, **Allowed Logout URLs** and **Allowed Web Origins**,
keeping the NAS origin registered while both run. The timing is the host's; **the requirement is the app's**,
and it goes in the tenant contract, because an unregistered origin fails at the login redirect with a message
about the tenant rather than about the deployment.

**The client needs no rebuild.** `redirect_uri` is computed from `window.location.origin`, which
`deploy/Dockerfile.gateway` documents as deliberate ("the build is origin-agnostic"). Verify the built CSP's
`connect-src` still names the Auth0 tenant - that value *is* baked at build time, and it is the one piece of
this that a new origin could break.

The login page will still show `usualexpat.uk.auth0.com`. Known, out of scope, and worth a line in the
deployment doc so it is not rediscovered as a bug.

### Watchtower stays, but one per host

Auto-updating a public production app from `:latest` deserves scrutiny, and survives it: since 2026-08-09 CI
**publishes nothing unless `VERSION` changed**, so `:latest` moves only on a deliberate release. Watchtower is
therefore a deployment mechanism triggered by an intentional act, not a continuous pull of whatever is newest.

On a shared host it is the host's, watching every project's labelled containers. Running one per project would
mean N daemons polling the same registry and racing to recreate the same containers. Postgres, the backup
sidecar and the proxy stay unlabelled and are never auto-updated - the database and the thing holding the
certificates are updated by hand or not at all.

Pinning `TAG` to a version in `.env` remains available for freezing a deploy, and the deployment doc should say
so next to the Watchtower section.

> **The failure this arrangement already produced once, and which the split makes more likely.** CLAUDE.md
> records the NAS running a *copy* of `deploy/docker-compose.yml` that nothing keeps current, a Container
> Manager project holding a third copy, and **Watchtower recreating from the running container's spec rather
> than from the compose file** - which is how 0.13.1 reached production with `Auth0__Management__*` empty. A
> tenant compose file on somebody else's host is the same shape with one more hop, so the diagnosis stays in
> the deployment doc: a key **absent** from `docker compose exec webapi env` means the YAML on the host is
> stale, **present but empty** means the `.env` is not being read.

---

## Part 4 - Documentation

**`docs/deployment-shared-host.md`** - the tenant contract, and the file this part used to call
`deployment-azure.md`. The rename is not cosmetic: the contract is the same on any host, and naming it for one
provider is how it ends up rewritten rather than reused. It states the two external networks and who creates
them, the database and role the app expects, `DATA_ROOT` and the `documents` directory beneath it, the
environment keys, the two profiles and what each is for, and the two things the host must get right that
Cambelt cannot check for itself:

1. **`/mcp` must not be buffered.**
2. **The documents directory must travel with the dumps.** A dump restored without `${DATA_ROOT}/documents`
   gives `Document` rows pointing at nothing, which is the warning `docs/deployment-synology.md:128-130`
   already makes and which no host can infer by looking at the containers.

`docs/deployment-synology.md` **stays** - it documents a working deployment, and it is now also the reference
for the `standalone` profile. Add a line at the top of each pointing at the other.

`docs/mcp-connect.md` gains the public `/mcp` endpoint once there is one. This is the first time the MCP
connection recipe can offer an address reachable from outside the LAN, which is most of the practical point of
the whole exercise.

Update `README.md` §6 and `docs/product/roadmap.md`'s HTTPS lines to say where HTTPS is met - **not that it is
met here**. The gate stays open in this repository and this repository can no longer observe it closing, which
was already half-true (the roadmap has always said "no code change, which is why nothing in this repository
will tell you it has not been done") and is now structural. **DEC-020** records the split; the priced rejection
of Container Apps and App Service, and the plain statement that Azure is the most expensive mainstream option
for this workload, are preserved in `sub-specs/infrastructure-spec.md` for whoever writes the hosting
repository's own decision record.
