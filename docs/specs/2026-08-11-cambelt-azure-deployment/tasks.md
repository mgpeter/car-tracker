# Spec Tasks

## Tasks

> **A ticked task with a struck-through title means it left, not that it was built.** On 2026-08-18 the VM,
> its Bicep, the shared proxy, the shared PostgreSQL server and the off-site backup pull moved to a separate
> hosting repository (DEC-020). Those tasks are kept, struck through, rather than deleted, so a reader can see
> what was planned here and where it went - and so the handful of sub-tasks that stayed behind are visible as
> exceptions rather than as omissions.

- [x] 1. The rename
      **Done 2026-08-17. It was six user-facing strings, not four** - see 1.1.
  - [x] 1.1 Update the four user-facing strings: `index.html:9` (`<title>`, currently the Vite default
        `cartracker-webapp`), `TopNav.tsx:52`, `LandingPage.tsx:35`, and `GaragePage.tsx:41` - the last also
        dropping **"· self-hosted"**, which becomes false on Azure
        > All four done, **and the guard written for 1.3 immediately found two more**, which is the argument for
        > writing it as a test rather than as a review note. (5) `GaragePage.tsx:31`, the footer prose, still
        > opened *"Self-hosted, and your garage is yours"* - so the footer line was **edited** during the
        > landing-page rewrite rather than removed, and both CLAUDE.md and this spec's own count were wrong
        > about it. Now *"Your garage is yours: each account sees only its own vehicles"*, which is the sentence
        > that was doing the work anyway. (6) `ChatSystemPrompt.cs:17` introduced the assistant as *"the
        > assistant inside Car Tracker"* - model-facing text that the model says back to the owner, so the
        > assistant would have named a product the UI no longer does. The prompt is frozen and cached, so the
        > cost is one cache rewrite (~10p on Opus 5, ~4p on Sonnet 5) and nothing structural; no test asserts
        > its text, only that it is a `const`.
  - [x] 1.2 Replace `public/favicon.svg` with a mark suiting the name
        > A toothed belt looped over two pulleys, keeping the plate mark's palette, its hardcoded-colour comment
        > and the single exemption `tokens.test.ts` grants that path. Drawn as **four strokes of one line**
        > (dark edge → yellow body → dark inner edge → background punched back through the middle) rather than
        > as an outlined path, so the belt keeps a real inner and outer edge at every size. Wordless and
        > **untoothed** for the same reason the plate carried no registration: at 16px teeth are mud. Checked by
        > rendering it at 16/24/32/48/96/200 in headless Chrome rather than by trusting the geometry - the two
        > pulleys fade into the green at 16px instead of smearing it, which is the degradation that was wanted.
  - [x] 1.3 Update the failing front-end tests - `LandingPage.test.tsx` and any snapshot asserting the old
        name. Consider extending that file's jargon guard to cover "self-hosted" so this class of stale claim
        cannot come back
        > **Nothing failed, and that was the finding.** No test anywhere asserted the product name: the landing
        > page's own `names the product and says what it does` asserted that an `h1` existed, so a rename could
        > not have broken it. It now names Cambelt, with a separate test that the page matches no `/car
        > tracker/i` - kept out of the jargon guard because this is not jargon, it is a wrong name, and it
        > would read as perfectly good copy to a reviewer who did not know the product had been renamed.
        > The guard already covered `self-hosted`, but only on the landing page, so `GaragePage.test.tsx` gained
        > the equivalent: the hero eyebrow names the product, and the page text matches neither `self-hosted`
        > nor `single-user`. That test went red on the footer prose in 1.1 the first time it ran.
  - [x] 1.4 `README.md:1` and prose references in `docs/**`, **only where the product is meant**
        > `README.md:1`, `docs/guide/USER-GUIDE.md` (title + first line), `docs/design-brief.md` (title + the
        > "What you are designing" line), `docs/product/mission.md` and `mission-lite.md`. README gained a note
        > naming the split - product Cambelt, code `CarTracker` - so the next reader meets it at the top rather
        > than deducing it. **Deliberately not renamed:** `docs/product/decisions.md:16` (DEC-001) and the
        > earlier specs' prose, which record what was decided on a date; rewriting them would falsify the
        > record, and the same reasoning the pre-public-release spec used to keep its problem statement in the
        > present tense. `archive/` untouched by definition.
  - [x] 1.5 Confirm by grep that no internal identifier moved: `CarTracker.*` namespaces, `cartracker-webapi`,
        `cartracker-gateway`, `cartrackerdb`, `cartracker.api`, `cartracker.settings`, `CARTRACKER_CONNECTION`
        > Confirmed, all seven unchanged: 3,371 `CarTracker.` references, 9 `cartracker-webapi`, 5
        > `cartracker-gateway`, 6 `cartrackerdb`, 5 `cartracker.api`, 1 `cartracker.settings`, 1
        > `CARTRACKER_CONNECTION`. `npm run build` and `dotnet build` both clean, **589 front-end tests** pass
        > (was 586; +2 written here, and the recorded figure was one light).

  > `cartracker.settings` is the one that punishes enthusiasm. Renaming it silently resets every user's theme
  > and MPG/L-100 km preference, with no error and no way for them to connect the change to a cause.

  > **Two adjacent staleness findings, flagged rather than fixed** - both are decisions rather than renames.
  > `lib/settings.ts`'s header comment justifies keeping the API key in localStorage because "the app is
  > single-user and self-hosted … Revisit if the app ever grows a second user or leaves the LAN" - **both
  > triggers have now fired**, the second one by this spec, and the file still carries an `apiKey` field whose
  > remaining purpose is worth establishing before the comment is rewritten around it. And
  > `docs/design-brief.md` still describes a single-user, self-hosted tool with "no marketing surface, no
  > onboarding", which the landing page already reversed at `:347`; only its product name was changed here.

  > **Shipped as `ecb0ed8`, `VERSION` 0.17.1 → 0.18.0**, bumped into the feature commit rather than after it.
  > Task 6.7's bump is therefore the *next* one, for task 3's compose change and the documentation.

- [x] 2. ~~Bicep~~ **- moved out 2026-08-18 (DEC-020)**
      The VM and everything describing it belong to the hosting repository. `main.bicep`, `main.bicepparam`,
      the NSG rules, the Key Vault secrets, `what-if` and the savings-plan question all go with it, along with
      the naming table and the priced comparison in `sub-specs/infrastructure-spec.md`, which is kept as
      handover material rather than deleted. **Nothing under `deploy/` in this repository describes a machine
      any more.**
  - [x] ~~2.1 `deploy/azure/main.bicep` + `main.bicepparam`~~ - hosting repository
  - [x] ~~2.2 NSG: 80 and 443 from the internet, 22 key-only~~ - hosting repository
  - [x] ~~2.3 Secrets into Key Vault by hand, never as Bicep parameters~~ - hosting repository
  - [x] ~~2.4 `az deployment group what-if` before the first `create`~~ - hosting repository
  - [x] ~~2.5 Re-check the cost figures against the pricing API~~ - hosting repository, and now a different
        question: the box is sized for several projects, so `B2as_v2` (same 2 vCPU, 8 GiB) is the likely step
        up from `B2als_v2`, and a resize is in-place with a reboot

- [ ] 3. The compose file becomes a tenant
      **This is what remains of the old task 3.** The `caddy` service, the data disk and the cloud-init that
      wrote `.env` from Key Vault all moved out; what stays is the shape of `deploy/docker-compose.yml` on a
      host it does not own.
  - [ ] 3.1 Move `postgres`, `caddy`, `watchtower` and `db-backup` behind a **`standalone` compose profile**,
        so `docker compose --profile standalone up -d` is today's self-contained stack and the default is the
        tenant one. This is the change that keeps the move from being a one-way door: the NAS deployment, a
        laptop and a fresh checkout all keep working unchanged
  - [ ] 3.2 Declare two **external** networks - `edge` (the host's proxy reaches the gateway) and
        `data-cambelt` (the app reaches the shared PostgreSQL). Per-project data networks are the point: an app
        on the same host cannot open a socket to a neighbour's database at all, which is isolation a single
        shared `data` network would have given away
  - [ ] 3.3 **Publish no ports in the default profile.** `GATEWAY_PORT` is published only under `standalone`;
        on a shared host the proxy is the sole listener and there is no path to the app that bypasses TLS.
        This is the old 3.5, arrived at by a different route
  - [ ] 3.4 Keep `DOTNET_gcServer=0` on both .NET containers - it was right for a small VM and is more right
        on a box with neighbours
  - [ ] 3.5 `deploy/.env.example` documents the tenant values **alongside** the Synology ones. Both
        deployments are real, and the difference is now which profile you run rather than which file you read
  - [ ] 3.6 Confirm the connection string is the only thing that changes when the database moves off-box:
        `CARTRACKER_CONNECTION` already carries host, database, user and password, and nothing in the app
        assumes it owns the server

  > **What must not be smuggled in with this.** Migrations still run on startup in Development only, and the
  > app still expects to be the only writer of its own database. A shared *server* is not a shared *database*:
  > one database and one role per project, per DEC-020, and `Maximum Pool Size` set explicitly in the
  > connection string, because Npgsql defaults to 100 per connection string and Postgres defaults to 100 for
  > the whole server.

- [x] 4. ~~Cutover~~ **- moved out 2026-08-18 (DEC-020)**, except the two halves that are Cambelt's
  - [x] ~~4.1 Register `https://cambelt.app` in Auth0~~ - hosting repository owns the timing; **this
        repository owns the fact that it is required**, and `docs/deployment-shared-host.md` says so, because
        an unregistered origin fails at the login redirect with a message about the *tenant* rather than about
        the deployment
  - [ ] 4.2 **Stays here.** Verify the built CSP's `connect-src` still names the Auth0 tenant - it is baked at
        build time, unlike `redirect_uri`, which is computed from `window.location.origin`. This is the one
        piece of the app a new origin can break, and the only one a test in this repository can cover
  - [x] ~~4.3 Point the Cloudflare DNS A record at the static IP, grey cloud~~ - hosting repository
  - [x] ~~4.4 Confirm Caddy has a valid certificate before announcing the address~~ - hosting repository. The
        reasoning travels with it and is written into the handover: `.app` is HSTS-preloaded, so a failed
        certificate is unreachable rather than degraded
  - [x] ~~4.5 Restore a NAS dump into the Azure database and copy `documents` across~~ - hosting repository,
        **with the pairing rule this repository has to keep asserting**: a dump without the documents
        directory restores `Document` rows pointing at nothing

- [x] 5. ~~Backups~~ **- moved out 2026-08-18 (DEC-020)**
      A backup schedule per project is how you get four jobs that each look fine alone and one that stopped
      six weeks ago. One host-level schedule covers every project's dumps and document volumes.
      `sub-specs/backup-and-restore.md` is kept as handover material: the forced-command SSH key, the 7/4/6
      rotation, the 90-day NAS retention and the "report failures somewhere actually read" requirement are all
      still the right answers, just not this repository's to implement.
  - [x] ~~5.1 db-backup sidecar on the new host, still not Watchtower-labelled~~ - hosting repository
  - [x] ~~5.2 NAS pull key with a forced command~~ - hosting repository
  - [x] ~~5.3 Pull covers both `backups/` and `documents/`~~ - hosting repository, and **the reason is an app
        fact**: `DocumentStore` writes content-addressed bytes whose only index is the `documents` table
  - [x] ~~5.4 NAS retention at least 90 days~~ - hosting repository
  - [x] ~~5.5 Rehearse a restore into a scratch database~~ - hosting repository
  - [x] ~~5.6 Make the pull job report failures somewhere actually read~~ - hosting repository

- [ ] 6. Documentation and housekeeping
  - [ ] 6.1 Write **`docs/deployment-shared-host.md`** - not `deployment-azure.md`, because the tenant
        contract is the same on any host. What it must state: the two external networks and who creates them,
        the database and role the app expects, `DATA_ROOT` and the `documents` directory beneath it, the
        environment keys, the two profiles and what each is for, and **the two things the host must get right
        that Cambelt cannot check**: an unbuffered `/mcp` and documents travelling with the dumps
  - [ ] 6.2 Cross-link it with `docs/deployment-synology.md`, which **stays** - it documents a real, working
        install, and it is now the reference for the `standalone` profile
  - [ ] 6.3 Note that the Auth0 login page shows `usualexpat.uk.auth0.com`, and that pinning `TAG` freezes a
        deploy
  - [ ] 6.4 Add `https://cambelt.app/mcp` to `docs/mcp-connect.md` once the address exists - the first
        endpoint the recipe can offer from outside the LAN, which is most of the point of the exercise
  - [x] 6.5 ~~DEC covering Azure + VM + Bicep + Caddy together~~ - **replaced by DEC-020**, which records the
        host leaving this repository instead. The priced rejection of Container Apps and App Service, and the
        plain statement that Azure is the most expensive mainstream option for this workload, are preserved in
        `sub-specs/infrastructure-spec.md` for whoever writes the hosting repository's own DEC
  - [ ] 6.6 Update `roadmap.md`'s HTTPS lines - **not as met**. What changed is *where* it is met: the gate
        stays open here and this repository can no longer observe it closing. Gates one and three are already
        closed, so nothing should read as permission to open sign-up
  - [ ] 6.7 Bump `VERSION` a **minor** for the compose and documentation change, `git add VERSION` **into the
        feature commit**. The rename in task 1 is a user-visible change and needs its own bump if it is
        committed separately

- [ ] 7. Verify
      **Split by who can verify it.** Everything below runs here; the host's checks - a valid certificate, an
      HTTP redirect, the DNS record - moved with the box.
  - [ ] 7.1 `dotnet build`, `dotnet test`, `npm --prefix src/CarTracker.WebApp run test`
  - [ ] 7.2 `docker compose --profile standalone up -d` from a fresh checkout brings the whole stack up with
        no external network and no shared database, exactly as today. **This is the regression the profile
        split can silently cause**, and the only way to see it is to run it
  - [ ] 7.3 `docker compose up -d` against pre-created `edge` and `data-cambelt` networks and an existing
        database: the app starts, publishes **no ports**, and answers on the `edge` network. `docker compose
        ps` showing a published port is a failure, not a detail
  - [ ] 7.4 Sign in end-to-end against whatever origin the host serves; confirm no CSP violations in the
        console. The CSP's `connect-src` is baked at build time and is the one thing a new origin breaks
  - [ ] 7.5 **`/mcp` through the host's proxy, with a token minted in Account.** Run a read tool and confirm
        the streaming response completes. This is the one behaviour local testing cannot prove, and the
        specific reason the Cloudflare proxy is left off until it is known-good
  - [ ] 7.6 Upload a document, then `docker compose up -d --force-recreate webapi` - what Watchtower does -
        and confirm it still downloads. If it 404s, the documents bind mount is not in effect and every upload
        since is already gone
  - [ ] 7.7 `docker compose down && docker compose up -d`: documents survive, because they are a bind mount,
        and the database survives because it is no longer in this stack at all. **Not `down -v`** under the
        tenant profile - the volumes that would remove are the host's

  > **The old 7.7 - destroy and redeploy the VM from Bicep with the data disk retained - moved out with the
  > box.** It was the claim that made VM-level backup unnecessary, and it is still the claim the hosting
  > repository has to test. Recorded here because it is the sort of check that survives a repository split
  > only if somebody writes down that it existed.
