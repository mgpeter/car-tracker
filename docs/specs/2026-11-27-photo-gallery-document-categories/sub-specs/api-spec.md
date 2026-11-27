# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-11-27-photo-gallery-document-categories/spec.md

## Endpoints

### GET /api/document-types

**Purpose:** The category table, so the client renders groups and labels without hand-typing them.
**Parameters:** None.
**Response:** `200` with `DocumentTypeInfo[]` in display order:

```json
[
  { "type": "V5C", "group": "Ownership", "kind": "Paper", "label": "V5C logbook" },
  { "type": "CarPhoto", "group": "Photos", "kind": "Photo", "label": "Car photo" }
]
```

**Errors:** None beyond authentication.

### POST /api/vehicles/{reg}/documents

**Purpose:** Upload, unchanged except for optional renditions.
**Parameters:** `multipart/form-data`. Existing: `file`, `title`, `type`, `documentDate`, `notes`,
`serviceRecordId` / `expenseEntryId` / `issueId`. New, all-or-none:

- `thumbnail` - `image/webp` or `image/jpeg`, at most 300 KB
- `display` - `image/webp` or `image/jpeg`, at most 3 MB
- `width`, `height` - positive integers, the decoded original's dimensions

**Response:** `201` with `DocumentItem`, as now.
**Errors:** Existing (unsupported type, over 25 MB, duplicate, allowance reached). New `400` when the rendition
parts are partial, of an unsupported type, or over their cap.

### POST /api/vehicles/{reg}/documents/{id}/renditions

**Purpose:** Browser backfill for documents filed without renditions.
**Parameters:** `multipart/form-data` with `thumbnail`, `display`, `width`, `height`, all required.
**Response:** `200` with the updated `DocumentItem`. If renditions already exist, nothing changes and the current
item is returned.
**Errors:** `400` on the same rendition rules, or when the document is not an image. `404` when it's not this
vehicle's document.

### GET /api/vehicles/{reg}/documents/{id}/file

**Purpose:** The bytes, now with a variant.
**Parameters:** `variant` (query): `original` (default), `display`, `thumbnail`.
**Response:** `200` with the bytes. Renditions carry `Cache-Control: private, max-age=31536000, immutable` and an
`ETag`.
**Errors:** `404` when the document or the requested variant doesn't exist.

### PATCH /api/vehicles/{reg}/documents/{id}

Unchanged in shape. `type` accepts the new members. Changing a cover photo's type to a paper category also
clears the vehicle's cover.

### PUT /api/vehicles/{reg}/cover-photo

**Purpose:** Choose or clear the car's cover photo.
**Parameters:** Body `{ "documentId": 12 }` or `{ "documentId": null }`.
**Response:** `204`.
**Errors:** `400` on `documentId` when the document is not this vehicle's or is not photo-kind. `404` for an
unknown vehicle.

## Contract changes

- `DocumentType`: `Photo` renamed `ConditionPhoto`; adds `BillOfSale`, `RoadTax`, `Breakdown`,
  `ServiceInvoice`, `PartsReceipt`, `Warranty`, `WorkshopManual`, `Diagram`, `CarPhoto`.
  **BREAKING CHANGE** for any client sending `"Photo"`. The web client is the only consumer today: no MCP or chat
  tool touches documents yet.
- New enums `DocumentGroup` and `DocumentKind`; new record `DocumentTypeInfo`.
- `DocumentItem` adds `kind`, `group`, `hasThumbnail`, `hasDisplay`, `width`, `height`.
- `VehicleDetail` and the garage card summary add `coverPhotoId`.
