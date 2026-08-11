# Spec Summary (Lite)

The app becomes **Cambelt** at **`cambelt.app`**, served over HTTPS from a small Azure VM described in Bicep,
with dumps and documents pulled off-site to the NAS. This closes the **HTTPS** gate on `roadmap.md:208-210`;
gates one and three belong to `2026-08-11-pre-public-release-gates`, and deploying without that spec does not
make sign-up safe to open.

**`.app` is HSTS-preloaded**, so plaintext is not something a browser will accept — the TLD enforces the gate
rather than merely satisfying it. The corollary is operational: there is no HTTP fallback, so a failed
certificate means unreachable rather than degraded, and the DNS cutover must verify the cert *before* moving
the record.

**The rename is four strings.** `TopNav.tsx:52`, `LandingPage.tsx:35`, `GaragePage.tsx:41` (which also loses
"· self-hosted" — false on Azure, and a survivor of the cleanup CLAUDE.md records as done), and
`index.html:9`, whose `<title>` is still the Vite default `cartracker-webapp`. Namespaces, image names,
`cartrackerdb`, the `cartracker.api` audience and the `cartracker.settings` localStorage key **do not
change**: invisible to users, and changing them costs invalidated tokens, silently reset preferences, a
database migration and a broken NAS deploy.

**Bicep over Terraform**, and the reason is state. Terraform for a solo project either keeps state locally
(lost with the laptop) or in a storage account that must exist before anything can be provisioned — a
bootstrap problem Bicep does not have, because ARM's deployment history in the resource group *is* the record
and `what-if` reads live resources. The cost is honest: Bicep is Azure-only, so the three Cloudflare DNS
records are set by hand.

**Caddy fronts the gateway**, and the gateway needs no change — its `Kestrel__EndpointDefaults__Protocols:
"Http1"` and YARP HTTP/1.1 pinning were added for exactly this shape, with a comment already naming "any
reverse proxy". `GATEWAY_PORT` stops being published; Caddy is the only listener, and `https://cambelt.app`
becomes the registered Auth0 origin. The SPA needs no rebuild: `redirect_uri` is computed from
`window.location.origin`, which the gateway Dockerfile documents as deliberate.

**Costs** (UK South, GBP, Linux, list, pulled from the Azure retail API 2026-08-11): `B2als_v2` 2 vCPU/4 GiB
at **£23.50/mo**, plus £6.70 of disk and £2.77 for a static IP — **≈£33/mo**, or ≈£27 on a one-year savings
plan. The non-obvious result: `B2als_v2` on a three-year plan (£12.46) undercuts `B1ms` at list (£13.07) with
twice the RAM and twice the vCPU. 4 GiB is the recommendation because Postgres plus two .NET runtimes plus the
backup sidecar leaves nothing for page cache on 2 GiB.

**Watchtower stays**, and the reasoning is worth recording: auto-updating a public app from `:latest` sounds
reckless, but CI has published nothing without a `VERSION` bump since 2026-08-09, so `:latest` moves only on a
deliberate release. Postgres and the backup sidecar stay unlabelled and are never auto-updated.

**Backups** keep the `db-backup` sidecar exactly as it is (6-hourly, 7/4/6 rotation) and add a scheduled NAS
pull covering **both** `${DATA_ROOT}/backups` and `${DATA_ROOT}/documents` — the sidecar dumps Postgres only,
and a restored dump without the bytes gives `Document` rows pointing at nothing, the warning
`docs/deployment-synology.md:128-130` already makes. The pull uses a **forced-command SSH key**, so a leaked
NAS key cannot become a shell on a public VM. The residual risk, stated and accepted: the only off-site copy
is at home, and there is no immutable intermediate if the VM is compromised.
