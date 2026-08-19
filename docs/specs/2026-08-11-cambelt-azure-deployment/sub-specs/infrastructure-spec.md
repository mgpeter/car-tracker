# Infrastructure Specification

This is the infrastructure specification for the spec detailed in @docs/specs/2026-08-11-cambelt-azure-deployment/spec.md

> ## Handover material, not a work item
>
> **Nothing in this file is built from this repository (DEC-020, 2026-08-18).** The VM, its Bicep, the NSG,
> the Key Vault and the cloud-init all moved to a separate hosting repository, because the same box will run
> several unrelated side projects and a host with more than one tenant cannot be defined inside one of them.
>
> It is kept, rather than deleted, because the research was paid for once: the priced rejection of Container
> Apps and App Service, the sizing argument for 4 GiB, the naming scheme, the NSG rules and the disposable-box
> reasoning are all still the right answers. **Read it as the starting point for the hosting repository, and
> re-price it before acting** - the figures were pulled on 2026-08-11.
>
> **Three things changed with the premise and are wrong below unless you read them with this note:**
>
> 1. **Sizing.** `B2als_v2` (2 vCPU, 4 GiB) was chosen for one app. A shared host wants `B2as_v2` - the same
>    2 vCPU with 8 GiB, so no quota change - before a third project lands. The resize is in-place: same NIC,
>    same IP, same disks, a reboot, and `az vm list-vm-resize-options` says whether it can happen without
>    deallocating. **Change it in `main.bicepparam`, not only in the portal**, or the next deployment resizes
>    it back.
> 2. **PostgreSQL is shared.** One server, one database and one role per project, with a per-project
>    `data-<project>` network so an app cannot reach a neighbour's database at all. One instance per project
>    would cost its own `shared_buffers` and background workers, roughly 400-600 MB across four idle projects.
> 3. **The proxy is shared**, terminating TLS for every project and routing by hostname. Certificates are
>    per-host over HTTP-01 to begin with; a `*.usualexpat.com` wildcard needs DNS-01 and a scoped Cloudflare
>    token, which is what a service that is not publicly reachable would require. Subdomains of
>    `usualexpat.com` coexist with the apex staying on Azure Static Web Apps - DNS is per-record - but check
>    whether the apex sends HSTS with `includeSubDomains` before pointing a subdomain at a box whose
>    certificates you manage.
>
> Admin surfaces (a Docker UI, `psql`) are root-equivalent and belong on a tailnet or behind an SSH tunnel,
> not on the public listener. That is a hosting-repository decision; it is noted here because the NSG table
> below opens exactly three ports and should keep doing so.

## What it costs

**UK South, GBP, Linux, pay-as-you-go list prices, retrieved from the Azure retail pricing API on
2026-08-11.** Re-check before committing to anything: list prices move, and savings-plan rates are
commitments, not discounts you can walk away from.

| Resource | £/hour | £/month |
|---|---|---|
| `Standard_B2als_v2` - 2 vCPU / 4 GiB - **recommended** | 0.032198 | **23.50** |
| ↳ same, 1-year savings plan | 0.024471 | 17.86 |
| ↳ same, 3-year savings plan | 0.017065 | 12.46 |
| `Standard_B1ms` - 1 vCPU / 2 GiB - frugal | 0.017900 | 13.07 |
| `Standard_B2ats_v2` - 2 vCPU / 1 GiB | 0.008000 | 5.84 |
| OS disk - Standard SSD E4, 32 GiB (incl. mount fee) | - | 2.25 |
| Data disk - Standard SSD E6, 64 GiB (incl. mount fee) | - | 4.45 |
| Static Standard public IPv4 | 0.003800 | 2.77 |
| Key Vault (a handful of secret reads per boot) | - | ~0.00 |
| Egress | - | ~0.00 |

**All-in: ≈£32.97/month** on `B2als_v2` at list, **≈£27.33** on a one-year savings plan, **≈£22.54** on
`B1ms`.

Two results worth writing down because neither is guessable:

- **`B2als_v2` on a three-year savings plan (£12.46) costs less than `B1ms` at list (£13.07)** - twice the RAM
  and twice the vCPU for less money, if you are willing to commit for three years. A one-year plan is the
  sensible ceiling for a project that might move.
- **`B2ats_v2` is startlingly cheap at £5.84** and is the trap in the table. It is 2 vCPU with **1 GiB** of
  RAM. See sizing.

### Sizing: why 4 GiB

Measured intent rather than measured fact - verify after the first deploy - but the resident set is roughly
Postgres 200–300 MB, webapi 150–250 MB, gateway 100–150 MB, Watchtower and the backup sidecar ~20 MB each:
call it 600–750 MB.

On 2 GiB that fits and leaves little for the page cache Postgres depends on for read performance. On 1 GiB it
fits only with swap, and swapping a database onto a Standard SSD is how a cheap VM becomes a slow one. The
difference between 2 and 4 GiB is about £10/month, which is less than the cost of diagnosing memory pressure
once.

**Set `DOTNET_gcServer=0` on both .NET containers regardless of SKU.** Server GC sizes its heaps per core and
is tuned for throughput on machines that are not this one; workstation GC materially cuts resident memory on a
2-core box, and this workload has no throughput problem to solve.

### Why not the managed alternatives

Both were priced before choosing a VM, and the arithmetic is the argument:

- **Container Apps** always-on: two replicas at 0.25 vCPU / 0.5 GiB burn roughly £27/month of compute after
  the monthly free grant, *before* a database - about three times the VM for the same workload. It earns its
  price by scaling to zero, which this app cannot do: `RemindersBackgroundService` stops running, and cold
  starts land on the UI and on `/mcp`.
- **App Service + PostgreSQL Flexible Server** is the better-engineered answer at roughly £28–35/month, and
  buys PITR and patching. It also requires moving document storage off local disk to Blob, because App Service
  storage is not durable for it. Worth revisiting when the deployment carries enough other people's data that
  PITR stops being a luxury; not worth it on day one.
- **A single VM changes nothing about the app.** `deploy/docker-compose.yml` runs as-is, documents stay on a
  bind mount, Watchtower keeps working, and the migration is a `docker compose up`.

State plainly in the DEC: for a small always-on workload Azure is the most expensive mainstream option, and
the same VM is roughly a fifth of the price at Hetzner. Azure is chosen deliberately, not because it is cheap.

## Resources

`deploy/azure/main.bicep` plus `main.bicepparam`, deployed at resource-group scope. Roughly ten resources;
their names are settled under *Naming* below, not chosen while writing the template:

| Resource | Notes |
|---|---|
| Resource group | Created out-of-band (`az group create`) - RG creation is subscription-scope |
| Virtual network + subnet | One `/24`, one subnet. No peering, no gateway |
| Network security group | See rules below |
| Public IP | **Standard SKU, static.** Basic SKU is retired; static because the DNS record points at it |
| Network interface | Attached to the NSG and the public IP |
| Linux VM | Ubuntu 24.04 LTS, `B2als_v2`, SSH key auth only |
| OS disk | Standard SSD, 32 GiB |
| Data disk + attachment | Standard SSD, 64 GiB, mounted at `/srv/cambelt` |
| Key Vault + secrets | RBAC-authorised, read by the VM's managed identity |
| User-assigned managed identity | Granted Key Vault Secrets User on the vault |

### Naming

`<type>-<workload>-<env>[-role][-NN]`, lower case, following the Cloud Adoption Framework's abbreviations.
**These are the names `main.bicep` uses**; invent none at authoring time, because almost nothing in Azure can
be renamed in place and a scheme settled after the first deploy costs a redeploy.

> **Read `cambelt` here as a placeholder for the host, not for this app (DEC-020).** A box shared by several
> projects is not "the cambelt workload", and naming its resource group after one tenant is the same mistake
> as defining the host inside one tenant's repository. Pick the workload token when the hosting repository is
> created and use it consistently; everything else in this table - the abbreviations, the `-01` rule, the
> globally-unique warnings, the tags - is unaffected.

| Resource | Name |
|---|---|
| Resource group | `rg-cambelt-prod` |
| Virtual network | `vnet-cambelt-prod` |
| Subnet | `snet-cambelt-prod-web` |
| Network security group | `nsg-cambelt-prod-web` |
| Public IP | `pip-cambelt-prod` |
| Network interface | `nic-cambelt-prod-01` |
| Linux VM | `vm-cambelt-prod-01` |
| OS disk | `osdisk-cambelt-prod-01` |
| Data disk | `disk-cambelt-prod-data-01` |
| Key Vault | `kv-cambelt-prod` |
| User-assigned managed identity | `id-cambelt-prod` |

**`-01`, never `-main`.** An instance suffix earns its place by saying *which* one this is, and "main" only
means something while there is exactly one - which is the case where the suffix was not needed. When the
second arrives you have `-main` and no word for the other. `-01` increments and sorts. It goes on the things
that could plausibly multiply (VM, NIC, disks) and stays off the singletons (resource group, network, vault,
identity).

**No region token, deliberately.** CAF offers `-uks`, and it only pays for itself when the same workload runs
in two regions. This one will not, and the resource group carries the region. If that judgement is ever
reversed, reverse it *before* the first deploy rather than after.

**Three constraints that bite:**

- **The Key Vault name is globally unique across all of Azure**, 3–24 characters, alphanumerics and hyphens,
  starting with a letter. `kv-cambelt-prod` is 15 and legal but may already be someone else's, and the failure
  arrives at deploy time. Check with `az keyvault check-name-availability`; if it is taken, suffix with four
  characters of `uniqueString(resourceGroup().id)` rather than inventing a word.
- **Storage accounts and container registries reject hyphens** and take lower-case alphanumerics only, 3–24
  characters, so the scheme cannot survive there: they would be `stcambeltprod` and `crcambeltprod`. Neither
  exists in this deployment; it is written down so that the exception reads as known rather than as a slip the
  day one appears.
- **The public IP's optional DNS label is also globally unique**, per region: `cambelt-prod` yields
  `cambelt-prod.uksouth.cloudapp.azure.com`. Worth setting, because it gives a stable address to SSH to and to
  verify the stack against *before* `cambelt.app` points anywhere - which the certificate check needs (old task 4.4, now the hosting repository's), `.app` being
  HSTS-preloaded with no HTTP fallback to debug over.

**Tags carry what names cannot.** A name is fixed at creation; a tag is not. On the resource group, and by
convention on the resources beneath it: `workload=cambelt`, `env=prod`, `managedBy=bicep`, `repo=<url>`. This
is what keeps the bill legible once anything else lands in the subscription.

Name the deployment as well as the resources: `az deployment group create -n cambelt-<yyyy-MM-dd>` gives a
readable deployment history, which is what `what-if` output is read against.

### Deployment commands

```powershell
az group create -n rg-cambelt-prod -l uksouth --tags workload=cambelt env=prod managedBy=bicep
az keyvault check-name-availability -n kv-cambelt-prod   # globally unique; do this before the first deploy
az deployment group what-if -g rg-cambelt-prod -f deploy/azure/main.bicep -p deploy/azure/main.bicepparam
az deployment group create  -g rg-cambelt-prod -f deploy/azure/main.bicep -p deploy/azure/main.bicepparam `
  -n cambelt-2026-08-17
```

`what-if` is the plan step, and it is the reason Bicep is sufficient here - the property people reach for
Terraform to get.

### Why Bicep

- **No state.** ARM's deployment history in the resource group is the record, and `what-if` diffs against live
  resources rather than a file. Terraform for a solo project either keeps state locally, where it is lost with
  the laptop, or in a storage account that must be created before anything can be provisioned - a
  bootstrapping problem with no equivalent here. For one person and ten resources, one fewer thing that can be
  lost or corrupted is worth more than portability.
- **First-party and free**, ships with `az`, and new Azure features land in ARM immediately.
- **Fits the repo.** `scripts/release.ps1` is already PowerShell driving a CLI; `az deployment` is the same
  shape.

The honest cost: **Bicep is Azure-only.** If the project leaves Azure, this is dead weight - and it cannot
manage the Cloudflare DNS records, so those are three manual entries in the dashboard. Automating three
records set once would have meant a Terraform provider, a scoped API token and a state backend, which is more
machinery than it removes. Revisit if DNS starts changing often.

## Networking

| Port | Source | Purpose |
|---|---|---|
| 80 | Internet | ACME HTTP-01 challenge and the redirect to 443 |
| 443 | Internet | The app |
| 22 | Internet, key-only | SSH, and the NAS backup pull |

**The gateway is not published.** Today `docker-compose.yml` maps `${GATEWAY_PORT:-8082}:8080` on the host. On
Azure it binds to `127.0.0.1` only, and Caddy is the sole public listener - so there is no path to the app that
bypasses TLS.

**Port 22 is open to the internet, and that is a deliberate compromise.** The NAS initiates the backup pull
from a residential dynamic address, so an IP-restricted rule would need DDNS-driven NSG updates. Instead:
password authentication and root login disabled, key-only auth, and the NAS's key restricted by a
forced command (see the backup sub-spec) so it cannot open a shell even if it leaks. Azure Bastion and
just-in-time access both solve this properly and both cost more per month than the VM.

Keep 80 open: Caddy's ACME HTTP-01 challenge needs it, and closing it breaks renewal silently, sixty days
later, which is the worst possible time to discover it.

## Secrets

`POSTGRES_PASSWORD`, the five `Lookup:` DVLA/DVSA values, and the `Auth0:Management:` client secret the other
spec introduces all live in Key Vault, read at boot by the VM's managed identity and written into the `.env`
beside the compose file.

Not Bicep parameters: a parameter value lands in the deployment history in plain text, readable by anyone with
reader access to the resource group. Not baked into an image for the obvious reason.

The vault is RBAC-authorised rather than using access policies, and the identity gets **Key Vault Secrets
User** - read, not manage.

## cloud-init

`deploy/azure/cloud-init.yaml`, passed as `customData`:

1. `apt` install Docker Engine, the Compose plugin, and the Azure CLI.
2. Partition, format and mount the data disk at `/srv/cambelt`; create `pgdata`, `documents` and `backups`
   beneath it. Add the mount to `/etc/fstab` **by UUID**, not by device name - device ordering is not stable
   across reboots and a database that mounts on the wrong disk is worse than one that fails to mount.
3. Fetch secrets from Key Vault with the managed identity; write `/srv/cambelt/.env`, mode `0600`.
4. Clone or fetch the repo's `deploy/` directory and `docker compose up -d`.
5. Enable `unattended-upgrades` for OS security patches - the one thing a managed platform would have done for
   us, and the one thing most likely to be forgotten.
6. Harden `sshd`: `PasswordAuthentication no`, `PermitRootLogin no`.

This is what makes the VM disposable, and therefore what makes VM-level backup unnecessary: the machine is
reproducible from source in minutes, and only `/srv/cambelt` is irreplaceable. Say so explicitly in
the hosting repository's deployment doc, because "no VM backup" reads as an oversight otherwise.
