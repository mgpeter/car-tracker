# Spec Requirements Document

> Spec: Cambelt on Azure - a name, an address, and a box that can be rebuilt
> Created: 2026-08-11
> Status: **In progress, and half of it has left.** Task 1, the rename, landed 2026-08-17. On 2026-08-18 the
> **VM, the Bicep, the reverse proxy, the PostgreSQL server and the off-site backup pull moved to a separate
> hosting repository** (DEC-020): the same box will run several unrelated side projects, so those are host
> concerns with more than one consumer. What is left here is the app's side of the boundary - a compose file
> that is a good tenant of a shared host, and the facts about this app that a host cannot know.
>
> **The folder name is now wrong and stays wrong.** This is no longer an Azure deployment spec. CLAUDE.md,
> `docs/product/roadmap.md` and `docs/product/decisions.md` already reference the path, and renaming a
> directory to improve a title falsifies those references for no gain.
>
> **The HTTPS gate is still open**, and this repository can no longer close it. Cambelt is still served over
> plain HTTP from the NAS.

## Overview

The app becomes **Cambelt**, and becomes deployable onto a shared host that serves it over HTTPS at
**`cambelt.app`**.

**As written on 2026-08-11 this spec also built that host** - an Azure VM in Bicep, Caddy in front of the
gateway, its own PostgreSQL container, and a scheduled pull of dumps and documents to the NAS. **That half
moved out on 2026-08-18** (DEC-020), because the same box will now run several unrelated side projects, which
makes the proxy, the database server and the backup schedule host concerns with more than one consumer. The
sections below keep their original tense where they are still the argument for the work; Scope and Deliverable
are rewritten, because those genuinely changed.

### This does not close the HTTPS gate; the host does

`docs/product/roadmap.md` records HTTPS as blocking public sign-up, "because the MCP endpoint carries a bearer
token, and the shipped stack serves plain HTTP". The stack today publishes the gateway on
`http://synologynas:8082`.

`.app` is on the HSTS preload list, so browsers refuse plaintext to it categorically. The domain choice
enforces the gate rather than merely satisfying it - there is no configuration mistake that can leave that
deployment serving cleartext, because there is no cleartext for a browser to accept.

**The corollary is a real operational risk and travels with the work rather than being dropped:** there is no
HTTP fallback to limp along on. If certificate provisioning fails, the site is unreachable, not degraded. The
DNS cutover must verify the certificate *before* the record moves, not after. That check now belongs to
whoever runs the host, and it is written into the handover material in `sub-specs/`.

**What this repository can still get wrong, and therefore still owns:** `/mcp` is a long-lived streaming
response, and a proxy that buffers it breaks the one feature that most wanted a public address. The gateway's
`Kestrel__EndpointDefaults__Protocols: "Http1"` and YARP's HTTP/1.1 pinning were added for exactly this shape,
with a comment already naming "any reverse proxy". Those settings, and the note that they are the first place
to look at a 502, stay here.

### The rename was smaller than it looks, and one string was embarrassing

Only four user-facing strings carried the old name, and one of them was `index.html:9`, whose `<title>` was
still `cartracker-webapp` - the Vite scaffold default, never changed, and therefore the browser-tab and
bookmark name for a product about to be shown to strangers.

Another was `GaragePage.tsx:41`, reading **"Car Tracker · self-hosted"**. CLAUDE.md recorded the stale
self-hosted line as removed during the landing-page rewrite; that removed a different one, on the garage
footer. This one survived.

Everything else that says `cartracker` is an internal identifier - namespaces, image names, the database, the
Auth0 API audience, a localStorage key. None of it is visible to a user and all of it is expensive to change,
so none of it changed.

> **Shipped 2026-08-17, and it was six strings rather than four.** The guard written to stop the old name
> coming back found the garage *footer* prose still opening "Self-hosted, and your garage is yours", and the
> chat system prompt introducing the assistant as "the assistant inside Car Tracker" - model-facing text that
> the model says back to the owner. See task 1.

## User Stories

### It has a name and an address

As someone told about this app, I want to type `cambelt.app` and arrive, so that the product has an identity
that is not a hostname on somebody's home network.

### It is reachable over HTTPS, including by an assistant

As someone connecting an MCP client, I want the endpoint to be `https://cambelt.app/mcp`, so that the bearer
token minted in Account is not crossing the internet in clear text. Today `docs/mcp-connect.md` can only offer
a LAN address.

### The app is a good tenant of a host it does not own

As the person operating a box that runs several of my projects, I want Cambelt's compose file to declare what
it needs and nothing more - two networks, a database, a data directory - so that adding it to the host is
reading one file, and so that nothing about Cambelt's release process is special.

### It still comes up on its own

As someone with a fresh checkout, a laptop or a spare NAS, I want the whole stack in one command without a
shared proxy or a shared database existing anywhere, so that the tenant shape does not become the only shape.

### The data survives the box

As the person responsible for other people's vehicle records, I want a copy of the database and the uploaded
documents that lives somewhere the host provider cannot reach, so that a lost subscription, a mistaken
resource-group delete or a compromised host is not the end of the data.

> The *mechanism* for this is the host's - one backup schedule covering every project. **The half that is
> Cambelt's own is the pairing rule**: a dump restored without `${DATA_ROOT}/documents` gives `Document` rows
> pointing at nothing, and no host can infer that from looking at the containers.

## Spec Scope

1. **Rename to Cambelt on the public surface** - the UI strings, the page title, the favicon, README and docs.
   No internal identifier changes. **Done 2026-08-17.**
2. **A compose file that is a good tenant** - the app's three services on two external networks (`edge`,
   `data-cambelt`), publishing no ports, expecting a database that already exists.
3. **A `standalone` profile** carrying `postgres`, `caddy`, `watchtower` and `db-backup`, so the
   self-contained stack that runs on the NAS today is one flag away and a fresh checkout still works.
4. **`docs/deployment-shared-host.md`** - the tenant contract: which networks, which database and role, which
   directories, which environment keys, and what the host must get right that Cambelt cannot check.
5. **`docs/mcp-connect.md` gains the public address**, once there is one. The first address the MCP recipe can
   offer from outside the LAN, which is most of the practical point of the exercise.

## Out of Scope

- **The host itself.** The VM, the Bicep, the shared proxy, the shared PostgreSQL server, the off-site backup
  pull and the operator's guide for the box all live in the hosting repository (DEC-020). The Azure research
  already done stays in `sub-specs/` as handover material, marked as such.
- **The isolation and GDPR work.** `docs/specs/2026-08-11-pre-public-release-gates/` closed gates one and
  three. **Deploying to `cambelt.app` does not make sign-up safe to open** - the allowlist is what keeps the
  door shut, and HTTPS is a gate this repository cannot assert has been met.
- **An Auth0 custom domain.** It needs a paid plan costing more than the hosting, so the login page will read
  `usualexpat.uk.auth0.com`. Recorded as a known wart, not solved here.
- **Managed PostgreSQL.** The app talks to a Postgres server over a connection string and does not care whose
  it is. Whether the host runs a container or buys Flexible Server is the host's decision to price.
- **Retiring the Synology deployment.** It documents a real, working install and the doc stays.

## Expected Deliverable

1. `docker compose --profile standalone up -d` brings up the whole app from a fresh checkout, exactly as it
   does today, with no external network and no shared database required.
2. `docker compose up -d` on a host that already provides `edge`, `data-cambelt` and a `cambelt` database
   brings up the app with **no published ports**, ready for the host's proxy to route a hostname at it.
3. `docs/deployment-shared-host.md` states the tenant contract completely enough that adding Cambelt to a new
   host needs nothing from this spec, and names the two things the host must get right that Cambelt cannot
   check for itself: an unbuffered `/mcp`, and the documents directory travelling with the dumps.
