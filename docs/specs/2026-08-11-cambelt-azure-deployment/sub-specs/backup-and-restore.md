# Backup & Restore Specification

This is the backup specification for the spec detailed in @docs/specs/2026-08-11-cambelt-azure-deployment/spec.md

## The shape

```
Azure VM  /srv/cambelt/pgdata      ← live database
          /srv/cambelt/documents   ← uploaded bytes (DEC-005)
          /srv/cambelt/backups     ← db-backup sidecar, 6-hourly, 7/4/6 rotation
                    ▲
                    │  scheduled pull over SSH, forced-command key, NAS initiates
                    │
Synology NAS  /volume1/backup/cambelt/{backups,documents}
```

The NAS pulls. Nothing on Azure holds a NAS credential, and the NAS needs no inbound exposure - which matters,
because an internet-facing Synology is among the most-targeted devices in home networking.

## What is kept, and why both paths

**The `db-backup` sidecar is unchanged.** `pg_dump` every 6 hours to `${DATA_ROOT}/backups/{daily,weekly,
monthly}`, rotated 7 daily / 4 weekly / 6 monthly, with `--clean --if-exists` so a restore overwrites cleanly.
It is deliberately not Watchtower-labelled: the database and its backup tool are never auto-updated.

**The pull must cover `documents` as well as `backups`.** The sidecar dumps Postgres only. A restored dump
without the bytes gives you `Document` rows pointing at files that do not exist - the documents screen renders
broken images for records the user believes they still have. `docs/deployment-synology.md:128-130` already
makes this warning for the Hyper Backup case; it is the same warning and the same cost here.

The 6-hourly cadence exists so a fresh dump always predates a Watchtower auto-update. That reasoning survives
the move unchanged.

## The forced-command key

This is the most important line in this document.

The NAS authenticates with a dedicated SSH key whose `authorized_keys` entry restricts it to the backup
command and nothing else:

```
command="/usr/local/bin/cambelt-backup-export",restrict ssh-ed25519 AAAA... cambelt-nas-pull
```

`restrict` disables port forwarding, agent forwarding, PTY allocation and X11. `command=` means the key can
run one read-only export and cannot open a shell **regardless of what the client asks for**.

Port 22 is open to the internet because the NAS pulls from a residential dynamic address (see the
infrastructure sub-spec). A leaked backup key must therefore not be a shell on a public VM, and this is what
guarantees it is not. A plain SSH key here would make the backup mechanism the weakest thing on the box.

The key is separate from any administrative key, and lives only on the NAS.

## Restore

Adapted from the recipe already proven in `docs/deployment-synology.md:132-140`:

```sh
# stop writers first
docker compose stop webapi
gunzip -c /srv/cambelt/backups/daily/cartrackerdb-<timestamp>.sql.gz \
  | docker exec -i $(docker compose ps -q postgres) psql -U postgres -d cartrackerdb
docker compose start webapi
```

Restoring from the **NAS** copy means shipping the dump back to the VM first - or, for a full disaster,
provisioning a new VM from Bicep, restoring the dump and copying `documents` into place. Write both paths out;
the second is the one that will be needed on the worst day, and it is the one nobody rehearses.

## Rehearsal

**Quarterly, and before the deployment ever carries anyone else's data.** Restore the most recent NAS copy
into a scratch database, confirm the row counts and confirm a document downloads. A backup that has never been
restored is a hypothesis.

Record the date of the last successful rehearsal in `docs/deployment-azure.md`, where it is visible, rather
than trusting memory.

If the Raspberry Pi under discussion happens, this is the job it should have: pull, restore into a scratch
Postgres on a schedule, verify, and complain when it fails. That turns the rehearsal from a diary entry into a
thing that tells you when it stops working.

## Residual risk, stated and accepted

Two properties this topology does not have. Both were weighed and the simpler design chosen; neither should be
rediscovered later as a surprise.

1. **The only off-site copy is at home**, so it depends on the house connection being up when the job runs. A
   long outage means a long gap, and nothing alerts on it unless the pull job reports failures somewhere you
   read.
2. **There is no immutable intermediate.** A compromised VM could corrupt the dumps before the NAS pulls them,
   and the NAS would faithfully replicate the corruption. Retention on the NAS side is the only thing standing
   between that and total loss, so **keep more history on the NAS than on the VM** - the VM's rotation is
   7/4/6; the NAS should hold at least 90 days.

**The upgrade path, priced, so revisiting is cheap:** push dumps and documents to an Azure Storage account in
a *separate resource group* with versioning, soft-delete and an immutability policy, and have the NAS pull
from there with a read-only SAS. Cool-tier block blob in UK South is **£0.008/GB/month** - a few gigabytes is
pennies. That adds an append-only copy the VM cannot rewrite, and removes the dependency on the house
connection. It was not taken now because it is a second moving part for a deployment with one user; it becomes
worth taking when the deployment holds other people's documents.
