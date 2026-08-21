# Deploying CarTracker on a Synology NAS

CarTracker deploys as three containers behind a single origin - **gateway** (serves the built SPA and proxies
the API), **webapi**, and **postgres** - plus a **db-backup** sidecar and **watchtower** for auto-updates.
Images are published to Docker Hub by CI, and only by CI; the NAS pulls them.

```
git push main ─► GitHub Actions (test + contract gate, then publish) ─► Docker Hub :edge
git push tag ─► GitHub Actions (retag, no rebuild) ────────────────► Docker Hub :<version> :latest :stable
NAS: watchtower ─polls Docker Hub─► recreates webapi + gateway on a new image for the tag it runs
     browser ─http://synologynas:8082─► gateway ─► SPA (static) + /api,/mcp ─► webapi ─► postgres (bind mount)
```

The database lives on a **host bind mount** (`${DATA_ROOT}/pgdata18`), so it survives `docker compose down`,
`down -v`, image rebuilds and container recreation. Only deleting the host folder removes it. **Uploaded
documents sit on a second bind mount** (`${DATA_ROOT}/documents`) for the same reason - the bytes are evidence
and the container they are served from is auto-updated, so they cannot live inside it.

---

## One-time setup

### 1. GitHub (for CI publishing)

- Repository **Settings → Secrets and variables → Actions**, add:
  - `DOCKERHUB_USERNAME` - your Docker Hub username (e.g. `mgpeter`).
  - `DOCKERHUB_TOKEN` - a Docker Hub **access token** (Docker Hub → Account Settings → Security).
- Create two **public** Docker Hub repositories: `cartracker-webapi` and `cartracker-gateway`. (They contain no
  secrets - all config is injected as environment variables - so public keeps the NAS pull credential-free.)

The next push to `main` runs CI; if it's green, the `publish` job builds and pushes both images.

### 2. Auth0

Confirm the application (`AYVXSt9aa5rz4kHFYs3KZ5HqYfBNkPKp`, tenant `usualexpat.uk.auth0.com`) has
`http://synologynas:8082` in **Allowed Callback URLs**, **Allowed Logout URLs** and **Allowed Web Origins**, and
that **Refresh Token** rotation is enabled (Application grant types + API → Allow Offline Access). The browser
origin is whatever you type in the address bar, so it must match exactly.

### 3. Invitations and the Management API (nobody can sign up without this)

Signing in is Auth0's; **having an account here is not**. The first time a valid token arrives for a subject
the app has never seen, the address behind it is checked against `SIGNUP_ALLOWED_EMAILS` (exact addresses) and
`SIGNUP_ALLOWED_DOMAINS` (everyone at a domain), both comma-separated, and the tenant must have marked it
verified. Not admitted means no account row is written and the app shows a "not yet invited" panel. The
reasoning is in the [README](../README.md#who-may-sign-up--an-empty-allowlist-means-closed); the dashboard
steps are here. It comes before the NAS section deliberately: skip it and the deploy will look perfect right
up to the moment you try to sign in.

**The access token carries no email address** - only `auth0|68a…` - so the server asks the tenant. That needs a
machine-to-machine application:

1. Auth0 dashboard → **Applications → Create Application → Machine to Machine**, authorised for the
   **Auth0 Management API**.
2. On its **API Access** tab → Auth0 Management API → **Edit**, grant **both**:
   - **`read:users`** - resolves the address and its `email_verified` flag. Without it *nobody* can be
     admitted, whatever the allowlist says.
   - **`delete:users`** - erases the login when someone deletes their account. Without it `DELETE /api/account`
     answers `503` and deletes nothing, rather than destroying the data and leaving a working sign-in behind.

   The permission counter on that tab should read **2** granted, not 1 - either one missing silently disables
   half of this.
3. Put its client id and secret in the NAS `.env` as `AUTH0_MANAGEMENT_CLIENT_ID` / `_CLIENT_SECRET`,
   alongside the allowlist - both in §4 - and **Build** the project rather than restarting it.

No tenant is configured for the credential separately: it reuses `Auth0:Authority`, so one tenant is one
setting. Two would be how a deployment ends up validating tokens against one tenant and reading users from
another.

**Check it took**, from any browser once deployed - `/api/meta` is anonymous and reports the credential state
directly:

```
http://synologynas:8082/api/meta      →  "identityDeletionConfigured": true
```

`false` means the container never received the pair, and no sign-in will be admitted. The WebApi also states
the whole posture once per boot; `docker compose logs webapi | grep "Sign-up posture"` gives the allowlist
counts it actually parsed and whether the credential is present, which distinguishes "the key never arrived"
from "the address is not on the list".

**Adoption (`OWNERSHIP_CLAIM_UNOWNED_FOR`) is a one-off retrofit and normally stays blank.** It names the
single Auth0 subject permitted to inherit vehicles that have no owner - only relevant on a database that
predates multi-user. Blank means no adoption, ever, which is what a fresh deployment wants: the retired rule it
replaces ("whoever signs in first claims everything") is a trap the moment a stranger can sign in first.

### 4. NAS

- Enable **Container Manager** and **SSH** (Control Panel → Terminal & SNMP).
- Create the data folders on a volume:
  ```sh
  mkdir -p /volume1/docker/cartracker/pgdata18 /volume1/docker/cartracker/documents \
           /volume1/docker/cartracker/backups
  ```
- Copy `deploy/docker-compose.yml` to the NAS (e.g. `/volume1/docker/cartracker/`), and create a `.env` beside
  it from `deploy/.env.example`:
  ```sh
  DOCKERHUB_USER=mgpeter
  TAG=edge          # this is the box the dogfooding happens on; `stable` for anywhere else
  POSTGRES_PASSWORD=<a strong password>
  DATA_ROOT=/volume1/docker/cartracker
  GATEWAY_PORT=8082
  TZ=Europe/London

  # Who may create an account here, and how the server learns their address (§3). BLANK MEANS CLOSED.
  SIGNUP_ALLOWED_EMAILS=you@example.com
  SIGNUP_ALLOWED_DOMAINS=
  AUTH0_MANAGEMENT_CLIENT_ID=<M2M client id>
  AUTH0_MANAGEMENT_CLIENT_SECRET=<M2M client secret>

  # Only for a pre-multi-user database being retrofitted (§3). Blank means no adoption, ever.
  OWNERSHIP_CLAIM_UNOWNED_FOR=
  ```
  **Note the polarity between the two halves of that file.** A blank `VES_API_KEY` (§6) means one *feature* is
  off and everything else carries on; a blank `SIGNUP_*` or `AUTH0_MANAGEMENT_*` means the *door is shut* and
  nobody new gets an account. Same blank, opposite consequence - `deploy/.env.example` says so at the keys
  themselves, which is the copy to work from.

> **Container Manager Projects keep their own copy of the compose file, and this is the trap.** Importing
> `docker-compose.yml` as a **Project** snapshots the YAML into DSM. Editing the file on disk afterwards does
> not change what runs, and neither does adding a key to `.env` - the `${…}` interpolation site lives in the
> YAML, so a project imported before a key existed has nowhere to put it and the container comes up with the
> value silently absent. Whenever you pull a newer `deploy/docker-compose.yml`, update the project's copy too.
>
> **And an environment change needs Build, not Restart.** *Action → Build* re-reads the YAML and the `.env`;
> Restart reuses the existing container spec, and so does a Watchtower update - which is how a container can
> take a brand-new image while still carrying an environment assembled months earlier.

### 5. First deploy

Over SSH, from the folder holding `docker-compose.yml` + `.env`:
```sh
docker login                       # to pull, if the repos are private (skip if public)
docker compose --env-file .env up -d
```
(Or import the compose as a **Container Manager → Project**.) On first boot Postgres initialises the `cartrackerdb`
database and the WebApi applies all migrations (`ApplyMigrationsOnStartup=true`). Browse
**http://synologynas:8082**, sign in via Auth0, and you land on the (empty) garage - add a vehicle.

### 6. DVLA lookup (optional, and the deploy works without it)

The add-car registration lookup is dormant until credentials exist. Getting them is a registration task, not a
deploy step - see the **README Quickstart** for where each one comes from
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

Left blank, the lookup answers `503 NotConfigured` and add-car stays manual - which is the normal state, not a
broken one.

---

## Releasing new versions

**A release is a git tag** (changed 2026-08-21, DEC-021). The root **`VERSION`** file still holds the number -
it is what MSBuild stamps into the assemblies, because `.dockerignore` excludes `.git` and nothing inside an
image build can read a tag - but bumping it no longer *causes* anything. Three channels:

| tag | moves when | who runs it |
|---|---|---|
| `:edge`, `:<sha>` | a push to `main` touches anything that can affect an image | the NAS |
| `:<version>`, `:latest`, `:stable` | you push a `v<version>` git tag | the shared host, and anyone else |

**Step 1 - ship it to edge.** Bump, stage `VERSION` into the feature commit, push:
```sh
./scripts/release.ps1 -Minor          # or: ./scripts/release.sh --minor
git add VERSION && git commit -m "<subject>" && git push
```
CI runs the build, tests and contract gate, then pushes `:edge` and `:<sha>`. A commit confined to `docs/`,
`archive/` or root markdown skips the Docker steps, because it cannot change either image. The run summary
prints the tag command for step 2.

**Step 2 - bless it.** Once that version has actually run for a while:
```sh
git tag -a v0.22.0 -m "0.22.0" && git push origin v0.22.0
```
The **Release** workflow retags the digest CI already built into `:0.22.0`, `:latest` and `:stable`. It does
not rebuild: a rebuild would put a different digest under the same version number, and the two "prove the
image" steps in CI would have validated an artifact the deployment does not run. It refuses if the tag does
not name the `VERSION` at its own commit, if that commit never published, if the version tag already exists
at another digest, or if the commit is not on `main`.

`-DryRun`/`--dry-run` prints the bump and exits; `-Patch`/`-Major` for the other bumps. `-Build`/`--build`
builds both images locally as `:dev` for a smoke test. **The scripts cannot publish** - `docker push` was
removed from them, because `--all-tags` from a dev machine would have moved `:latest` and `:stable` to an
unreviewed working-tree build.

**Actions → CI → Run workflow** rebuilds and republishes `:edge` from the current `main` without any source
change, for a base-image patch or a publish that failed after a green build. **Actions → Release → Run
workflow** re-runs a promotion that failed after the tag was already pushed, which beats deleting and
re-pushing a tag.

To **pin** a deploy and stop auto-updates entirely, set `TAG=0.22.0` in the `.env` and `docker compose up -d`.

---

## Backups & restore

The `db-backup` sidecar runs `pg_dump` every 6 hours to `${DATA_ROOT}/backups/{daily,weekly,monthly}/`, rotated
7 daily / 4 weekly / 6 monthly. The 6-hour cadence means a fresh dump always predates an auto-update.

**The sidecar dumps Postgres only.** Uploaded documents are files on `${DATA_ROOT}/documents`, not rows, so
they need a folder copy - a restored dump gives you `Document` rows whose bytes are missing otherwise. Point
whatever off-NAS copy you run at both paths.

**Restore** a dump (dumps carry `--clean --if-exists`, so they overwrite cleanly):
```sh
# stop writers first
docker compose stop webapi
gunzip -c /volume1/docker/cartracker/backups/daily/cartrackerdb-<timestamp>.sql.gz \
  | docker exec -i $(docker compose ps -q postgres) psql -U postgres -d cartrackerdb
docker compose start webapi
```

To keep an off-NAS copy, point Synology **Hyper Backup** at `${DATA_ROOT}/backups` **and**
`${DATA_ROOT}/documents`.

**The sidecar's `pg_dump` must be at least the server's major version.** It refuses a newer server outright,
so a 17 client left pointing at an 18 cluster stops dumping and nothing here says so - which is why its image
carries an explicit `:18-alpine` tag rather than the `:latest` it used to resolve to. Move the two together.

---

## Major version upgrade (17 to 18)

Postgres is not Watchtower-labelled, so this only ever happens because you decided to do it. Read the whole
section before starting.

**The trap.** Postgres 18's official image moved `PGDATA` to `/var/lib/postgresql/<major>/docker` and its
`VOLUME` to the parent `/var/lib/postgresql`. Pointing an 18 image at the old `.../data` mount does **not**
fail: it initialises a fresh cluster on the container's own layer, passes `pg_isready`, and the WebApi
migrates an empty database. A green stack, an empty garage, and every real row still on disk with nothing
reading it. That is why the mount moved to a **new** folder, `${DATA_ROOT}/pgdata18`, and why the PG17
`${DATA_ROOT}/pgdata` beside it is left untouched: it is the rollback.

There is no schema rollback. `AddPerOwnerReferenceLists`'s `Down()` throws by design, so recovery here is
backup-and-restore shaped, never migrate-down shaped.

1. **See what is actually running.** The tag in the compose file is not the answer.
   ```sh
   docker inspect --format '{{.Config.Image}}' $(docker compose ps -q postgres)
   ```
2. **Take a fresh dump with the old client against the old server.** Do not rely on the scheduled one.
   ```sh
   docker compose stop webapi
   docker compose exec -T postgres pg_dump -U postgres -d cartrackerdb --clean --if-exists \
     | gzip > /volume1/docker/cartracker/backups/pre-pg18-$(date +%F).sql.gz
   ```
3. **Copy `${DATA_ROOT}/documents` off the NAS in the same pass.** A dump restored without it gives
   `Document` rows pointing at nothing: the bytes are content-addressed on disk and the table is their only
   index, so the two travel together.
4. **Stop the stack and make the new folder.**
   ```sh
   docker compose --profile standalone down
   mkdir -p /volume1/docker/cartracker/pgdata18
   ```
5. **Update the compose file on the NAS, pull, and *rebuild*.** Two traps in one step. The NAS runs a *copy*
   of `deploy/docker-compose.yml` and Container Manager snapshots a third inside DSM, and `${...}`
   interpolation lives in the YAML - so a copy predating this change has nowhere to put the new mount, and
   Container Manager's *Restart* silently keeps the old spec where *Build* does not. Separately, `18-alpine`
   floats at the major: Docker never re-checks a floating tag it has already cached, so without an explicit
   pull the box can come up on a months-old 18.x.
   ```sh
   docker compose --profile standalone pull postgres db-backup
   ```
6. **Start the database alone, and record what actually landed.**
   ```sh
   docker compose --profile standalone up -d postgres
   docker compose exec -T postgres psql -U postgres -c 'select version()'
   docker inspect --format '{{.Image}}' $(docker compose ps -q postgres)
   ```
   A fresh `initdb` under 18 turns **data checksums** on by default. This route gets that for free; an
   in-place `pg_upgrade` would have had to match the old cluster's setting instead.
7. **Restore.**
   ```sh
   gunzip -c /volume1/docker/cartracker/backups/pre-pg18-<date>.sql.gz \
     | docker compose exec -T postgres psql -U postgres -d cartrackerdb
   ```
8. **Verify before starting any writer.** Row counts against what you had:
   ```sh
   docker compose exec -T postgres psql -U postgres -d cartrackerdb -c \
     "select 'vehicles' t, count(*) from vehicles
      union all select 'fuel_entries', count(*) from fuel_entries
      union all select 'expense_entries', count(*) from expense_entries
      union all select 'mileage_readings', count(*) from mileage_readings
      union all select 'documents', count(*) from documents"
   ```
   Then in the app: BT53's fuel spend reads **£888.87** over 13 fills, and the odometer shows the latest
   reading by date rather than the highest number.
9. **Start the rest, and check the backups resumed.** A container being up is not a dump landing.
   ```sh
   docker compose --profile standalone up -d
   ls -l /volume1/docker/cartracker/backups/daily/
   ```
10. **Rollback**, if any of the above disagrees: `down`, put the mount back to
    `${DATA_ROOT}/pgdata:/var/lib/postgresql/data`, put the image back to `postgres:17-alpine`, rebuild. The
    17 cluster was never opened by an 18 binary, so it is exactly as you left it. Delete
    `${DATA_ROOT}/pgdata` only after a settling period you choose deliberately.

---

## Auto-updates (Watchtower)

`watchtower` polls Docker Hub every 5 minutes and recreates **only** the containers labelled
`com.centurylinklabs.watchtower.enable=true` - the `webapi` and `gateway`. Postgres and the backup tool are
never auto-updated (label absent), so a database upgrade is always a deliberate, manual step. When a new image
is pulled, the WebApi applies any new migrations on startup.

**It follows the tag each container was created from, not the `TAG` in your `.env`.** A container created on
`:latest` keeps watching `:latest` however that file now reads, so switching channel needs
`docker compose up -d` - Container Manager's *Build* - and a *Restart* silently leaves you where you were.
`docker inspect --format '{{.Config.Image}}' <container>` says which one is actually in force.

---

## Verifying persistence (the "survive destroys" requirement)

```sh
# add a vehicle and upload a document in the UI first, then:
docker compose down -v && docker compose --env-file .env up -d
```
Both are still there: the data is on host **bind mounts**, not named volumes, so `down -v` cannot remove them.
Named volumes would be wiped - that's why this deployment uses bind mounts.

Worth doing the recreate case too, because it is the one that happens by itself:
```sh
docker compose up -d --force-recreate webapi     # what Watchtower does on a new image
```
The uploaded document must still download afterwards. If it 404s with "the file is missing from the volume",
the `${DATA_ROOT}/documents` mount is not in effect and every upload since is already gone.

---

## Troubleshooting

- **`/api/...` returns 401 after login:** the WebApi container can't reach Auth0's JWKS. Confirm outbound HTTPS
  to `usualexpat.uk.auth0.com:443` from the NAS (unlike the dev machine, there's no per-app firewall like
  Bitdefender). `docker compose logs webapi` shows the token-validation reason.
- **Login redirect mismatch:** the address-bar origin must be a registered Auth0 origin - use exactly
  `http://synologynas:8082`.
- **A setting you put in `.env` behaves as if it were unset** - the lookup says it isn't configured, or an
  invited person is told they are not invited. Check the value actually reached the container:
  ```sh
  docker compose exec webapi env | grep -E 'Lookup|Auth0|Signup'
  ```
  Read the result, because the two failures need different fixes:
  - **The key is absent entirely** → the compose file that is running has no `${…}` interpolation site for it.
    Its copy is older than the key. Update it (and, under Container Manager, the *project's* copy - see §4).
  - **The key is present but empty** (`Auth0__Management__ClientId=`) → the compose file is current but the
    `.env` is not being read. It must sit beside the YAML, and the CLI form is
    `docker compose --env-file .env up -d`.

  Two things that look like causes and are not: the separator is a **double** underscore
  (`Lookup__VesApiKey`, `Auth0__Management__ClientId`) and a single one binds nothing; and editing `.env` needs
  a **recreate** (`docker compose up -d`, or Container Manager's *Build*) - a restart, and a Watchtower image
  update, both keep the old environment.
- **"Not yet invited", but the address is on the allowlist:** almost always the above - `AUTH0_MANAGEMENT_*`
  never reached the container, so no address could be read, and an address that cannot be read is on no list.
  `http://synologynas:8082/api/meta` answering `"identityDeletionConfigured": false` confirms it in one request;
  `docker compose logs webapi | grep "Sign-up posture"` gives the boot-time summary. If the credential *is*
  configured, the next suspects are the M2M application missing the **`read:users`** grant (the log then reads
  `Auth0 Management returned 403`) and the tenant not having verified the address - an unverified address is
  refused by design, since on a database connection anyone can type any address. See §3.
- **ARM NAS:** CI/scripts build `linux/amd64` by default. For an ARM Synology, add `linux/arm64` to the buildx
  `platforms` in `.github/workflows/ci.yml` (and build with `docker buildx --platform linux/arm64` locally).
- **HTTPS later:** put the gateway behind DSM's reverse proxy (Login Portal → Reverse Proxy) with a certificate,
  then register the new `https://…` origin in Auth0. No code or image change needed.
