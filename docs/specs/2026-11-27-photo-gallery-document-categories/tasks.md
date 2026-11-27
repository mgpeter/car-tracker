# Spec Tasks

These are the tasks to be completed for the spec detailed in
@docs/specs/2026-11-27-photo-gallery-document-categories/spec.md

> Server first, then the browser. Task 1 lands the categories and the schema in one migration, because the type
> rename has to happen between dropping and re-adding the check constraint. Tasks 2 and 3 put every rule
> (rendition sets, variants, cover photo, re-tag, deletion) in the domain and prove it against real PostgreSQL,
> because there is no `CarTracker.WebApi.Tests` project. Tasks 4 to 6 are the web app, each typed against the
> regenerated contract.
>
> `docs/specs/2026-11-27-assistant-photo-filing/` builds on this spec. After task 3 it can rely on
> `DocumentKind`, `DocumentTypeInfo` and `GET /api/document-types`. After task 4 it can rely on
> `uploadDocument(reg, file, meta)` in `api/documents.ts` (renditions included) and a pre-fillable
> `UploadSheet`, so neither has to be refactored when that spec runs (assumed: A13).
>
> UI tests are Vitest + Testing Library component tests with an axe sweep, the project's existing tool (assumed: A9).
> Writes are asserted on the request, not the DOM. jsdom has no canvas, image decoder or
> `IntersectionObserver`, so tests stub `createImageBitmap`, `HTMLCanvasElement.prototype.toBlob` and
> `IntersectionObserver` per file.

## Tasks

- [x] 1. **Grouped categories and the schema** (assumed: A1, A2, A15)
  - [x] 1.1 Write unit tests in `tests/CarTracker.Domain.Tests/DocumentCategoriesTests.cs`: every
        `Enum.GetValues<DocumentType>()` member has a group, a kind and a non-blank label; kind is `Photo`
        exactly for the Photos group (`CarPhoto`, `ConditionPhoto`); `DocumentCategories.All` lists every member
        once, in the technical spec's group order (Ownership, Legal & cover, Work & parts, Reference, Photos,
        Other) and member order; `(int)DocumentType.ConditionPhoto == 5` and the old members keep their numbers.
        Write integration tests (Testcontainers) in `tests/CarTracker.Data.Tests/PhotoGalleryMigrationTests.cs`,
        following `PerOwnerReferenceListBackfillTests`: migrate to the migration before this one (currently
        `AddTermsAcceptance`), seed `documents` rows with raw SQL as `'Photo'`, `'Receipt'` and `'V5C'`, migrate up,
        and assert the `'Photo'` row is now `'ConditionPhoto'` and the others are unchanged;
        `ck_documents_type` accepts every member's name and rejects `'Photo'`; `ck_documents_dimensions` rejects
        a width without a height and a zero width; `ck_documents_renditions` rejects a thumbnail path without a
        display path; deleting a document that is a vehicle's `cover_photo_id` nulls the column. Watch them fail
        for the missing types and migration, not for a fixture problem
  - [x] 1.2 Bump `VERSION` minor in this task's commit (`./scripts/release.ps1 -Minor` or by hand): 0.30.0 to
        0.31.0 if clear-vehicle-fields has already bumped to 0.30.0, otherwise the next minor from what the file
        holds
  - [x] 1.3 In `CarTracker.Shared`: rewrite `DocumentType` (`Photo` renamed `ConditionPhoto` and kept at 5; new
        members from 8: `BillOfSale`, `RoadTax`, `Breakdown`, `ServiceInvoice`, `PartsReceipt`, `Warranty`,
        `WorkshopManual`, `Diagram`, `CarPhoto`), and add `DocumentGroup` and `DocumentKind`. Add static
        `DocumentCategories` in `CarTracker.Domain/Documents` with `Group`, `Kind` and `Label` as exhaustive
        switches with no default, and `All` in display order (assumed: A11 for the labels, A19)
  - [x] 1.4 Add `ThumbnailPath`, `DisplayPath`, `Width`, `Height` to `Data/Document.cs` and `CoverPhotoId` to
        `Data/Vehicle.cs`. In `DocumentConfiguration`: `varchar(400)` and `integer` column types, the two new
        check constraints and the replaced `ck_documents_type`. In `VehicleConfiguration`:
        `HasOne<Document>().WithMany().HasForeignKey(v => v.CoverPhotoId).OnDelete(DeleteBehavior.SetNull)` with
        `HasColumnType("integer")` and the index `ix_vehicles_cover_photo_id` (assumed: A20)
  - [x] 1.5 Generate the migration `AddPhotoGalleryAndDocumentCategories`
        (`dotnet ef migrations add ... --project src/CarTracker.Data`), then hand-order `Up` as
        database-schema.md lays it out (columns and constraints, drop `ck_documents_type`, the `UPDATE`, re-add
        the constraint, `cover_photo_id` with its FK and index). Write the lossy `Down` with a comment saying it
        is lossy and listing the mapping. Check the model snapshot agrees with the configuration (assumed: A20)
  - [x] 1.6 Keep the build working (assumed: A15): switch `DocumentService.GetLogAsync`'s papers/photos split to
        `DocumentCategories.Kind`, and rename `DocumentType.Photo` to `ConditionPhoto` in `DocumentTests.cs` and
        `ChecksAndTasksTests.cs` (a mechanical rename; no assertion changes)
  - [x] 1.7 `dotnet build` to rewrite `api-contract/v1.json`, then `npm run gen:api`. Point `DocumentsPage.tsx`'s
        local `DocumentType` at `components['schemas']['DocumentType']`, update its type list, and change the
        `'Photo'` fixtures in `DocumentsPage.test.tsx` to `'ConditionPhoto'`. The commit body carries a
        `BREAKING CHANGE:` line for the `Photo` rename (assumed: A2)
  - [x] 1.8 Verify all tests pass (`dotnet test`; `NODE_OPTIONS=--no-webstorage npx vitest run`; `npx tsc -b`)

- [x] 2. **Renditions on the server** (assumed: A6, A7, A12)
  - [x] 2.1 Write integration tests (Testcontainers plus a real temp directory) in
        `tests/CarTracker.Data.Tests/DocumentRenditionTests.cs`, in the style of `DocumentTests`:
        `RenditionSet` refuses a thumbnail without a display copy, dimensions without renditions, renditions
        without dimensions, a zero or negative dimension, and a `image/png` or `image/heic` rendition, and
        accepts WebP and JPEG; `DocumentStore.SaveAsync` refuses a 301 KB thumbnail and a 3 MB + 1 byte display
        copy and leaves no file behind; `RecordAsync` with renditions stores three files and sets all four
        columns, and the item has `HasThumbnail`, `HasDisplay`, `Width`, `Height`, `Kind`, `Group` and
        `CreatedAt`; renditions do not change `CapacityAsync`'s count; `DeleteAsync` removes all three files,
        and a file another row still references in any of the three path columns survives;
        `AddRenditionsAsync` fills null columns, is a no-op returning the current item when they are set (files
        on disk unchanged), refuses a PDF and an oversize file, and never touches the original;
        `FindFileAsync` returns the original, display and thumbnail paths and content types, and not-found for a
        variant that does not exist; for each variant, a context pinned with `TestOwner.As(otherOwner)` gets
        not-found for this owner's document; `GetLogAsync` puts `CarPhoto` and `ConditionPhoto` in `Photos` and
        a JPEG `ServiceInvoice` in `Papers`; `ListRowsAsync` carries the four new columns (assumed: A12)
  - [x] 2.2 `DocumentStore`: a `maxBytes` parameter on `SaveAsync` (defaulting to `MaxSizeBytes`, still enforced
        while reading), plus `RenditionContentTypes` (WebP, JPEG), `MaxThumbnailBytes` (300 KB) and
        `MaxDisplayBytes` (3 MB)
  - [x] 2.3 Domain, `CarTracker.Domain/Documents`: a `RenditionSet` record and validator (all or none, types,
        positive dimensions); `RecordAsync` takes optional stored renditions and dimensions; new
        `AddRenditionsAsync(vehicleId, id, ...)` that refuses non-image originals and is a no-op when renditions
        exist; `DeleteAsync` deletes each of the three paths behind a still-referenced check widened to
        `FilePath`, `ThumbnailPath` and `DisplayPath`
  - [x] 2.4 Add `DocumentVariant` (`Original`, `Display`, `Thumbnail`) to `CarTracker.Shared`, and
        `DocumentService.FindFileAsync(vehicleId, id, variant)`, which reaches the document through
        `context.Vehicles.Any(v => v.Id == d.VehicleId)` so the owner filter applies (assumed: A6) and returns the path,
        content type and the file's hash for the `ETag`
  - [x] 2.5 `DocumentItem` gains `Kind`, `Group`, `HasThumbnail`, `HasDisplay`, `Width`, `Height` and
        `CreatedAt` (assumed: A7). Update the `DocumentLog` doc comments (photos are every photo-kind document, not
        "the March 2026 condition baseline"). `DocumentRowItem` gains the four new columns, appended with
        defaults (assumed: A12)
  - [x] 2.6 `DocumentEndpoints`: upload reads optional `thumbnail`, `display`, `width`, `height` parts and
        validates the set before streaming any bytes, then saves each with its cap (400 on a bad set or an
        oversize rendition); new `POST /{id}/renditions`; `GET /{id}/file` takes `variant` (default
        `original`), answers 404 for an absent variant, and sends
        `Cache-Control: private, max-age=31536000, immutable` and the `ETag` on renditions only. The original
        keeps today's headers and `?download=true`
  - [x] 2.7 `dotnet build` and `npm run gen:api`; confirm the contract diff adds fields and one endpoint and
        removes nothing. By hand against the AppHost (assumed: A6): an upload with a thumbnail and no display copy
        answers 400; `variant=thumbnail` returns the cache headers and an `ETag`; `variant=display` on a PDF
        answers 404 (assumed: A21, A22)
  - [x] 2.8 Verify all tests pass (`dotnet test`; `NODE_OPTIONS=--no-webstorage npx vitest run`; `npx tsc -b`)

- [x] 3. **Cover photo and the category endpoint** (assumed: A5, A6, A8)
  - [x] 3.1 Write integration tests (Testcontainers) in `tests/CarTracker.Data.Tests/CoverPhotoTests.cs`:
        `SetCoverAsync` sets a `CarPhoto` and a `ConditionPhoto`; refuses a paper-kind document and another
        vehicle's document with `Validation` keyed `documentId`; refuses another owner's document under
        `TestOwner.As`; clears with `null`; deleting the cover document nulls `CoverPhotoId` after a reload;
        re-tagging the cover to `ServiceInvoice` through `UpdateAsync` clears it, and re-tagging it to
        `ConditionPhoto` keeps it. Add to `VehicleDeletionTests` and `AccountDeletionTests`: deleting a vehicle
        (and an account) whose cover is set succeeds with the vehicle tracked (assumed: A5). Add to `AccountExportTests`
        that the profile carries `coverPhotoId`, and to `AccountImportTests` that an imported profile naming a
        cover id is written with `cover_photo_id` null. Add to `tests/CarTracker.Domain.Tests/DerivedMetricsServiceTests.cs`
        that the garage card carries the vehicle's `CoverPhotoId`. Add type assertions to
        `src/CarTracker.WebApp/src/api/contract.test.ts`: `DocumentType` has no `'Photo'` and includes
        `'CarPhoto'`; `DocumentTypeInfo` has `type`, `group`, `kind`, `label`; `DocumentItem` has `kind` and
        `hasThumbnail`; `GarageItem` and `VehicleDetail` have `coverPhotoId`
  - [x] 3.2 Domain: `DocumentService.SetCoverAsync(vehicleId, documentId?)` returning a `WriteResult`, with the
        document required to be this vehicle's and photo-kind
  - [x] 3.3 `DocumentService.UpdateAsync`: when the new type is paper-kind and the document is its vehicle's
        cover, clear `CoverPhotoId` in the same save
  - [x] 3.4 Run the deletion tests from 3.1. If either service throws a circular-dependency error, null
        `CoverPhotoId` and save inside its existing execution-strategy transaction before removing vehicles
        (assumed: A5). If both pass, leave the services alone and keep the tests as guards
  - [x] 3.5 `ImportWriter.WriteProfileAsync`: set `CoverPhotoId = null` beside the four fields it already
        overwrites, with a comment that document rows are not imported, so the id would point at nothing
  - [x] 3.6 Contract and endpoints: `DocumentTypeInfo(Type, Group, Kind, Label)` in `CarTracker.Shared`;
        `GET /api/document-types` serving `DocumentCategories.All`; `PUT /api/vehicles/{registration}/cover-photo`
        with `{ documentId }` answering 204, 400 keyed `documentId`, or 404; `CoverPhotoId` on `GarageItem`
        (from `DerivedMetricsService.GetGarageAsync`) and on `VehicleDetail`
  - [x] 3.7 `dotnet build` and `npm run gen:api`; confirm the contract diff is additive
  - [x] 3.8 Verify all tests pass (`dotnet test`; `NODE_OPTIONS=--no-webstorage npx vitest run`; `npx tsc -b`)

- [x] 4. **Browser renditions, the shared documents API and the Documents screen** (assumed: A3, A11, A13, A14, A16)
  - [x] 4.1 Write unit and component tests. `src/lib/renditions.test.ts`: a 4032x3024 image gives a thumbnail
        with a 480 px long edge and a display copy with a 2048 px long edge, both WebP, plus the bitmap's width
        and height; when `toBlob` returns `image/png` for WebP, the rendition is re-encoded as JPEG; an image
        `createImageBitmap` rejects (HEIC in Chrome) gives no renditions; a PDF gives none; the returned blobs
        are always the `toBlob` results and never the original file, even for a small JPEG (assumed: A3).
        `src/api/documents.test.ts`: `uploadDocument` posts multipart with `file`, `title`, `type`, the link id
        and the four rendition parts for an image, none for a PDF or an undecodable image, and still uploads the
        undecodable one; the backfill posts `/renditions` once per document per session, never for a PDF, and
        runs at most two at a time (assumed: A14). `DocumentsPage.test.tsx`: papers only (a `CarPhoto` row in the log does
        not appear); the upload sheet's category select renders one `<optgroup>` per group from a mocked
        `/api/document-types`, and a group the client does not know still renders, under its raw name; search
        matches title, notes, category label and link label; the group filter narrows rows; a link to Photos
        replaces the photo grid; the link chip's `href` comes from `hrefFor` (assumed: A16); the axe sweep passes. The
        existing `ChatPanel.test.tsx` must stay green through 4.2
  - [x] 4.2 Extract the decode-and-scale step from `lib/attachments.ts` into a shared helper both files use. The
        chat's behaviour and its JPEG output do not change
  - [x] 4.3 New `lib/renditions.ts`: `makeRenditions(file)` returns `{ thumbnail, display, width, height }` or
        null, with the long edges, qualities and caps from the technical spec and the PNG-to-JPEG fallback
  - [x] 4.4 New `api/documents.ts`, typed off the generated contract: `useDocuments(reg)` (one cached query,
        key `['vehicle', reg, 'documents']`), `useDocumentTypes()`, `GROUP_LABEL: Record<DocumentGroup, string>`
        (assumed: A11), `fileUrl(reg, id, variant)`, `useDocumentObjectUrl(reg, id, variant)` moved from the screen,
        `uploadDocument(reg, file, meta)` with no React dependency (assumed: A13), `postRenditions`, and
        `useRenditionBackfill` with an in-memory attempted set (assumed: A14)
  - [x] 4.5 Move the upload sheet to `screens/documents/UploadSheet.tsx`, with an `initial` prefill (type, title,
        date, link, files) and a preset link shown as a read-only chip (assumed: A13). The category select uses one
        `<optgroup>` per group from `/api/document-types` and defaults to the first paper category, or to
        `initial.type`. Upload goes through `uploadDocument`. Use it in `RetagSheet`'s grouped select too
  - [x] 4.6 Rewrite `DocumentsPage.tsx` for papers only: `useDocuments`, `<DataTable>` through `useTableView`
        with search over title, notes, category label and link label and a group filter; columns Category
        (group and label), Title, Date, Linked to, Size, actions; a one-line link to Photos where the grid was;
        the link chip through `hrefFor` (assumed: A16); footer and eyebrow copy that no longer mention photo sets
  - [x] 4.7 Verify all tests pass (`NODE_OPTIONS=--no-webstorage npx vitest run`; `npx tsc -b`; `dotnet test`)

- [x] 5. **Photos screen, viewer and cover on the garage card** (assumed: A7, A8, A9, A14, A17, A18, A21, A23, A24)
  - [x] 5.1 Write component tests. `screens/photos/PhotosPage.test.tsx`: only photo-kind documents show; the
        All / Car photos / Condition filter; tiles grouped by month of `documentDate`, else of `createdAt`
        marked "uploaded" (assumed: A7), newest month first; a tile requests `variant=thumbnail` only when
        `hasThumbnail` and only once its `IntersectionObserver` entry intersects; an image with no thumbnail
        that this browser cannot decode shows "No preview in this browser" with Save still offered; an image
        with no thumbnail that it can decode triggers one backfill POST; multi-upload of three files posts them
        one after another with a status row each, defaults the category to `CarPhoto`, each date to the file's
        `lastModified` day and each title to the file name without its extension; a duplicate refusal on file 2
        is reported against it and file 3 still posts; an allowance refusal on file 2 stops file 3 and says one
        was filed. `screens/photos/PhotoViewer.test.tsx`: opening a tile shows its display copy (falling back to
        the original for a decodable type, and to the placeholder otherwise); ArrowRight and ArrowLeft step in
        date order; a horizontal pointer swipe steps; Esc closes and focus returns to the opening tile; both
        neighbours' display copies are requested; the caption shows title, date, category label and link chip;
        Save original requests `variant=original`; Set as cover sends `PUT .../cover-photo` with
        `{ documentId }`; Delete goes through `ConfirmButton` and sends `DELETE`; Re-tag opens the re-tag sheet;
        no element carries a `style` attribute. `GaragePage.test.tsx`: a card with `coverPhotoId` fetches
        `variant=thumbnail` through `apiBlob` and renders an object URL, never an `<img src>` pointing at
        `/api`; a null cover requests nothing; a 404 renders nothing (assumed: A8). `routes.gating.test.tsx` and
        `shell.test.tsx` pass with `/:reg/photos` inside the gated branch and Photos in the nav. Axe sweeps for
        the page and for the open viewer
  - [x] 5.2 Add `'photos'` to `ScreenId` and `SCREENS` beside Documents (assumed: A17), to `BUILT` in `routes.tsx`, and a
        `photos` route under `:reg`
  - [x] 5.3 Multi-file mode for `UploadSheet`: one shared category and date, a title per file, sequential upload
        through `uploadDocument` with a status row per file, continuing past a duplicate and stopping on the
        allowance refusal with a count of what was filed (assumed: A24)
  - [x] 5.4 New `screens/photos/PhotosPage.tsx`: `useDocuments`, the `Seg` filter, month groups (assumed: A7), lazy
        thumbnail tiles through `useDocumentObjectUrl` that revoke their object URLs on unmount, the
        "No preview in this browser" placeholder, `useRenditionBackfill`, and an Upload centre action
  - [x] 5.5 New `screens/photos/PhotoViewer.tsx` (assumed: A18): full-screen dialog on `useFocusTrap`,
        `useInertBackground` and `useScrollLock`; keyboard, pointer swipe, neighbour preload, caption, and the
        actions Save original, Re-tag, Set as cover and Delete; a two-pane compare mode with each pane dated,
        used in 6.4. Styles in `components.css`, sized by `img` `width`/`height` attributes, no inline styles
  - [x] 5.6 Cover: the viewer's Set as cover calls `PUT .../cover-photo` and invalidates the garage and vehicle
        queries; `VehicleCard` shows the cover thumbnail through `apiBlob` when `coverPhotoId` is set and nothing
        on a failure (assumed: A8)
  - [x] 5.7 Verify all tests pass (`NODE_OPTIONS=--no-webstorage npx vitest run`; `npx tsc -b`; `dotnet test`)

- [x] 6. **Attachments on records, the issue timeline and docs** (assumed: A3, A4, A9, A10, A22)
  - [x] 6.1 Write component tests. `screens/documents/AttachmentStrip.test.tsx`: given a log with documents linked
        to several records, the strip for `{ kind: 'ServiceRecord', id: 7 }` shows only record 7's papers and
        photos; three strips on one screen make one `/documents` request; a photo thumbnail opens the viewer
        over that record's photos only; a paper chip offers View and Save; Attach opens the upload sheet with the
        link preset, and the POST carries `serviceRecordId` (and `expenseEntryId`, `issueId` for the other
        kinds). `ServiceHistoryPage.test.tsx` and `ExpensesPage.test.tsx`: the strip appears when editing an
        existing record and not on a new one (assumed: A10). `phase3.test.tsx` (Issues): an issue with three linked photos
        lists them in date order; Compare opens the viewer with the earliest on the left and the latest on the
        right, each dated; Compare is absent with fewer than two photos. `legal.test.tsx`: the privacy page
        renders the originals-and-previews sentence (assumed: A4). Axe sweeps for each changed screen, and an entry in
        `test/coverage.test.ts` only where a component is swept with its page
  - [x] 6.2 New `screens/documents/AttachmentStrip.tsx`: filters `useDocuments` client-side by link kind and id;
        photo thumbnails, paper chips with View and Save, and an Attach button opening `UploadSheet` with
        `initial.link`; runs `useRenditionBackfill` for its photos
  - [x] 6.3 Place the strip in `AddServiceSheet` and `AddExpenseSheet` for existing records, and in each issue's
        list item on `IssuesPage` (assumed: A10)
  - [x] 6.4 Issue timeline: the issue's photos in date order inside its list item, with a Compare action opening
        `PhotoViewer` in compare mode (earliest left, latest right) when there are two or more
  - [x] 6.5 Privacy: add the sentence to `PrivacyPage.tsx` under "What is collected" and move
        `LegalVersion.Current` to the ship date (assumed: A4)
  - [x] 6.6 Docs: an entry for this spec in `docs/product/roadmap.md`; renditions in `asset_hosting` in
        `docs/product/tech-stack.md`; in `docs/deployment-shared-host.md`, that renditions live under
        `DATA_ROOT/documents`, are in every snapshot, are not verified by the restore drill and are rebuilt by
        the browser backfill, with no host change; one `docs/engineering-log.md` entry. Check that
        `git grep -nP '\x{2014}' -- '*.md'` stays empty
  - [x] 6.7 By hand against the AppHost (assumed: A3, A9): an HEIC uploaded from Safari on an iPhone shows as a thumbnail in
        Chrome on a desktop and opens in the viewer, while Save returns the original with the stored SHA-256;
        `exiftool` on that photo's thumbnail and display copy shows no GPS tags; an HEIC uploaded from Chrome
        shows the placeholder, then gets renditions the first time it is opened in Safari; the issue Compare view
        lays out side by side at desktop and phone widths (assumed: A22; not run by hand in the batch, needs a signed-in browser and an iPhone; covered only by the compare and renditions component tests)
  - [x] 6.8 Verify all tests pass: the full suite once (`dotnet test`;
        `NODE_OPTIONS=--no-webstorage npx vitest run`; `npx tsc -b`)

## Follow-ups from batch review

- [ ] 7. **Rework A2: accept "Photo" as an alias for ConditionPhoto**
  - [ ] 7.1 Write integration tests (Data.Tests, real PostgreSQL): an upload whose form `type` is `Photo` is
        stored as `ConditionPhoto`; a document PATCH with `"type": "Photo"` stores `ConditionPhoto`; an unknown
        name still gets a 400
  - [ ] 7.2 Map `"Photo"` to `ConditionPhoto` in `UploadDocumentAsync` and accept it in the PATCH JSON (a
        converter or a mapping at the binding step); keep `ConditionPhoto` the only name in
        `api-contract/v1.json` and the generated client, and note the alias in the contract description
  - [ ] 7.3 Drop the `BREAKING CHANGE` wording where docs mention the rename; record the alias in
        `docs/engineering-log.md`
  - [ ] 7.4 Verify all tests pass (`dotnet test`; `NODE_OPTIONS=--no-webstorage npx vitest run`; `npx tsc -b`)
