# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-documents/spec.md

## The entity already exists — verified, no schema change

This spec was checked against `src/CarTracker.Data/Document.cs` and
`src/CarTracker.Data/Configuration/DocumentConfiguration.cs` **before it was written**, and there is
**no database change and therefore no `database-schema.md`.** The service-history spec learned the hard way
that "the entity exists" is not the same as "nothing needs to point at it" — so this was checked properly:

- **`Document.cs`** carries `Id`, `VehicleId`, `DocumentType Type`, `Title`, `DateOnly? DocumentDate`,
  `string FilePath`, `string ContentType`, `long SizeBytes`, `string? Sha256`, the three nullable link FKs
  `ServiceRecordId` / `ExpenseEntryId` / `IssueId`, `Notes`, and the `IAuditable` triple
  (`CreatedAt` / `UpdatedAt` / `Source`). Every column this feature needs is present.
- **`DocumentConfiguration.cs`** is complete too: the `documents` table, the `ck_documents_type` /
  `_size_bytes` / `_notes` check constraints, `Type` as `varchar(20)` via `HasConversion<string>()`, the
  vehicle FK cascading, all **three link FKs configured `OnDelete(DeleteBehavior.SetNull)`**, the
  `ix_documents_vehicle_type` index, and `ConfigureAudit`. Nothing is missing and nothing is pointing the wrong
  way — unlike the service spec, where `expense_entries` had no column to point back at a record.

So: no new table, no new column, no migration. If the port reveals a genuine gap, add a `database-schema.md`
then — but the check says there is none.

## Storage

- Bytes live on a **mounted volume** (DEC-005, tech-stack `asset_hosting`), never in Postgres — `bytea` bloats
  `pg_dump` and MinIO is a third container for one user, both rejected. A configured **root path** is bound
  from configuration; `FilePath` stores the path **relative to that root**, exactly as `Document.cs`'s own
  remark states, so moving the volume does not rewrite every row.
- Layout under the root is by vehicle id then a generated file name (the `Sha256` or a GUID), not the original
  filename — two receipts both called `scan.pdf` must not collide, and a user-supplied name is not a safe path
  component. The original name is not load-bearing; `Title` is what the screen shows.
- **`Sha256` is computed while streaming the upload to disk**, in one pass, not by re-reading the file after.
  It backs two things the entity comments already name: duplicate detection (a byte-identical re-upload is
  caught, not filed twice) and backup verification (the Phase 5 folder-copy can be checked against the row).

## `DocumentEndpoints`

- A new `DocumentEndpoints.cs` under `src/CarTracker.WebApi/Endpoints/` — it does not exist today; Documents is
  the only unbuilt workbook screen. Group route `/api/vehicles/{registration}/documents`, resolving
  registration → id via `VehicleLookup` and returning `VehicleLookup.NotFound` on miss, exactly like
  `FuelEndpoints` and `ServiceEndpoints`.
- **Upload is `multipart/form-data`**, not a JSON body — the other groups all take JSON, so this is the first
  endpoint that reads a file part plus form fields. Validate `ContentType` against an allow-list (PDF and
  common image types; the design accepts *"PDF or photos"*) and reject anything else with a 400, and cap the
  size. `SizeBytes` and `ContentType` are captured from the received part, not trusted from a client field.
- **Reads never re-query past the metadata.** GET list returns `Document` metadata rows; GET file streams the
  bytes from the volume with the stored `ContentType`, `Content-Disposition: inline` for view and `attachment`
  for download — *"download keeps the original"* is a header, not a second copy.
- **Links are set on create or via PATCH, and at most one is non-null.** A document is mirrored-from-nothing —
  it is filed, and optionally attached to exactly one of a service record, an expense or an issue. Setting a
  link validates that the target exists **and belongs to the same vehicle**; a cross-vehicle link is a 400.
- **DELETE removes the row and the file.** Unlike a fuel or service mirror there is no cascade *into* the
  document — the FKs point outward and are `SetNull` — so deleting the *document* is the only thing that
  removes its bytes, and it must remove them, or the volume grows orphans the row no longer references.
- Every write stamps `Source` through the existing audit interceptor. No `AnomalyScanner` call: a document is
  not a derived input, moves no figure, and trips no detector — this is the one write path in the app that does
  not scan, and that is correct, not an omission.

## The screen

- Ported from `archive/dashboard-full-claude-design/documents.dc.html` — unwrap the `<x-dc>` template to real
  JSX (`sc-if` → `&&`, the `.drow` list → `.map()`), strip `support.js` and `image-slot.js` as harness.
- **Papers is `<DataTable>`** — its next consumer after fuel, expenses, mileage and service. Its container-query
  reflow already handles the design's `@media (max-width:820px)` collapse; do not re-fork the media query onto
  it.
- **Photo sets is a grid, not a table** — six tiles, `Type = Photo`, with a caption and a date, and the
  issue-linked tiles carrying `→ issue`. A grid of images is not columns of aligned figures, so the seam that
  keeps checks a list keeps this one a grid.
- **Tag chips are `Type` and links, nothing more.** Render the `DocumentType` as a chip and each non-null FK as
  a `→` chip that navigates to the linked record. The design's `→ policy` chip maps to the insurance document's
  place in Settings, not a fourth FK — there is no `PolicyId` on `Document`, and this spec does not add one.
- **`usePlate()` for the registration; never `plate={reg}`** — `coverage.test.ts` fails the build on a passed
  plate and requires every screen axe-swept or exempted with a reason. The upload sheet, the viewer, and both
  lists are new components and each must clear that guard.
- The viewer is *simple*: a PDF opens in an object/iframe, an image renders inline, and Download always returns
  the untouched original — the design promises the original survives, so no re-encoding on the way out.

## Verification

- `npm run gen:api` then `git diff --exit-code`: the multipart upload and the new response shapes change the
  OpenAPI contract, and the staleness gate must be green with the regenerated TS types committed.
- .NET tests run against real Postgres via Testcontainers applying migrations (not in-memory) — assert upload
  writes a file and a row, `Sha256` matches a known fixture, a duplicate is detected, a cross-vehicle link is a
  400, and DELETE removes both the row and the file. The volume root points at a temp directory under test.
- End to end on BT53: upload the MOT-pass certificate linked to its 8 Jul 2026 service record, confirm the chip
  and that View/Download work; upload a baseline photo and an issue-linked photo and confirm the grid.

## External Dependencies (Conditional)

None. `Document` and its configuration already exist; storage is a local volume already chosen (DEC-005); hash
and multipart handling are in the framework. This is wiring, not acquisition — no new package.
