# Spec Summary (Lite)

The app becomes **Cambelt** and becomes deployable as **one tenant of a shared host** that serves it over
HTTPS at **`cambelt.app`**.

**Half of this spec left on 2026-08-18** (DEC-020). It was written for an Azure VM whose only job was Cambelt;
the same box will now run several unrelated side projects, so the VM, its Bicep, the reverse proxy, the
PostgreSQL server and the off-site backup pull moved to a hosting repository. The folder keeps its Azure name
deliberately, because three other documents already reference the path.

**The rename shipped 2026-08-17, and it was six strings rather than the four counted here.** `TopNav.tsx`, the
landing hero, the garage hero (which also lost "· self-hosted"), and `index.html`, whose `<title>` had never
been changed from the Vite default `cartracker-webapp`. The guard written to keep the old name out then found
two more: the garage *footer* prose, and the chat system prompt introducing the assistant as "the assistant
inside Car Tracker". Namespaces, image names, `cartrackerdb`, the `cartracker.api` audience and the
`cartracker.settings` localStorage key **do not change** - invisible to users, and changing them costs
invalidated tokens, silently reset preferences, a database migration and a broken NAS deploy.

**What is left here is the tenant contract.** The app's three services join two **external** networks - `edge`
for the host's proxy, `data-cambelt` for its database - publish **no ports**, and expect a database that
already exists. `postgres`, `caddy`, `watchtower` and `db-backup` move behind a **`standalone` profile**, so
today's self-contained stack is one flag away and the Synology deployment and a fresh checkout keep working.

**Two things the host must get right that Cambelt cannot check for itself**, and therefore the two things this
repository must keep saying: `/mcp` is a long-lived streaming response that a proxy must not buffer, and a
dump restored without `${DATA_ROOT}/documents` gives `Document` rows pointing at nothing.

**The gateway needs no change for any of this.** `Kestrel__EndpointDefaults__Protocols: "Http1"` and YARP's
HTTP/1.1 pinning were added for exactly this shape, with a comment already naming "any reverse proxy", and
they are the first place to look at a 502 behind one. The SPA needs no rebuild either: `redirect_uri` is
computed from `window.location.origin`, which the gateway Dockerfile documents as deliberate. What *is* baked
at build time is the CSP's `connect-src`, which is the one piece a new origin could break.

**`.app` is HSTS-preloaded**, so plaintext is not something a browser will accept: the TLD enforces the gate
rather than merely satisfying it. The corollary is operational and travels with the host work - there is no
HTTP fallback, so a failed certificate means unreachable rather than degraded, and the DNS cutover must verify
the certificate *before* the record moves.

**HTTPS remains open as a gate**, and this repository can no longer close it or observe that it has been
closed. Gates one and three were closed by `2026-08-11-pre-public-release-gates`; the invitation allowlist is
what keeps sign-up shut in the meantime.

**The Azure research is kept, not deleted.** The priced rejection of Container Apps and App Service, the
`B2als_v2` sizing, the naming scheme, the NSG rules and the backup topology stay in `sub-specs/` as handover
material for the hosting repository, each file marked as such at the top.
