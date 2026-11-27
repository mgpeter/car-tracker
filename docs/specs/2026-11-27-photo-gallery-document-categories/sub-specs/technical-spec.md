# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-11-27-photo-gallery-document-categories/spec.md

## Shape

Photos stay rows in `documents`. Papers and photos differ only by category, and a category's kind (paper or
photo) is derived on read from a server-owned table, never stored. One upload path, one hash and duplicate rule,
one plan allowance, one delete path, and the shared host's backup contract (`BLOB_INDEX_SQL` reads
`documents.file_path`) is untouched.

## Categories

`DocumentType` in `CarTracker.Shared/DocumentType.cs` becomes:

| Group | Members |
|---|---|
| Ownership | `V5C`, `BillOfSale` |
| Legal & cover | `MOT`, `Insurance`, `RoadTax`, `Breakdown` |
| Work & parts | `ServiceInvoice`, `PartsReceipt`, `Warranty`, `Receipt` |
| Reference | `Manual`, `WorkshopManual`, `Diagram` |
| Photos | `CarPhoto`, `ConditionPhoto` |
| Other | `Other` |

- `Photo` is renamed `ConditionPhoto` and keeps its numeric value (5). The rest keep theirs; new members take
  8 onwards. Stored values are strings, so only the rename touches data.
- Existing `Photo` rows become `ConditionPhoto`, because the current screen already presents every unlinked
  photo as the condition baseline. The owner can re-tag any of them as `CarPhoto`.
- `Receipt` stays as the general receipt. `Manual` stays as the owner's handbook.
- New enums in `CarTracker.Shared`: `DocumentGroup` (`Ownership`, `LegalAndCover`, `WorkAndParts`, `Reference`,
  `Photos`, `Other`) and `DocumentKind` (`Paper`, `Photo`).
- New static `DocumentCategories` in `CarTracker.Domain/Documents`: `Group(DocumentType)`, `Kind(DocumentType)`
  (Photo for the Photos group, Paper otherwise) and `Label(DocumentType)` ("Service invoice", "Workshop
  manual"). An exhaustive `switch` with no default, and a test enumerating `Enum.GetValues<DocumentType>()`.
- `GET /api/document-types` serves the table in display order. The client renders groups and labels from it and
  types `type` against the generated contract enum, never `Record<string, ...>`.

## Renditions, made in the browser

- New `lib/renditions.ts`, sharing a decode-and-scale helper extracted from `lib/attachments.ts` (the chat's
  canvas path). It runs for every image file at upload:
  - **Thumbnail:** long edge 480 px, WebP at 0.8, at most 300 KB.
  - **Display copy:** long edge 2048 px, WebP at 0.85, at most 3 MB.
  - `canvas.toBlob('image/webp')` silently returns PNG where WebP encoding is unsupported. Check `blob.type` and
    fall back to JPEG.
  - Also reports the decoded `width` and `height` (after EXIF orientation, which `createImageBitmap` applies).
- Re-encoding through a canvas drops EXIF, so neither rendition carries GPS or camera data. The original is
  untouched.
- **When the browser cannot decode the file** (HEIC in Chrome, Firefox and Edge), the upload goes ahead without
  renditions. The tile shows a "No preview in this browser" placeholder with Save still available.
- **Backfill:** when the Photos screen or an attachment strip shows an image document with no thumbnail, and this
  browser can decode the original, it fetches the original, makes both renditions and posts them to
  `POST .../documents/{id}/renditions`. At most two at a time, once per document per session. This fills existing
  photos and HEIC uploads from desktop the first time someone opens them in Safari.
- PDFs get no renditions. Papers that are images (a photographed receipt) do get them, because kind follows the
  category, not the content type.

## Server

- `Document` gains `ThumbnailPath`, `DisplayPath`, `Width`, `Height` (all nullable, see database-schema.md).
- `DocumentStore` saves renditions with the same content-addressed scheme under the vehicle's folder. Renditions
  accept only `image/webp` and `image/jpeg`, with caps of 300 KB (thumbnail) and 3 MB (display), enforced while
  reading as the original's cap is.
- Upload (`POST .../documents`) takes optional `thumbnail`, `display`, `width` and `height` parts. All of them or
  none: a thumbnail without a display copy, or dimensions without renditions, is a 400.
- `POST .../documents/{id}/renditions` fills renditions only where the columns are null, and is a no-op
  (`200`) when they are set. It does not touch the original.
- `GET .../documents/{id}/file?variant=original|display|thumbnail`, defaulting to `original`. An absent variant
  is a `404`. The client knows from the DTO which exist and never asks for a missing one.
- Rendition responses send `Cache-Control: private, max-age=31536000, immutable` and an `ETag` of the file's
  hash, so repeat views come from the browser cache. The original keeps today's headers.
- Delete removes all three files, each guarded by the existing "still referenced" check, widened to look at all
  three path columns.
- Renditions do not count toward the plan's document allowance, which counts rows.
- `DocumentItem` gains `Kind`, `Group`, `HasThumbnail`, `HasDisplay`, `Width`, `Height`. `DocumentLog.Photos` is
  now every photo-kind document and `Papers` every paper-kind one.
- Re-tagging a photo to a paper category clears it as the cover if it was one.

## Cover photo

- `Vehicle.CoverPhotoId` (nullable FK to `documents`, `ON DELETE SET NULL`).
- `PUT /api/vehicles/{reg}/cover-photo` with `{ "documentId": 12 }` or `{ "documentId": null }`. The document
  must belong to this vehicle and be photo-kind, or it's a 400 on `documentId`.
- `VehicleDetail` and the garage card's summary gain `coverPhotoId`. The garage card shows the cover's thumbnail
  through `apiBlob()` (never `<img src>` to the API), and nothing when unset.
- Export includes the column. **Import must write `cover_photo_id` as null**, because document rows are not
  imported and the id would point at nothing.

## Photos screen

- New `ScreenId` `'photos'`, label "Photos", route `/:reg/photos`, in the nav table beside Documents. Not named
  `gallery`: `/gallery` is already the design-system component gallery.
- Filter (`Seg`): All, Car photos, Condition. Tiles are grouped by month of `DocumentDate`, falling back to the
  upload date, with "Not dated" last.
- Tiles load thumbnails lazily (`IntersectionObserver`) through `apiBlob()`, and revoke object URLs on unmount as
  `useDocumentObjectUrl` does today.
- **Viewer:** a full-screen dialog built on the existing `Sheet` / dialog primitive, hand-rolled (no gallery
  library, strict CSP). It shows the display copy, falling back to the original for browser-decodable types and a
  placeholder otherwise. Left and right arrows, swipe (pointer events), Esc to close, focus returns to the tile.
  The neighbours' display copies are preloaded. The caption shows title, date, category and link chip. Actions:
  Save original, Re-tag, Set as cover, Delete (`ConfirmButton`).
- **Upload:** multiple files in one sheet, with a shared category (default `CarPhoto` here) and date (default
  each file's `lastModified` day, editable), and a title per file defaulting to its file name without the
  extension. Files upload one after another with a status row each. A duplicate is reported for that file and the
  rest continue. A plan-allowance refusal stops the rest and says how many were filed.
- Limitation: the default date is the file's modified date, not the EXIF capture date. On some phones that is the
  export time. Reading EXIF dates needs a parser this spec does not add.

## Documents screen

- Papers only. Goes through `useTableView` with search over title, notes, category label and link label, plus a
  group filter. Columns: Category (group and label), Title, Date, Linked to, Size, actions.
- The upload sheet's category select uses `<optgroup>` per group from `/api/document-types`, defaulting to the
  first paper category. Choosing a photo category from here is allowed and the file lands on Photos.
- The photo grid moves to the Photos screen. A one-line link from Documents to Photos replaces it.

## Attachments on records

- Extract the document-log query into `api/documents.ts` (`useDocuments(reg)`) so every screen shares one cached
  fetch. Attachments are filtered client-side by link id, with no request per record.
- New `<AttachmentStrip link={{ kind, id }} />`: photo thumbnails (opening the viewer over just that record's
  photos) and paper chips (View and Save), plus an Attach button that opens the upload sheet with the link preset.
- Placed where each record's detail already lives: the service record and expense edit sheets, and the issue
  card on Issues.
- **Issue timeline:** the issue's photos in date order. With two or more there is a Compare action that opens
  the viewer side by side, earliest on the left and latest on the right, each pane dated.

## Privacy and docs

- The privacy document gains one sentence, shown when documents are enabled: uploaded originals are kept as
  provided, including any location the camera recorded, and the app's previews have that removed.
- `docs/product/roadmap.md`: a new entry for this spec. `tech-stack.md`: note renditions in `asset_hosting`.
- `docs/deployment-shared-host.md`: renditions live under `DATA_ROOT/documents` and are in every snapshot. The
  restore drill verifies originals only, which is enough because renditions can be rebuilt by the browser
  backfill. No host change is needed.
- Minor `VERSION` bump.

## Tests

Data tests against real PostgreSQL, pinned with `TestOwner.As(ownerId)`:

- The migration maps `Photo` to `ConditionPhoto`, and the new check constraint accepts every member and rejects
  `'Photo'`.
- Upload with renditions stores three files, and delete removes all three. A rendition still referenced by
  another row survives.
- Rendition backfill fills null columns, is a no-op when set, and refuses a non-image content type and an
  oversize file.
- Cover photo: set, refuse a paper or another vehicle's document, null on document delete, cleared on re-tag to a
  paper, and the vehicle can still be deleted with a cover set (the FK runs both ways; prove no cycle error).
- Import writes `cover_photo_id` as null.
- A two-owner isolation test on the file endpoint for each variant.

Domain test: `DocumentCategories` covers every `DocumentType` member.

Front-end tests, asserting requests rather than the DOM where a write is involved:

- An image upload sends `thumbnail`, `display`, `width` and `height` parts. A PDF sends none. An undecodable image
  sends none and still uploads.
- The WebP-to-JPEG fallback when `toBlob` returns PNG.
- Backfill posts renditions once per document and not for PDFs.
- The viewer's arrow keys, Esc, focus return, and a placeholder for an undecodable original with no display copy.
- Multi-upload continues past a duplicate and stops on the allowance refusal.
- `AttachmentStrip` shows only the record's own attachments, and Attach presets the link.
- The category select renders every group from the endpoint, and an unknown group is not dropped silently.
- `routes.gating.test.tsx` passes with `/:reg/photos` inside the gated branch.
