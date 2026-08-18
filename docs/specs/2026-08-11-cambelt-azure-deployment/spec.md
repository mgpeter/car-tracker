# Spec Requirements Document

> Spec: Cambelt on Azure - a name, an address, and a box that can be rebuilt
> Created: 2026-08-11
> Status: **In progress.** Task 1 - the rename - landed 2026-08-17 and is the only part needing no Azure
> subscription, no DNS and no certificate. Tasks 2–7 (Bicep, cloud-init, cutover, backups, the operator's
> guide) are untouched, so the app is called Cambelt and is still served over plain HTTP from the NAS, and
> **the HTTPS gate is still open**.

## Overview

The app becomes **Cambelt**, served over HTTPS at **`cambelt.app`** from a small Azure VM provisioned by
Bicep, with the database dumped on a schedule and pulled off-site to the NAS.

Three things happen together because they cannot sensibly happen apart: a product going public needs a name it
can be searched for, an address it can be reached at, and a host that is not someone's spare NAS on a
residential connection.

### This closes the HTTPS gate

`docs/product/roadmap.md:208-210` records HTTPS as blocking public sign-up, "because the MCP endpoint carries
a bearer token, and the shipped stack serves plain HTTP". The stack today publishes the gateway on
`http://synologynas:8082`.

`.app` is on the HSTS preload list, so browsers refuse plaintext to it categorically. The domain choice
enforces the gate rather than merely satisfying it - there is no configuration mistake that can leave this
deployment serving cleartext, because there is no cleartext for a browser to accept.

**The corollary is a real operational risk and belongs here rather than in a footnote:** there is no HTTP
fallback to limp along on. If certificate provisioning fails, the site is unreachable, not degraded. The DNS
cutover must therefore verify the certificate *before* the record moves, not after.

### The rename is smaller than it looks, and one string is embarrassing

Only four user-facing strings carry the old name. One of them is `index.html:9`, whose `<title>` is still
`cartracker-webapp` - the Vite scaffold default, never changed, and therefore the browser-tab and bookmark
name for a product about to be shown to strangers.

Another is `GaragePage.tsx:41`, which reads **"Car Tracker · self-hosted"**. CLAUDE.md records the stale
self-hosted line as removed during the landing-page rewrite; that removed a different one on the garage
footer. This one survived, and it becomes false the moment the app runs on Azure.

Everything else that says `cartracker` is an internal identifier - namespaces, image names, the database, the
Auth0 API audience, a localStorage key. None of it is visible to a user and all of it is expensive to change,
so none of it changes.

## User Stories

### It has a name and an address

As someone told about this app, I want to type `cambelt.app` and arrive, so that the product has an identity
that is not a hostname on somebody's home network.

### It is reachable over HTTPS, including by an assistant

As someone connecting an MCP client, I want the endpoint to be `https://cambelt.app/mcp`, so that the bearer
token minted in Settings is not crossing the internet in clear text. Today `docs/mcp-connect.md` can only
offer a LAN address.

### The box can be thrown away

As the person operating this, I want the VM described in source and rebuildable in minutes, so that the host
is disposable and only the data is precious.

This is what makes VM-level backup unnecessary rather than merely skipped: if Bicep plus cloud-init plus
compose reconstruct the machine, the only irreplaceable thing is the database and the documents volume, which
the dumps already cover.

### The data survives the box, and survives Azure

As the person responsible for other people's vehicle records, I want a copy of the database and the uploaded
documents that lives somewhere Azure cannot reach, so that a lost subscription, a mistaken resource-group
delete or a compromised VM is not the end of the data.

## Spec Scope

1. **Rename to Cambelt on the public surface** - four UI strings, the page title, the favicon, README and
   docs. No internal identifier changes.
2. **Azure infrastructure in Bicep** - resource group, network, NSG, static IP, Ubuntu VM, data disk, Key
   Vault. One `az deployment group create` from nothing to a running host.
3. **TLS via Caddy on the VM**, with Cloudflare serving DNS only. Automatic Let's Encrypt certificates; the
   gateway stops being published to the internet.
4. **Backups** - the existing `db-backup` sidecar unchanged, plus a scheduled pull from the NAS covering both
   the dumps and the documents volume, over a forced-command SSH key.
5. **`docs/deployment-azure.md`** - the operator's guide, modelled on the Synology one, which stays.

## Out of Scope

- **The isolation and GDPR work.** `docs/specs/2026-08-11-pre-public-release-gates/` closes gates one and
  three. This spec closes gate two only. **Deploying to `cambelt.app` does not make sign-up safe to open** -
  that needs both specs, and the allowlist from the other one is what keeps the door shut in the meantime.
- **An Auth0 custom domain.** It needs a paid plan costing more than the hosting, so the login page will read
  `usualexpat.uk.auth0.com`. Recorded as a known wart, not solved here.
- **Azure Blob as a backup intermediate.** Considered and deliberately not taken; the upgrade path is
  documented and priced in the backup sub-spec so the decision can be revisited cheaply.
- **Moving off Docker Compose.** Container Apps and App Service were both priced and rejected for this
  workload - see the infrastructure sub-spec.
- **Managed PostgreSQL.** Postgres stays in a container on the VM, as it is today. Flexible Server would
  roughly double the bill for PITR this deployment does not yet need.
- **Retiring the Synology deployment.** It documents a real, working install and the doc stays.

## Expected Deliverable

1. `https://cambelt.app` serves the app with a valid certificate, and `https://cambelt.app/mcp` accepts a
   token minted in Settings.
2. `az deployment group create` produces the whole host from an empty resource group, and destroying and
   recreating the VM loses nothing but uptime.
3. A dump and a documents copy land on the NAS on schedule, and a restore from them has been rehearsed at
   least once before the deployment carries anyone else's data.
