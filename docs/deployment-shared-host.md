# Deploying Cambelt as a tenant of a shared host

This is the app's side of a boundary. It describes what Cambelt needs from a machine it does not own, and the
handful of things that machine can get wrong which Cambelt cannot detect from inside itself.

It is **not** a guide to building the host. Since DEC-020 the VM, the reverse proxy, the PostgreSQL server,
the networks and the backups live in their own repository, because the same box now runs several unrelated
side projects and a host with more than one tenant on it cannot be defined inside one of them.

- **The live host is Asgard**, an Azure VM defined in
  [`usualexpat-infra`](https://github.com/mgpeter/usualexpat-infra). It serves
  [cambelt.app](https://cambelt.app) behind Caddy with a Let's Encrypt certificate, and has done since
  2026-08-21.
- **The normative contract is that repository's `docs/tenant-contract.md`**, not this file. Where the two
  disagree, it wins, and this file should be corrected. It is deliberately not copied here: a definition that
  exists in two places agrees in neither, which is the failure DEC-020 exists to prevent.
- For the self-contained install - one box bringing its own Postgres, its own updater and its own backups -
  see [`deployment-synology.md`](deployment-synology.md), which documents a real, working deployment and is
  the reference for the `standalone` profile.

---

## Two profiles, one compose file

`deploy/docker-compose.yml` describes **a tenant**: `webapi` and `gateway`, and nothing else. No proxy, no
database server, no updater, no backup job, no published ports.

Everything a lone box also needs sits behind a `standalone` compose profile in that same file, with
`deploy/docker-compose.standalone.yml` turning the two networks back from external to Compose-created and
giving the gateway a published port.

| | Tenant (default) | `standalone` |
|---|---|---|
| Started with | `docker compose up -d` | `-f docker-compose.yml -f docker-compose.standalone.yml --profile standalone up -d` |
| PostgreSQL | the host's, reached as `postgres` | a `postgres` container in this stack |
| TLS | the host's proxy | none |
| Ports published | **none** | `${GATEWAY_PORT}:8080` |
| Networks | `edge`, `data-cambelt`, both external | created by Compose |
| Updates, backups | the host's Watchtower and restic | a `watchtower` and a `db-backup` sidecar here |

Both the override file **and** the `--profile` flag are needed for standalone. They do different jobs: a
profile can gate a whole service, an override cannot conveniently add one.

On the shared host the checkout lives at `/srv/data/cambelt/src` and is started as:

```sh
docker compose --project-directory /srv/data/cambelt \
  -f /srv/data/cambelt/src/deploy/docker-compose.yml \
  --env-file /srv/infra/tenants/cambelt/.env up -d
```

---

## What Cambelt expects the host to have made first

None of these are created by the compose file, and it will refuse to start rather than invent them - which is
the point. A tenant that lets Compose create its own `data-cambelt` gets a fresh, empty network with no
Postgres anywhere on it, and the symptom reads as a DNS fault ("could not translate host name postgres") while
being a lifecycle bug.

- **Two networks.** `edge`, which the host's proxy is on, and `data-cambelt`, which the shared PostgreSQL is
  on. Only `gateway` attaches to `edge`, with the alias **`cambelt-gateway`** - Caddy targets the alias, never
  a container name, so this repository can rename or move the service without touching the host's config. The
  `webapi` is on the data network only.
- **A database and an owning role**, both named `cambelt`. A shared *server* is not a shared *database*: the
  app assumes it is the only writer of its own, and assumes no superuser.
- **`/srv/data/cambelt/`**, writable, with `documents/` beneath it. That directory is `DATA_ROOT`, and it is
  exactly what the host's backup captures.
- **The DNS record and a valid certificate**, in that order and before anyone is told the address. `.app` is
  HSTS-preloaded at the TLD, so there is no cleartext to fall back to: a failed certificate is an unreachable
  site, not a degraded one.
- **The `https://` origin registered in Auth0** - Allowed Callback URLs, Allowed Logout URLs and Allowed Web
  Origins. This repository owns the fact that it is required rather than the timing: an unregistered origin
  fails at the login redirect with a message naming the *tenant*, which reads as an Auth0 fault rather than as
  a deployment step nobody took.

### Per-tenant data networks are isolation, not tidiness

One shared `data` network would let any app on the box open a socket to any neighbour's database. With a
network per tenant it cannot reach one at all. That is worth the extra line in the host's provisioning.

### `Maximum Pool Size` is not optional on a shared server

Npgsql defaults to 100 connections *per connection string*; the cluster is configured for 150 *in total*. Two
tenants at the default can starve the third, and the victim is whichever one restarts last. The compose file
sets `Maximum Pool Size=20` inside `ConnectionStrings__cartrackerdb`, which is the variable the running
container reads. `CARTRACKER_CONNECTION` is consumed only by `DesignTimeDbContextFactory` for `dotnet ef`, so
setting it there changes nothing about the running app.

---

## The two things the host must get right that Cambelt cannot check

Written here because they fail invisibly, and because a tenant has no way to verify either from its own side.

### 1. `/mcp` must not be buffered

`/mcp` is MCP Streamable HTTP: a long-lived response that is read as it arrives. A reverse proxy that buffers
responses by default turns a working stream into a request that appears to hang, on the one feature that most
wanted a public address in the first place. Caddy needs `flush_interval -1` on the `reverse_proxy` block;
other proxies have their own spelling of it.

The app's half of this is already in the compose file and has been since before there was a host:
`Kestrel__EndpointDefaults__Protocols: "Http1"` on the gateway, and YARP pinned to HTTP/1.1
(`ReverseProxy__Clusters__webapi__HttpRequest__Version: "1.1"` with `RequestVersionExact`). **Those two are
the first place to look at a 502**, and the reason is specific: the gateway's Kestrel listens cleartext and
advertises h2c but mishandles it, so proxying over HTTP/2 cleartext fails every request.

### 2. Documents must travel in the same snapshot as the dump

`DocumentStore` writes content-addressed bytes under `${DATA_ROOT}/documents` and the only index of them is
the `documents` table. A database dump is internally consistent by construction, so restoring one **without**
the corresponding files produces rows pointing at nothing while every check anyone would naturally run passes.

The host's descriptor for this tenant therefore declares the mapping, and the restore drill asserts it:

```sh
BLOB_PATHS="documents"
BLOB_ROOT="documents"
BLOB_INDEX_SQL='SELECT file_path, sha256 FROM documents'
```

Two app facts that make that query correct, and that will go stale here before they go stale in the code:
`CarTrackerDbContext` calls `UseSnakeCaseNamingConvention()`, so every column is snake_case and none needs
quoting; and `sha256` is nullable. The live database is the only authority - `\d documents`.

**The backup depends on how this app writes**, so changing that changes the argument. On create the app writes
the bytes and *then* inserts the row; on delete it deletes the row and *then* removes the file. The file
strictly outlives the row on both edges, which is what makes a database-first snapshot safe: it can contain a
file with no row (a wasted block) but not a row with no file, except in the seconds between the two steps of a
user's own delete. **If document writes ever become mutable rather than append-mostly, this file is affected
and so is the host's drill.**

### And one thing the app does own: bind mounts stay under `DATA_ROOT`

Bytes written anywhere else are not backed up, and nothing will say so until a restore comes up short.
Inverted, the same rule keeps the rendered `.env` **outside** `/srv/data/cambelt/`: that tree is what gets
copied off-box, so a secrets file inside it is a secrets file in the backups.

---

## Configuration

Every key is documented in `deploy/.env.example`, which is the authority; this is only the shape of it. On the
shared host the values come from the host (rendered from Key Vault, or set in the deployment tool's
configuration), never from a file in this repository.

| Group | Keys | Notes |
|---|---|---|
| Image | `DOCKERHUB_USER`, `TAG` | `TAG` names a **channel**, see below |
| Database | `POSTGRES_PASSWORD`, `PGDATABASE`, `PGUSER` | assembled into `ConnectionStrings__cartrackerdb` |
| Storage | `DATA_ROOT` | `documents/` beneath it |
| Auth0 (API) | `AUTH0_AUDIENCE`, `AUTH0_AUTHORITY` | the audience must match the SPA's |
| Auth0 (SPA) | `AUTH0_SPA_DOMAIN`, `AUTH0_SPA_CLIENT_ID`, `AUTH0_AUDIENCE` | served as `/config.js`, and named in the CSP |
| Auth0 (Management) | `AUTH0_MANAGEMENT_CLIENT_ID`, `AUTH0_MANAGEMENT_CLIENT_SECRET` | **the silent pair** - see below |
| Sign-up | `SIGNUP_MODE`, `SIGNUP_ALLOWED_EMAILS`, `SIGNUP_ALLOWED_DOMAINS` | blank mode means **Open** since 0.24.0 |
| Plans | `PLANS_COMP_EMAILS`, `PLANS_COMP_DOMAINS`, `PLANS_FREE_*`, `PLANS_PRO_*` | comps blank means nobody is on Pro |
| Assistant | `CHAT_API_KEY`, `CHAT_MODEL`, `CHAT_DAILY_TOKENS_*` | absent key means the feature is off |
| Lookup | `VES_API_KEY`, `MOT_*` | absent means the button is not rendered |

**Three keys have three different polarities and it is worth knowing which is which.** An absent `CHAT_API_KEY`
or `VES_API_KEY` switches a feature *off* and the UI hides it. An absent `PLANS_COMP_EMAILS` puts *everybody*
on the free tier, which is how `cambelt.app` lost its assistant on its own first 0.24.0 deploy. An absent
`SIGNUP_MODE` leaves the door **open** - the opposite of what it meant before 0.24.0, and the one that a stale
`.env` gets wrong in the dangerous direction.

The WebApi logs a `Sign-up posture:` line at every boot precisely so a posture is a stated fact rather than
something inferred from a refusal. Read it after any config change.

### `Auth0__Management__*` is the pair that fails silently

Every other misconfiguration here announces itself: a wrong password is an auth error, a wrong database name
is a connection error. A blank Management credential means no address can be resolved behind a login, so
**nobody new is admitted, account deletion refuses, and the site stays perfectly healthy while doing it.** The
host's drift check asserts these two are non-empty *inside the running container*, which is a better guarantee
than asserting it at render time, because a configuration field left blank looks identical to one filled in.

### Diagnosing a tenant that starts and misbehaves

```sh
docker compose exec webapi env | grep -E 'Auth0|Signup|Plans'
```

- Key **absent** entirely: the compose file on the box is stale, so `${...}` interpolation has nowhere to put
  the value.
- Key **present but empty**: the `.env` is not being read, or the value is blank at source.

Two different faults, two different fixes, and the difference is one command. `GET /api/meta` answers part of
it anonymously in one request (`identityDeletionConfigured`, `chatConfigured`, `vehicleLookupConfigured`).

**Never `docker restart` after a config change** - it preserves the old environment block. Use
`docker compose up -d --force-recreate webapi`, then read the value back out of the running container.

---

## Releases, and what `TAG` actually does

- `:edge` is the tip of `main`, republished on every commit that can affect an image. The dogfooding NAS runs
  it, because that is what finds the bugs.
- `:stable` (and `:latest`, the same digest) moves only when a `v<version>` git tag is pushed, which retags a
  digest CI already built rather than rebuilding it.
- `:0.27.1` and the like never move at all.

**Pin an exact version on a public site.** A semver tag is immutable, so the site changes when you change
`TAG` and at no other time - which also means Watchtower will not move it, and that is the intent: deploys are
deliberate.

> **Watchtower follows the tag a container was *created* from.** Editing `.env` and restarting leaves the
> container on its old channel with nothing visible to say so. `docker compose up -d` is what changes it, and
> `docker inspect --format '{{.Config.Image}}' <container>` is what answers "which channel is this actually
> on".

## Things that will look like bugs and are not

- **The login page says `usualexpat.uk.auth0.com`.** That is the Auth0 tenant, which is a different name from
  the product and from the deployment. Nothing is misconfigured; the address bar returns to `cambelt.app`
  after the redirect.
- **Migrations do not run.** The WebApi applies them on startup in **Development only**. On a shared host the
  schema is moved deliberately, not by a container recreating itself at 3am.
- **`/api/meta` reports a version older than the tag you pushed.** The assembly version comes from `VERSION`
  at build time; a `:stable` retag does not rebuild, so the two agree by construction. If they disagree, the
  image predates `Directory.Build.props` being copied before `dotnet restore` in the Dockerfile.
