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

- [x] 3. The compose file becomes a tenant
      **Landed 2026-08-21 (`70e1c1a`, "Prepare for Arcane deployment"). This is what remains of the old task 3.** The `caddy` service, the data disk and the cloud-init that
      wrote `.env` from Key Vault all moved out; what stays is the shape of `deploy/docker-compose.yml` on a
      host it does not own.
  - [x] 3.1 Move `postgres`, `caddy`, `watchtower` and `db-backup` behind a **`standalone` compose profile**,
        so `docker compose --profile standalone up -d` is today's self-contained stack and the default is the
        tenant one. This is the change that keeps the move from being a one-way door: the NAS deployment, a
        laptop and a fresh checkout all keep working unchanged
  - [x] 3.2 Declare two **external** networks - `edge` (the host's proxy reaches the gateway) and
        `data-cambelt` (the app reaches the shared PostgreSQL). Per-project data networks are the point: an app
        on the same host cannot open a socket to a neighbour's database at all, which is isolation a single
        shared `data` network would have given away
  - [x] 3.3 **Publish no ports in the default profile.** `GATEWAY_PORT` is published only under `standalone`;
        on a shared host the proxy is the sole listener and there is no path to the app that bypasses TLS.
        This is the old 3.5, arrived at by a different route
  - [x] 3.4 Keep `DOTNET_gcServer=0` on both .NET containers - it was right for a small VM and is more right
        on a box with neighbours
  - [x] 3.5 `deploy/.env.example` documents the tenant values **alongside** the Synology ones. Both
        deployments are real, and the difference is now which profile you run rather than which file you read
  - [x] 3.6 Confirm the connection string is the only thing that changes when the database moves off-box:
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
  - [x] 4.2 **Stays here, and the premise changed under it.** The task assumed the CSP was baked at build
        time; since `0.21.0` (`6b6c5d3`) it is **served by the gateway at run time**, read from the same
        configuration section that produces `/config.js`, so the origin the policy permits and the origin the
        SPA calls come from one place and cannot drift. That is what made one published image deployable
        against any Auth0 tenant rather than needing a `-cambelt` build per release. What this repository still
        asserts is the half a build can get wrong: `theme-csp.test.ts` fails if `dist/index.html` ships a
        policy of its own, because policies **intersect** rather than override and a leftover meta tag naming
        the build's tenant would reduce the effective `connect-src` to `'self'` on exactly the deployments
        that had configured themselves correctly. The header's contents are asserted against a running
        container in CI, which is now the only place they exist
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

- [x] 6. Documentation and housekeeping
      **Closed 2026-08-23**, after the host it describes had been serving for two days.
  - [x] 6.1 Written, as `docs/deployment-shared-host.md` - not `deployment-azure.md`, because the tenant
        contract is the same on any host. It covers the two external networks and who creates them, the
        database and role, `DATA_ROOT` and `documents` beneath it, the configuration groups and their three
        different polarities, the two profiles, and the two things the host must get right that Cambelt cannot
        check: an unbuffered `/mcp` and documents travelling in the same snapshot as the dump.
        **It links to the normative contract rather than restating it.** `usualexpat-infra`'s
        `docs/tenant-contract.md` is the authority and says so; copying it here would recreate the
        three-copies-agreeing-in-none problem DEC-020 exists to prevent, which is the same failure as the NAS
        running a stale copy of `deploy/docker-compose.yml`.
        **This task was recorded as done before it was done.** DEC-020 and CLAUDE.md both referred to this
        file in the past tense from 2026-08-18, and it did not exist until now - the drift that a
        documentation task about drift is most likely to suffer.
  - [x] 6.2 Cross-linked both ways. `deployment-synology.md` **stays**: it documents a real, working install
        and is now the reference for the `standalone` profile, and its new banner says so rather than leaving
        a reader to guess which of two deployment documents applies to them
  - [x] 6.3 Both are in the new file - the login page's tenant name under "Things that will look like bugs
        and are not", and `TAG` under releases, with the trap attached: **Watchtower follows the tag a
        container was created from**, so editing `.env` and restarting leaves it on the old channel with
        nothing visible to say so
  - [x] 6.4 `docs/mcp-connect.md` opens with an address table now, and `https://cambelt.app/mcp` is the
        first endpoint it can offer from outside a LAN - most of the point of the exercise. The buffering
        symptom is written in beside it, because "a tool call hangs" is what an operator will see and
        the cause is two documents away
  - [x] 6.5 ~~DEC covering Azure + VM + Bicep + Caddy together~~ - **replaced by DEC-020**, which records the
        host leaving this repository instead. The priced rejection of Container Apps and App Service, and the
        plain statement that Azure is the most expensive mainstream option for this workload, are preserved in
        `sub-specs/infrastructure-spec.md` for whoever writes the hosting repository's own DEC
  - [x] 6.6 Updated - **and as met**, which reverses this task's own instruction. It was written on
        2026-08-18 when the host was a plan; the host shipped on 2026-08-21 and `cambelt.app` has served TLS
        since. The reasoning that the gate closes *somewhere else* still stands and is what the roadmap now
        says: the lines record that it is met, name where, and keep the point that no code change in this
        repository can confirm it. Sign-up had in any case already opened without it (DEC-022, 2026-08-22),
        so nothing here reads as permission for anything
  - [x] 6.7 Done as it went. The rename took `0.18.0` on its own, as this task required; the tenant compose
        and run-time configuration work took `0.21.0` through `0.23.0`. **This closing documentation pass takes
        no bump** - it changes `docs/` and root markdown only, which is exactly the set CI's publish job
        excludes, so there is no image for a version to name

- [x] 7. Verify
      **Split by who can verify it.** Everything below runs here; the host's checks - a valid certificate, an
      HTTP redirect, the DNS record - moved with the box, and are green there: `cambelt.app` has served TLS
      behind Caddy since 2026-08-21, and both tenants pass the host's restore drill against real snapshots.
  - [x] 7.1 `dotnet build`, `dotnet test`, `npm --prefix src/CarTracker.WebApp run test` - green on 2026-08-23
        at `0.27.1`: **390 Domain, 335 Data, 61 Chat, 653 front-end**, with one skip, which is 10.2's in the
        chat spec and is meant to be there
  - [x] 7.2 The standalone path still brings the whole stack up with no external network and no shared
        database. **The NAS is the standing proof**: it has run this profile continuously across the split and
        every release since, on `TAG=edge`, so the regression the profile split could have caused silently
        would have shown up within about five minutes of the commit that caused it. Both the override file and
        the `--profile` flag are needed and neither implies the other, which is the one thing about it worth
        writing down twice
  - [x] 7.3 Verified by the deployment itself: `cambelt.app` runs the default profile against the host's
        pre-created `edge` and `data-cambelt` networks and a database the host provisioned, publishes no
        ports, and answers only through Caddy. The host asserts the negative continuously rather than once -
        its drift check fails if anything but Caddy publishes to `0.0.0.0`, which is a stronger guarantee than
        reading `docker compose ps` on the day of the cutover
  - [x] 7.4 Sign-in works end to end at `https://cambelt.app` with no CSP violations. **The failure mode
        this task named no longer exists**: since `0.21.0` the policy is emitted by the gateway at run time
        from the same configuration section that produces `/config.js`, so a new origin cannot break it and
        the two lines that diagnose a login are `curl -s https://cambelt.app/config.js` and
        `curl -sI https://cambelt.app/ | grep -i content-security-policy`
  - [x] 7.5 **`/mcp` works through the host's proxy** with a token minted in Account: a read tool runs and
        the streaming response completes. This was the one behaviour local testing could not prove, and it is
        held up by `flush_interval -1` on the Caddy site block - a host guarantee, stated in the host's config
        with the reason on it, because a tenant cannot verify it from its own side. `docs/mcp-connect.md` now
        offers the public address and names buffering as the first thing to suspect if a call hangs rather
        than fails
  - [x] 7.6 Documents survive a `--force-recreate` of the webapi, which is what Watchtower does on every
        published image. The bind mount under `${DATA_ROOT}` is in effect, and the host's restore drill checks
        the stronger property continuously: **every `documents` row has its file and every file's bytes match
        the sha recorded at upload**, which validates the dump, the file copy, the encryption, the transfer
        and both disks in one assertion
  - [x] 7.7 `down` and `up -d` leaves documents intact, because they are a bind mount, and leaves the
        database untouched because it is not in this stack at all. **`down -v` stays out of the tenant
        profile's vocabulary** - the volumes it would remove belong to the host and to its neighbours

  > **The old 7.7 - destroy and redeploy the VM from Bicep with the data disk retained - moved out with the
  > box.** It was the claim that made VM-level backup unnecessary, and it is still the claim the hosting
  > repository has to test. Recorded here because it is the sort of check that survives a repository split
  > only if somebody writes down that it existed.
