# Spec Requirements Document

> Spec: Documents — upload, tag, link, photo baselines
> Created: 2026-07-16
> Status: Planning

## Overview

Give the papers and photo sets a home: the 17th and last workbook screen, and the only one whose entity is
already built. `Document` exists in full — entity and EF configuration both — so this spec is file-upload
infrastructure, a `DocumentEndpoints` group, and the screen, not a data model. The bytes land on a mounted
volume with the path on the row (DEC-005), never in Postgres.

## User Stories

### Upload a paper and tag it on the way in

As the owner, I want to upload a PDF or photo and say what it is as I add it, so that a certificate is filed
the moment it arrives rather than living in a phone's camera roll.

BT53 already has real papers — the V5C, the Admiral certificate, the 8 Jul 2026 MOT pass, invoices from K&P
Motors — and today there is nowhere in the app to put any of them. Upload is multipart: the file, a title, a
`DocumentType`, and an optional link to the record it belongs to. The design's toast says it exactly — *"tag
on the way in, link to a record if it belongs to one"* — and the tagging is the `Type` plus that link, because
that is what the schema models (see the technical spec on why there is no free-form tag table).

### Link a document to the record it belongs to

As the owner, I want the major-service invoice to point at its service record, so that opening the record and
opening the paper are one hop apart and neither can be orphaned by accident.

`Document` carries three real nullable FKs — `ServiceRecordId`, `ExpenseEntryId`, `IssueId` — README §3.9's
exact three targets, chosen over a polymorphic pair so each link gets real referential integrity. The links
are severed, never cascaded (`OnDelete(SetNull)`): delete the service record and the MOT certificate stays,
its link nulled. That is the opposite of the fuel and service expense mirror, which cascades — a mirror is a
shadow of its source, a document is evidence that outlives it.

### Photo baselines as evidence for an issue

As the owner, I want the March 2026 condition photos kept as a baseline set, so that when the head gasket or
the sills are argued about later, "worsening" is measured against a real starting point rather than memory.

A photo is a `Document` with `Type = Photo`, and a condition photo linked to an `Issue` is what makes the
issues watchlist evidential rather than anecdotal. The design says it plainly: *"Photos are evidence, not
decoration — each links to the issue or record it documents, and the baseline set is what 'worsening' is
measured against."* The rear-tyre-cracking and headlamp-haze shots each carry `→ issue`; the front-¾, rear,
sills and interior shots are the unlinked baseline.

## Spec Scope

1. **File-upload infrastructure** — multipart receive, write to the configured volume root, store the relative
   path on `FilePath`, capture `ContentType` and `SizeBytes`, and compute `Sha256` while streaming, for dedup
   and backup verification.
2. **`DocumentEndpoints`** — the CRUD group that does not yet exist: POST upload, GET list/metadata, GET file
   bytes (view/download), PATCH the metadata and links, DELETE. Follows the existing endpoint groups' shape —
   registration → id via `VehicleLookup`, behind the gateway's `X-Api-Key`.
3. **Documents screen** — `documents.dc.html` ported: the papers list on `<DataTable>` and the photo-sets grid,
   with an upload sheet and a simple in-browser viewer plus download-keeps-the-original.
4. **Type, link and the chips** — `DocumentType` chosen on upload; the link chips (`→ service record`,
   `→ expense`, `→ issue`, `→ policy`) rendered from the FKs, navigating to the linked record.
5. **The photo-baseline link to issues** — condition photos linked to an `Issue`, surfaced as the baseline set,
   cross-referenced from the issues concept so a baseline is what worsening is compared against.

## Out of Scope

- **OCR / receipt parsing.** A document store files bytes; reading a receipt's total off a photo is the
  receipt-photo-capture spec's job, and folding it in here would couple filing to extraction. Kept separate.
- **Versioning.** A re-upload is a new `Document`, not a revision of an old one — the workbook has no version
  chain and no story here needs one. `Sha256` catches an *accidental* duplicate; it does not build a history.
- **Backup of the volume.** The folder-copy-alongside-`pg_dump` is Phase 5 hardening (tech-stack). This spec
  writes files to the volume; standing up their backup is a separate, later concern.
- **A free-form tags table.** The design's chip row *looks* like arbitrary tags, but the schema models one
  `DocumentType` plus the three link FKs and `Notes` — nothing else. Inventing a tags collection would be a
  schema change this spec has no mandate for; the chips are `Type` and the links, and that is the honest read.
- **`search_documents` and any MCP document tool.** They depend on this feature existing and are excluded from
  the MCP spec until it does; they are not built here either.
- **`bytea` or MinIO storage.** DEC-005 chose local-volume-plus-path and rejected both — `bytea` bloats
  `pg_dump`, MinIO is a third container for one user. Not reopened.

## Expected Deliverable

1. Uploading BT53's MOT-pass certificate through the screen — `Type = MOT`, linked to the 8 Jul 2026 service
   record — files it to the volume, lists it in Papers with a `→ service record` chip, and View opens it while
   Download returns the original bytes unchanged.
2. Uploading a "rear tyre cracking" photo linked to an issue and a "front ¾ baseline" photo with no link shows
   both in the photo grid, the first carrying `→ issue` and the second sitting in the unlinked baseline set.
3. `Sha256` is computed and stored on upload; uploading a byte-identical file is detected as a duplicate rather
   than filed twice blind.
