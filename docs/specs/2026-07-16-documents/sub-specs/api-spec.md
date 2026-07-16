# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-07-16-documents/spec.md

All routes sit behind the gateway on one origin and require `X-Api-Key` (DEC-009). Registration → id resolution
is the endpoint's job, via `VehicleLookup`, exactly as in the existing groups. This is the first group that
speaks `multipart/form-data` rather than JSON, because it moves bytes.

## Endpoints

### GET /api/vehicles/{registration}/documents

**Purpose:** The document metadata list — papers and photos both — newest first, for the screen's two sections
to split by `Type`. Metadata only; the bytes are a separate request, so listing a garage of files does not
stream them all.
**Parameters:** `registration` (path); `type` (query, optional) to filter to one `DocumentType`.
**Response:** `DocumentItem[]` — `{ Id, Type, Title, DocumentDate, ContentType, SizeBytes, Sha256, ServiceRecordId, ExpenseEntryId, IssueId, Notes, CreatedAt }`.
**Errors:** 404 when the registration is unknown.

### POST /api/vehicles/{registration}/documents

**Purpose:** Upload a file and file it. Streams the part to the configured volume root, stores the relative
path on `FilePath`, captures `ContentType` and `SizeBytes` from the received part, computes `Sha256` in the
same pass, and optionally links it to one record.
**Body:** `multipart/form-data` — a `file` part, plus form fields `Title`, `Type` (`DocumentType`),
`DocumentDate?`, `Notes?`, and **at most one** of `ServiceRecordId?` / `ExpenseEntryId?` / `IssueId?`.
**Response:** `201` with `DocumentItem`, `Location` pointing at the file route below.
**Errors:** 404 unknown registration; 400 on a disallowed content type, an oversize file, more than one link
set, or a link whose target is not this vehicle's.
**Notes:** `ContentType` and `SizeBytes` come from the received bytes, never from a client-supplied field — the
row must describe what was actually stored. A byte-identical re-upload is reported as a duplicate rather than
filed blind.

### GET /api/vehicles/{registration}/documents/{id}/file

**Purpose:** The bytes. Streams the stored file with its `ContentType`; `?download=true` sets
`Content-Disposition: attachment` so the browser saves the original untouched, otherwise `inline` for the
simple viewer.
**Parameters:** `id` (path); `download` (query, optional).
**Response:** `200` with the file stream and the stored content type.
**Errors:** 404 when the document, or its file on the volume, is missing.

### PATCH /api/vehicles/{registration}/documents/{id}

**Purpose:** Correct the metadata or change the link — retitle, retype, attach to a record or detach. The bytes
are immutable; a different file is a new upload, not a PATCH (there is no versioning — see the spec).
**Parameters:** `id` (path); `UpdateDocumentRequest` — `Title?`, `Type?`, `DocumentDate?`, `Notes?`, and the
three optional link FKs, still at most one non-null.
**Response:** `200` with the updated `DocumentItem`.
**Errors:** 404; 400 on more than one link, or a cross-vehicle link target.

### DELETE /api/vehicles/{registration}/documents/{id}

**Purpose:** Remove the row **and** its file from the volume. Nothing cascades into a document — the three FKs
point outward and are `SetNull` — so this is the only thing that frees the bytes, and it must, or the volume
accumulates orphans no row references.
**Response:** `204`.
**Errors:** 404.

## No change to existing responses

The three link targets are read from the document, not pushed onto the service record, expense or issue: those
rows do not learn they have a document attached, because the FK lives on `Document` and the screens navigate
*from* the paper *to* the record. If a "documents on this record" affordance is ever wanted on the other side,
it is a query over `Document`, not a new column there.
