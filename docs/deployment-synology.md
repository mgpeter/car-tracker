# Deploying CarTracker on a Synology NAS

CarTracker deploys as three containers behind a single origin — **gateway** (serves the built SPA and proxies
the API), **webapi**, and **postgres** — plus a **db-backup** sidecar and **watchtower** for auto-updates.
Images are published to Docker Hub by CI (or the local release scripts); the NAS pulls them.

```
git push main ─► GitHub Actions (test + contract gate, then publish) ─► Docker Hub
NAS: watchtower ─polls Docker Hub─► recreates webapi + gateway on a new :latest
     browser ─http://synologynas:8082─► gateway ─► SPA (static) + /api,/mcp ─► webapi ─► postgres (bind mount)
```

The database lives on a **host bind mount** (`${DATA_ROOT}/pgdata`), so it survives `docker compose down`,
`down -v`, image rebuilds and container recreation. Only deleting the host folder removes it.

---

## One-time setup

### 1. GitHub (for CI publishing)

- Repository **Settings → Secrets and variables → Actions**, add:
  - `DOCKERHUB_USERNAME` — your Docker Hub username (e.g. `mgpeter`).
  - `DOCKERHUB_TOKEN` — a Docker Hub **access token** (Docker Hub → Account Settings → Security).
- Create two **public** Docker Hub repositories: `cartracker-webapi` and `cartracker-gateway`. (They contain no
  secrets — all config is injected as environment variables — so public keeps the NAS pull credential-free.)

The next push to `main` runs CI; if it's green, the `publish` job builds and pushes both images.

### 2. Auth0

Confirm the application (`AYVXSt9aa5rz4kHFYs3KZ5HqYfBNkPKp`, tenant `usualexpat.uk.auth0.com`) has
`http://synologynas:8082` in **Allowed Callback URLs**, **Allowed Logout URLs** and **Allowed Web Origins**, and
that **Refresh Token** rotation is enabled (Application grant types + API → Allow Offline Access). The browser
origin is whatever you type in the address bar, so it must match exactly.

### 3. NAS

- Enable **Container Manager** and **SSH** (Control Panel → Terminal & SNMP).
- Create the data folders on a volume:
  ```sh
  mkdir -p /volume1/docker/cartracker/pgdata /volume1/docker/cartracker/backups
  ```
- Copy `deploy/docker-compose.yml` to the NAS (e.g. `/volume1/docker/cartracker/`), and create a `.env` beside
  it from `deploy/.env.example`:
  ```sh
  DOCKERHUB_USER=mgpeter
  TAG=latest
  POSTGRES_PASSWORD=<a strong password>
  DATA_ROOT=/volume1/docker/cartracker
  GATEWAY_PORT=8082
  TZ=Europe/London
  ```

### 4. First deploy

Over SSH, from the folder holding `docker-compose.yml` + `.env`:
```sh
docker login                       # to pull, if the repos are private (skip if public)
docker compose --env-file .env up -d
```
(Or import the compose as a **Container Manager → Project**.) On first boot Postgres initialises the `cartrackerdb`
database and the WebApi applies all migrations (`ApplyMigrationsOnStartup=true`). Browse
**http://synologynas:8082**, sign in via Auth0, and you land on the (empty) garage — add a vehicle.

### 5. DVLA lookup (optional, and the deploy works without it)

The add-car registration lookup is dormant until credentials exist. Getting them is a registration task, not a
deploy step — see the **README Quickstart** for where each one comes from
(VES: <https://register-for-ves.driver-vehicle-licensing.api.gov.uk/>). Once you have them, they are per
deployment and never baked into an image: put them in the NAS `.env` and recreate.

```sh
VES_API_KEY=<from DVLA>          # alone, this is enough to switch the lookup on
MOT_API_KEY=<from DVSA>          # the four MOT values are all-or-nothing, and add only the MOT expiry seed
MOT_TOKEN_URL=<from DVSA>
MOT_CLIENT_ID=<from DVSA>
MOT_CLIENT_SECRET=<from DVSA>
```
```sh
docker compose --env-file .env up -d      # recreates webapi with the new environment
```

Left blank, the lookup answers `503 NotConfigured` and add-car stays manual — which is the normal state, not a
broken one.

---

## Releasing new versions

The root **`VERSION`** file (semver) is the single source of truth for image tags. Two ways to publish, both
producing `:latest` + `:<version>`:

- **Via CI (recommended):** bump locally without pushing images, commit, and let CI publish —
  ```sh
  ./scripts/release.ps1 -Minor -NoPush     # or: ./scripts/release.sh --minor --no-push
  git add VERSION && git commit -m "Bump VERSION to <new>" && git push
  ```
  CI builds + pushes `:latest`, `:<version>`, `:<sha>`. Watchtower updates the NAS within ~5 minutes.

- **Directly from your PC:** `docker login`, then `./scripts/release.ps1 -Minor` (or `.sh --minor`) — bumps,
  builds, and pushes straight to Docker Hub. Commit `VERSION` afterwards (the scripts don't).

`--dry-run`/`-DryRun` prints the bump and exits; `--patch`/`--major` for the other bumps.

A push to `main` that doesn't bump `VERSION` still ships `:latest` (so every merge auto-deploys) under the same
version number. To **pin** a NAS deploy and stop auto-updates, set `TAG=1.3.0` in the NAS `.env` and
`docker compose up -d`.

---

## Backups & restore

The `db-backup` sidecar runs `pg_dump` every 6 hours to `${DATA_ROOT}/backups/{daily,weekly,monthly}/`, rotated
7 daily / 4 weekly / 6 monthly. The 6-hour cadence means a fresh dump always predates an auto-update.

**Restore** a dump (dumps carry `--clean --if-exists`, so they overwrite cleanly):
```sh
# stop writers first
docker compose stop webapi
gunzip -c /volume1/docker/cartracker/backups/daily/cartrackerdb-<timestamp>.sql.gz \
  | docker exec -i $(docker compose ps -q postgres) psql -U postgres -d cartrackerdb
docker compose start webapi
```

To keep an off-NAS copy, point Synology **Hyper Backup** at `${DATA_ROOT}/backups`.

---

## Auto-updates (Watchtower)

`watchtower` polls Docker Hub every 5 minutes and recreates **only** the containers labelled
`com.centurylinklabs.watchtower.enable=true` — the `webapi` and `gateway`. Postgres and the backup tool are
never auto-updated (label absent), so a database upgrade is always a deliberate, manual step. When a new image
is pulled, the WebApi applies any new migrations on startup.

---

## Verifying persistence (the "survive destroys" requirement)

```sh
# add a vehicle in the UI first, then:
docker compose down -v && docker compose --env-file .env up -d
```
The vehicle is still there: the data is on a host **bind mount**, not a named volume, so `down -v` cannot remove
it. Named volumes would be wiped — that's why this deployment uses a bind mount.

---

## Troubleshooting

- **`/api/...` returns 401 after login:** the WebApi container can't reach Auth0's JWKS. Confirm outbound HTTPS
  to `usualexpat.uk.auth0.com:443` from the NAS (unlike the dev machine, there's no per-app firewall like
  Bitdefender). `docker compose logs webapi` shows the token-validation reason.
- **Login redirect mismatch:** the address-bar origin must be a registered Auth0 origin — use exactly
  `http://synologynas:8082`.
- **Add-car says the lookup isn't configured, but you set the keys:** check they reached the container —
  `docker compose exec webapi env | grep Lookup`. The separator is a **double** underscore
  (`Lookup__VesApiKey`); a single one binds nothing. Editing `.env` also needs a `docker compose up -d` to
  recreate the container — a restart alone keeps the old environment.
- **ARM NAS:** CI/scripts build `linux/amd64` by default. For an ARM Synology, add `linux/arm64` to the buildx
  `platforms` in `.github/workflows/ci.yml` (and build with `docker buildx --platform linux/arm64` locally).
- **HTTPS later:** put the gateway behind DSM's reverse proxy (Login Portal → Reverse Proxy) with a certificate,
  then register the new `https://…` origin in Auth0. No code or image change needed.
