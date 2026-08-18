# Spec Tasks

## Tasks

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

  > **`VERSION` is not bumped** - task 6.7 puts the bump in the feature commit for the whole spec. If the
  > rename is committed on its own ahead of the Azure work, it is a user-visible change and needs its own
  > minor, per CLAUDE.md's rule that a feature shipping without a bump ships under the previous number.

- [ ] 2. Bicep
  - [ ] 2.1 `deploy/azure/main.bicep` + `main.bicepparam`: VNet, subnet, NSG, static Standard public IP, NIC,
        Ubuntu 24.04 VM (`B2als_v2`), 32 GiB OS disk, 64 GiB data disk, Key Vault, user-assigned managed
        identity with **Key Vault Secrets User**. **Take every name from the infrastructure sub-spec's
        *Naming* table** rather than choosing one while writing the template - almost nothing in Azure renames
        in place, so a name invented here is a redeploy to correct. Two of them are **globally unique** and can
        therefore fail at deploy time on someone else's tenant: the Key Vault and the public IP's DNS label.
        `az keyvault check-name-availability` before the first `create`
  - [ ] 2.2 NSG: 80 and 443 from the internet, 22 key-only. Keep 80 open - Caddy's ACME HTTP-01 challenge
        needs it, and closing it breaks renewal silently sixty days later
  - [ ] 2.3 Secrets into Key Vault by hand (`az keyvault secret set`), **never as Bicep parameters** - a
        parameter value is stored in the deployment history in plain text
  - [ ] 2.4 `az deployment group what-if` and read the output before the first `create`
  - [ ] 2.5 Re-check the cost figures in the infrastructure sub-spec against the pricing API before committing
        to a savings plan - the rates quoted were pulled 2026-08-11, and a savings plan is a commitment

- [ ] 3. cloud-init and the stack
  - [ ] 3.1 `deploy/azure/cloud-init.yaml`: Docker + Compose plugin + Azure CLI; partition, format and mount
        the data disk at `/srv/cambelt` **by UUID in `/etc/fstab`**, not by device name
  - [ ] 3.2 Fetch secrets from Key Vault with the managed identity; write `/srv/cambelt/.env` mode `0600`
  - [ ] 3.3 Harden `sshd` (`PasswordAuthentication no`, `PermitRootLogin no`) and enable
        `unattended-upgrades`
  - [ ] 3.4 Add the `caddy` service to `deploy/docker-compose.yml` on 80/443 proxying `gateway:8080`, with its
        certificate store on a **bind mount** under `${DATA_ROOT}` - losing it means re-issuing on every
        restart and hitting Let's Encrypt rate limits at the worst time
  - [ ] 3.5 Bind the gateway to `127.0.0.1` instead of publishing `${GATEWAY_PORT}`, so no path to the app
        bypasses TLS
  - [ ] 3.6 Set `DOTNET_gcServer=0` on both .NET containers
  - [ ] 3.7 Add the Azure values to `deploy/.env.example` **alongside** the Synology ones - both deployments
        are real

- [ ] 4. Cutover
  - [ ] 4.1 Register `https://cambelt.app` in Auth0 Allowed Callback URLs, Logout URLs and Web Origins,
        keeping the NAS origin while both run
  - [ ] 4.2 Verify the built CSP's `connect-src` still names the Auth0 tenant - that value **is** baked at
        build time, unlike `redirect_uri`, which is computed from `window.location.origin`
  - [ ] 4.3 Point the Cloudflare DNS A record at the static IP, **grey cloud (DNS-only)**
  - [ ] 4.4 **Confirm Caddy has a valid certificate before announcing the address.** `.app` is HSTS-preloaded:
        there is no HTTP fallback, so a failed certificate is unreachable, not degraded
  - [ ] 4.5 Restore a NAS dump into the Azure database and copy `documents` across, so the deployment starts
        with the real history rather than an empty garage

- [ ] 5. Backups
  - [ ] 5.1 Confirm the `db-backup` sidecar is writing to `/srv/cambelt/backups` on the new host and is still
        **not** Watchtower-labelled
  - [ ] 5.2 Create the NAS pull key with a **forced command**:
        `command="/usr/local/bin/cambelt-backup-export",restrict ssh-ed25519 ...` - so a leaked backup key
        cannot become a shell on a public VM. Separate from any administrative key, and only on the NAS
  - [ ] 5.3 Schedule the NAS pull to cover **both** `backups/` and `documents/` - a dump without the bytes
        restores `Document` rows pointing at nothing
  - [ ] 5.4 Set NAS retention to at least 90 days, longer than the VM's 7/4/6, so a corruption replicated by
        the pull is still recoverable from older history
  - [ ] 5.5 **Rehearse a restore** into a scratch database: check row counts, then download a document.
        Record the date in `docs/deployment-azure.md`
  - [ ] 5.6 Make the pull job report failures somewhere actually read - a silent backup that stopped six weeks
        ago is the failure mode this topology has

- [ ] 6. Documentation and housekeeping
  - [ ] 6.1 Write `docs/deployment-azure.md` on `docs/deployment-synology.md`'s structure; cross-link the two;
        keep the Synology doc
  - [ ] 6.2 State explicitly in it that there is **no VM-level backup by design** - the VM is reproducible
        from Bicep and cloud-init, and only `/srv/cambelt` is irreplaceable. Otherwise it reads as an oversight
  - [ ] 6.3 Note that the Auth0 login page shows `usualexpat.uk.auth0.com`, and that pinning `TAG` freezes a
        deploy
  - [ ] 6.4 Add `https://cambelt.app/mcp` to `docs/mcp-connect.md` - the first address reachable from outside
        the LAN, which is most of the point
  - [ ] 6.5 DEC covering Azure + VM + Bicep + Caddy together, with the priced rejection of Container Apps and
        App Service, and the plain statement that Azure is the most expensive mainstream option for this
        workload and was chosen anyway
  - [ ] 6.6 Update `roadmap.md:208-210` - **HTTPS met** - and README §6 and CLAUDE.md's state of play. Note in
        the roadmap that gates one and three remain, so nobody reads a green HTTPS line as permission to open
        sign-up
  - [ ] 6.7 Bump `VERSION` a **minor**, `git add VERSION` **into the feature commit**

- [ ] 7. Verify
  - [ ] 7.1 `dotnet build`, `dotnet test`, `npm --prefix src/CarTracker.WebApp run test`
  - [ ] 7.2 `https://cambelt.app` serves the app with a valid certificate; `http://` redirects
  - [ ] 7.3 Sign in end-to-end against the new origin; confirm no CSP violations in the console
  - [ ] 7.4 **`/mcp` over Caddy with a token minted in Settings.** Run a read tool and confirm the streaming
        response completes - this is the one behaviour local testing cannot prove, and the specific reason the
        Cloudflare proxy is left off for now
  - [ ] 7.5 Upload a document, then `docker compose up -d --force-recreate webapi` - what Watchtower does - and
        confirm it still downloads. If it 404s, the `/srv/cambelt/documents` mount is not in effect and every
        upload since is already gone
  - [ ] 7.6 `docker compose down -v && docker compose up -d`: the database and documents survive, because they
        are bind mounts
  - [ ] 7.7 Destroy and redeploy the VM from Bicep with the data disk retained, and confirm the stack returns
        - the claim that makes VM backup unnecessary, and the one nobody tests until they need it
