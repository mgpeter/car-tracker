# Database Schema

This is the database schema implementation for the spec detailed in @docs/specs/2026-11-27-photo-gallery-document-categories/spec.md

## Migration `AddPhotoGalleryAndDocumentCategories`

One migration, hand-ordered, because the type rename has to happen between dropping the old check constraint and
adding the new one.

```sql
-- 1. Renditions and dimensions on documents
ALTER TABLE documents
    ADD COLUMN thumbnail_path varchar(400) NULL,
    ADD COLUMN display_path   varchar(400) NULL,
    ADD COLUMN width          integer      NULL,
    ADD COLUMN height         integer      NULL;

ALTER TABLE documents
    ADD CONSTRAINT ck_documents_dimensions
    CHECK ((width IS NULL AND height IS NULL) OR (width > 0 AND height > 0));

-- A display copy and a thumbnail are made together, so one without the other is a bug.
ALTER TABLE documents
    ADD CONSTRAINT ck_documents_renditions
    CHECK ((thumbnail_path IS NULL) = (display_path IS NULL));

-- 2. Categories: drop, rename, re-add
ALTER TABLE documents DROP CONSTRAINT ck_documents_type;

UPDATE documents SET type = 'ConditionPhoto' WHERE type = 'Photo';

ALTER TABLE documents
    ADD CONSTRAINT ck_documents_type CHECK (type IN (
        'V5C', 'BillOfSale',
        'MOT', 'Insurance', 'RoadTax', 'Breakdown',
        'ServiceInvoice', 'PartsReceipt', 'Warranty', 'Receipt',
        'Manual', 'WorkshopManual', 'Diagram',
        'CarPhoto', 'ConditionPhoto',
        'Other'));

-- 3. Cover photo
ALTER TABLE vehicles
    ADD COLUMN cover_photo_id integer NULL
    REFERENCES documents (id) ON DELETE SET NULL;

CREATE INDEX ix_vehicles_cover_photo_id ON vehicles (cover_photo_id);
```

## EF Core configuration

- `DocumentConfiguration`: the four new properties with explicit column types (`varchar(400)`, `integer`), the
  two new check constraints, and the replaced `ck_documents_type`. `type` stays `varchar(20)`; the longest new
  value (`ConditionPhoto`, `WorkshopManual`, `ServiceInvoice`) is 14 characters.
- `VehicleConfiguration`:
  `builder.HasOne<Document>().WithMany().HasForeignKey(v => v.CoverPhotoId).OnDelete(DeleteBehavior.SetNull)`
  and `HasColumnType("integer")`.

## Rationale

- **No new table.** Photos and papers share storage, hashing, the allowance, deletion and the host's backup
  index. A separate table would duplicate all five, and the host's `BLOB_INDEX_SQL` would need changing in another
  repository.
- **Kind and group are not columns.** They are derived from `type` on read through `DocumentCategories`, so they
  can't disagree with it (the project's central rule, applied to a lookup).
- **`ConditionPhoto` for every existing photo.** The current screen presents all unlinked photos as the
  condition baseline, and linked ones are issue evidence, so that is what they already mean.
- **The cover FK runs against the vehicle → documents cascade.** Deleting a vehicle cascades to its documents,
  which then set `cover_photo_id` to null on a row already being deleted. PostgreSQL resolves this, but EF Core's
  change tracker can report a circular dependency when both rows are tracked. `VehicleDeletionService` must be
  proved against it by a Data test, and must null the cover first if the test fails.
- **Renditions are paths, not a second table.** Exactly two per document, made together, never versioned.

## Down

Lossy, and says so in its own comment: `CarPhoto`/`ConditionPhoto` → `Photo`;
`BillOfSale`/`ServiceInvoice`/`PartsReceipt` → `Receipt`; `WorkshopManual`/`Diagram` → `Manual`;
`RoadTax`/`Breakdown`/`Warranty` → `Other`. It drops the four columns, the two constraints and
`cover_photo_id`, and restores the original `ck_documents_type`. Rendition files on disk are left in place.
