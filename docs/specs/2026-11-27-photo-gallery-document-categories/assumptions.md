# Assumptions

## A1 · accepted · high
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 1.5
- **Question:** The migration rewrites every stored `Photo` row to `ConditionPhoto` across all accounts, and its Down maps nine new categories back onto four old ones, which loses information. Should it run like that?
- **Assumed:** Yes, as database-schema.md says. One migration, `AddPhotoGalleryAndDocumentCategories`, hand-ordered: drop `ck_documents_type`, `UPDATE documents SET type = 'ConditionPhoto' WHERE type = 'Photo'`, add the new constraint. Down is lossy and says so in a comment. No per-owner gating and no precondition guard (unlike `AddPerOwnerReferenceLists`), because the rename keeps every row and changes only its label. A Data test seeds rows through the schema before this migration and then migrates up.
- **Why:** The current screen already presents every unlinked photo as the condition baseline and every linked one as issue evidence, so `ConditionPhoto` describes what each row already means. An owner can re-tag any of them as `CarPhoto`.
- **Touches:** src/CarTracker.Data/Migrations/*_AddPhotoGalleryAndDocumentCategories.cs, tests/CarTracker.Data.Tests/PhotoGalleryMigrationTests.cs
- **If wrong:** If existing photos should become `CarPhoto`, or unlinked ones `CarPhoto` and linked ones `ConditionPhoto`, change the one `UPDATE` (and its test) before the migration ships (small). After it ships, write a follow-up data migration instead (medium).

## A2 · overridden · high
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 1.3, 1.7
- **Question:** Renaming `DocumentType.Photo` to `ConditionPhoto` breaks the public contract: a client that still sends `"Photo"` gets a 400. Should the server accept `"Photo"` as an alias for a while?
- **Assumed:** No alias. The contract changes as api-spec.md says, and the commit body carries a `BREAKING CHANGE:` line. The web client in the same build is the only consumer: no MCP tool, chat tool or import path writes a `DocumentType` (import skips document rows). A browser tab left open across the deploy gets one 400 on upload and works after a reload.
- **Why:** An alias needs a custom binder for one enum and a removal date nobody tracks. Nothing outside this repository sends the value.
- **Touches:** src/CarTracker.Shared/DocumentType.cs, api-contract/v1.json, src/CarTracker.WebApp/src/api/generated/schema.d.ts
- **If wrong:** Add a form-value mapping of `"Photo"` to `ConditionPhoto` in `UploadDocumentAsync` and a `JsonConverter` for the PATCH (small).
- **Answer:** Accept "Photo" as an alias for ConditionPhoto: map the form value in UploadDocumentAsync and accept it in the document PATCH JSON. The contract and generated client keep ConditionPhoto as the only documented name (2026-11-27 batch review).

## A3 · accepted · high
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 4.1, 4.3, 5.5, 6.7
- **Question:** The spec says the previews strip camera metadata, including GPS, because canvas re-encoding drops EXIF. The originals keep it. jsdom has no real canvas, so no automated test can show that GPS is gone from a rendition. How is the privacy claim held, and what does the viewer show when there is no display copy?
- **Assumed:** Three things. (1) A structural unit test: `makeRenditions` only ever returns blobs produced by `canvas.toBlob`, never the original `File` or a slice of it, even when the original is already a small JPEG or WebP. (2) A manual check in 6.7: run `exiftool` on a thumbnail and a display copy fetched from the running app for a geotagged phone photo, and confirm no GPS tags, while the "Save original" download still has them and matches the stored SHA-256. (3) The viewer falls back to the original only when no display copy exists and the browser can decode it. That shows pixels on screen, but the original bytes, with their metadata, sit in a blob in the owner's own browser. Nothing is sent anywhere new. The privacy sentence (A4) states that originals are kept as provided.
- **Why:** The spec forbids server-side image processing and rewriting the original, so the browser canvas is the only stripping step. A test that pins that renditions come from the canvas is the closest automated guard.
- **Touches:** src/CarTracker.WebApp/src/lib/renditions.ts, src/CarTracker.WebApp/src/lib/renditions.test.ts, src/CarTracker.WebApp/src/screens/photos/PhotoViewer.tsx
- **If wrong:** To keep originals out of the viewer entirely, show the "No preview" placeholder until the backfill has made a display copy (one branch in the viewer, small). To strip GPS from originals as well, that contradicts the spec's evidence and hash rule and needs a new spec (large).

## A4 · accepted · high
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 6.5
- **Question:** technical-spec.md says the privacy document gains one sentence "shown when documents are enabled". The public privacy page is read without signing in, and no deployment-level "documents enabled" flag exists: the document allowance is per plan (100 on Free, 2,000 on Pro), served only on authenticated meta. Changing the privacy wording also raises whether `LegalVersion.Current` moves.
- **Assumed:** Show the sentence unconditionally, in "What is collected", after the line that mentions photographs and documents: uploaded originals are kept exactly as provided, including any location the camera recorded, and the previews the app makes have that removed. Move `LegalVersion.Current` to the ship date, because the change is about what personal data is held, which is the kind of change that constant's remarks say to move it for.
- **Why:** Every plan can upload documents, so the condition is always true. A material privacy change should be versioned so `users.terms_version` says which text an account saw.
- **Touches:** src/CarTracker.WebApp/src/legal/PrivacyPage.tsx, src/CarTracker.Domain/Legal/LegalVersion.cs, src/CarTracker.WebApp/src/legal/legal.test.tsx
- **If wrong:** Leave `LegalVersion` as it is (one line), or gate the sentence on a new public meta flag (small, plus a contract change).

## A5 · accepted · high
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 3.1, 3.4
- **Question:** `vehicles.cover_photo_id` references `documents` with `ON DELETE SET NULL`, while `documents.vehicle_id` cascades from `vehicles`. database-schema.md names only `VehicleDeletionService` as at risk of an EF circular-dependency error. `AccountDeletionService` removes vehicles the same way (materialised, `RemoveRange`). Which deletion paths need the guard?
- **Assumed:** Both. 3.1 adds a Data test for each: delete a vehicle with a cover set, and delete an account whose vehicle has a cover set, each with the vehicle tracked. If either throws, that service sets `CoverPhotoId = null` and saves inside its existing execution-strategy transaction before removing the vehicles. If both pass, no code changes and the tests stay as guards.
- **Why:** Both services remove the same tracked entity, so the same cycle can appear in either. Deleting data is the operation where a silent 500 does the most damage.
- **Touches:** src/CarTracker.Domain/Vehicles/VehicleDeletionService.cs, src/CarTracker.Domain/Accounts/AccountDeletionService.cs, tests/CarTracker.Data.Tests/VehicleDeletionTests.cs, tests/CarTracker.Data.Tests/AccountDeletionTests.cs
- **If wrong:** Drop the account-deletion test (small).

## A6 · accepted · medium
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 2.1, 2.3, 2.4, 2.6, 3.2
- **Question:** The spec asks for tests of the rendition upload rules, the file variants, a two-owner isolation test "on the file endpoint for each variant" and the cover-photo rules. There is no `CarTracker.WebApi.Tests` project. Also, `documents` has no owner query filter: `DocumentService.FindAsync(vehicleId, id)` finds another owner's document if handed their vehicle id, and isolation today depends on the endpoint resolving the registration through the filtered `Vehicles` first.
- **Assumed:** No new test project. The rules live in the domain, where Data tests reach them: a `RenditionSet` validator (all-or-none, content types, positive dimensions), the caps in `DocumentStore`, `DocumentService.AddRenditionsAsync`, `DocumentService.SetCoverAsync`, and a new `DocumentService.FindFileAsync(vehicleId, id, variant)` that reaches the document through `context.Vehicles.Any(v => v.Id == d.VehicleId)`, the same correlated shape `CapacityAsync` uses. The isolation test runs that read for each variant under `TestOwner.As(otherOwner)` and expects not-found. Endpoints only map results to status codes and headers. The headers, the 400 for a partial multipart set and the 404 for an absent variant are checked by hand against the AppHost in 2.7.
- **Why:** This follows CLAUDE.md ("rules worth testing live in the domain") and the decision clear-vehicle-fields logged as its A4. Putting the owner check in the domain read makes the isolation test real rather than a false green.
- **Touches:** src/CarTracker.Domain/Documents/DocumentService.cs, src/CarTracker.Domain/Documents/DocumentStore.cs, src/CarTracker.Domain/Documents/RenditionSet.cs, src/CarTracker.WebApi/Endpoints/DocumentEndpoints.cs, tests/CarTracker.Data.Tests/DocumentRenditionTests.cs
- **If wrong:** Add a `WebApplicationFactory` test project over Testcontainers for the endpoint-level cases (medium).

## A7 · accepted · medium
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 2.5, 5.4
- **Question:** The Photos screen groups tiles "by month of `DocumentDate`, falling back to the upload date, with 'Not dated' last". `DocumentItem` carries no upload date, and if the fallback always exists, "Not dated" can never happen.
- **Assumed:** `DocumentItem` gains `CreatedAt` (the audit instant, already on every row). Tiles group by `DocumentDate`, else by the London calendar month of `CreatedAt`, and a tile dated that way says "uploaded" in its caption. No "Not dated" group is built, because every row has a `CreatedAt`.
- **Why:** The fallback is the more useful half of the rule, and an always-empty group is dead code.
- **Touches:** src/CarTracker.Shared/Logs/DocumentDtos.cs, src/CarTracker.Domain/Documents/DocumentService.cs, src/CarTracker.WebApp/src/screens/photos/PhotosPage.tsx
- **If wrong:** Drop the fallback and group undated photos under "Not dated" (small). `CreatedAt` on the DTO is harmless either way.

## A8 · accepted · medium
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 3.6, 5.6
- **Question:** The garage card shows the cover's thumbnail, and `GarageItem` gains only `coverPhotoId`. The spec also says the client "never asks for a missing" variant. A cover chosen from an HEIC uploaded on desktop has no thumbnail until the backfill runs, and the card cannot know that.
- **Assumed:** Keep the spec's shape (`coverPhotoId` only). The card requests `variant=thumbnail`, and on a 404 or any failure renders nothing, exactly as when no cover is set. It never falls back to the original, so the garage never downloads full-size photos.
- **Why:** One extra request per card in a rare case is cheaper than a second field on a metrics projection. A missing image is the same state the owner sees with no cover.
- **Touches:** src/CarTracker.WebApp/src/components/VehicleCard.tsx, src/CarTracker.WebApp/src/screens/GaragePage.test.tsx
- **If wrong:** Add `CoverPhotoHasThumbnail` to `GarageItem` and skip the request when false (small, contract change).

## A9 · accepted · medium
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 4.1, 5.1, 6.1, 6.7
- **Question:** With `tests=auto` and acceptance criteria about UI, which UI test layer? The project has Vitest with Testing Library and axe, and no Playwright. Deliverable 1 (an HEIC from Safari on an iPhone shows as a thumbnail in Chrome on a desktop) depends on real browser codecs that jsdom and a headless engine cannot reproduce.
- **Assumed:** UI tests are Vitest + Testing Library component tests, the project's existing tool, with `createImageBitmap`, `HTMLCanvasElement.prototype.toBlob` and `IntersectionObserver` stubbed per test. No Playwright is added. Deliverable 1, the EXIF check (A3) and the side-by-side layout are checked by hand against the running AppHost in 6.7, and the execute run lists them as criteria checked manually.
- **Why:** Adding a browser test framework is a decision of its own, and even Playwright's WebKit would not show iPhone Safari's HEIC decode.
- **Touches:** src/CarTracker.WebApp/src/**/*.test.ts(x)
- **If wrong:** Add Playwright with one Chromium test for the viewer's keyboard navigation and focus return (medium).

## A10 · accepted · medium
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 6.3, 6.4
- **Question:** "Placed where each record's detail already lives: the service record and expense edit sheets, and the issue card on Issues." A new record has no id to link to, and the issue card is a list item, not a sheet.
- **Assumed:** The strip appears in `AddServiceSheet` and `AddExpenseSheet` only when editing an existing record, and not for `'new'`. On Issues it sits in each issue's list item (`.ilist li`), above the footer, with the timeline and Compare there too. `IssueSheet` does not get a strip.
- **Why:** Attach needs a link id. The issue list item is what the spec calls the issue card.
- **Touches:** src/CarTracker.WebApp/src/screens/ServiceHistoryPage.tsx, src/CarTracker.WebApp/src/screens/ExpensesPage.tsx, src/CarTracker.WebApp/src/screens/IssuesPage.tsx
- **If wrong:** Move or repeat the strip in `IssueSheet` (small).

## A11 · accepted · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 1.3, 4.4
- **Question:** The spec gives four labels ("V5C logbook", "Car photo", "Service invoice", "Workshop manual"). What are the rest, and where do group labels such as "Legal & cover" come from, since `DocumentTypeInfo` carries `group` as an enum value only?
- **Assumed:** Labels: V5C logbook, Bill of sale, MOT certificate, Insurance certificate, Road tax, Breakdown cover, Service invoice, Parts receipt, Warranty, Receipt, Owner's handbook, Workshop manual, Diagram, Car photo, Condition photo, Other. Group labels live in the client as `Record<DocumentGroup, string>` typed off the generated enum (Ownership, Legal & cover, Work & parts, Reference, Photos, Other), so a new group fails `tsc -b`. A group the build does not know at runtime renders under its raw name rather than being dropped.
- **Why:** The standing rule allows `Record<Enum, ...>` over the generated type. The labels follow the spec's four examples: what an owner would call the paper, in sentence case.
- **Touches:** src/CarTracker.Domain/Documents/DocumentCategories.cs, src/CarTracker.WebApp/src/api/documents.ts
- **If wrong:** Change strings in one switch (small), or add `groupLabel` to `DocumentTypeInfo` (small, contract change).

## A12 · accepted · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 2.5
- **Question:** The export writes `DocumentRowItem`, a hand-listed projection, and describes `documents.ListRowsAsync` as "every document row as stored". Should it carry the four new columns?
- **Assumed:** Yes. `DocumentRowItem` gains `ThumbnailPath`, `DisplayPath`, `Width` and `Height`, appended with defaults. Import still skips document rows, so nothing reads them back.
- **Why:** The export is meant to be the whole row, and the rendition paths tell the reader which files under the documents volume belong to which row.
- **Touches:** src/CarTracker.Shared/Logs/ExportRowDtos.cs, src/CarTracker.Domain/Documents/DocumentService.cs, tests/CarTracker.Data.Tests/AccountExportTests.cs
- **If wrong:** Leave the projection as it is (small).

## A13 · accepted · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 4.4, 4.5, 5.3
- **Question:** `docs/specs/2026-11-27-assistant-photo-filing/` reuses "the upload function from the gallery spec (extracted to `api/documents.ts`)" and opens "the upload sheet pre-filled". Neither shape is specified here.
- **Assumed:** `api/documents.ts` exports `uploadDocument(reg, file, meta)`, which makes the renditions itself and returns an `ApiResult<DocumentItem>`, with no React dependency. It also exports `useDocuments`, `useDocumentTypes` and `kindOf(type, types)`. `UploadSheet` moves to `screens/documents/UploadSheet.tsx` with props `{ open, onClose, reg, initial?: { type?, title?, documentDate?, link?, files? }, multiple? }`. A preset link shows as a read-only chip and travels as its id field.
- **Why:** The next spec can then call the function from a chat card and open the sheet from a query string without refactoring this one.
- **Touches:** src/CarTracker.WebApp/src/api/documents.ts, src/CarTracker.WebApp/src/screens/documents/UploadSheet.tsx
- **If wrong:** Rename or reshape the props when the next spec runs (small).

## A14 · accepted · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 4.4, 5.4
- **Question:** The backfill runs "once per document per session", and the Photos screen has a filter. Where do those live?
- **Assumed:** In memory only: a module-level `Set<number>` of document ids already attempted, which clears on reload, and component state for the filter. No new `localStorage` or `sessionStorage` key, so `lib/clientStorage.ts` and the cookie notice do not change.
- **Why:** A new storage key reopens the DEC-024 consent question for no benefit. A reload retrying the backfill once is harmless.
- **Touches:** src/CarTracker.WebApp/src/api/documents.ts, src/CarTracker.WebApp/src/screens/photos/PhotosPage.tsx
- **If wrong:** Declare a `session` key in `clientStorage.ts` (small).

## A15 · accepted · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 1.6, 1.7
- **Question:** Task 1 renames `DocumentType.Photo`, and the web Documents screen hand-types its own `DocumentType` union containing `'Photo'`, which `tsc` cannot catch. Tasks 4 to 6 rewrite that screen, but each task is its own commit.
- **Assumed:** Task 1 does the minimum to keep every commit working: rename `DocumentType.Photo` to `ConditionPhoto` in existing C# tests and fixtures (`DocumentTests`, `ChecksAndTasksTests`), switch `DocumentService.GetLogAsync`'s split to `DocumentCategories.Kind`, and point `DocumentsPage.tsx`'s local `DocumentType` at `components['schemas']['DocumentType']` with the type list updated, and its test fixtures to `'ConditionPhoto'`. These are mechanical renames. No assertion is loosened.
- **Why:** Commit-per-task means each commit should build and pass on its own.
- **Touches:** tests/CarTracker.Data.Tests/DocumentTests.cs, tests/CarTracker.Data.Tests/ChecksAndTasksTests.cs, src/CarTracker.WebApp/src/screens/DocumentsPage.tsx, src/CarTracker.WebApp/src/screens/DocumentsPage.test.tsx
- **If wrong:** Nothing to undo. Task 4 replaces the screen anyway.

## A16 · accepted · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 4.6
- **Question:** The Documents screen's link chip points at `/v/{reg}/service` (and `expenses`, `issues`), but no `/v` route exists: vehicle screens live at `/:reg/...`. The chip leads to a not-found page today. The spec does not mention it.
- **Assumed:** Fix it while rewriting the screen: the chip uses `hrefFor(LINK_SCREEN[kind], reg)`, and a component test asserts the `href`.
- **Why:** The spec's whole point is following a link between a document and its record in both directions. A broken half defeats that.
- **Touches:** src/CarTracker.WebApp/src/screens/DocumentsPage.tsx, src/CarTracker.WebApp/src/screens/DocumentsPage.test.tsx
- **If wrong:** Revert the one line (small).

## A17 · accepted · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 5.2
- **Question:** Where does Photos sit in the nav table, and what does its bottom-bar centre slot do?
- **Assumed:** `photos: { label: 'Photos', group: 'records', topLevel: false, scoped: true }`, declared directly after `documents` so the More panel and sheet list it beside Documents. No `nav` or `bottom` label. The centre slot is an Upload action, as on Documents. `'photos'` is added to `BUILT` in `routes.tsx` and gets its own route under `:reg`.
- **Why:** The spec says "beside Documents". Neither screen is a daily action, so neither belongs in the top bar.
- **Touches:** src/CarTracker.WebApp/src/shell/nav.ts, src/CarTracker.WebApp/src/routes.tsx
- **If wrong:** Change one table entry (small).

## A18 · accepted · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 5.5
- **Question:** The viewer is "a full-screen dialog built on the existing `Sheet` / dialog primitive". `Sheet` renders a form with a title, a submit handler and a footer, and is laid out as a bottom sheet or centred dialog.
- **Assumed:** `PhotoViewer` is its own component built on the hooks `Sheet` is built on (`useFocusTrap`, `useInertBackground`, `useScrollLock`), with `role="dialog"`, `aria-modal` and a label naming the photo. It does not wrap `Sheet`. Layout uses CSS classes in `components.css`. Sizing comes from the `img` element's `width` and `height` attributes, never a `style` attribute, because of the strict CSP. The same component has a two-pane compare mode for 6.4.
- **Why:** This reuses the focus and inertness behaviour that is already tested without forcing a form into an image viewer.
- **Touches:** src/CarTracker.WebApp/src/screens/photos/PhotoViewer.tsx, src/CarTracker.WebApp/src/styles/components.css
- **If wrong:** Add a `variant="fullscreen"` to `Sheet` and render the viewer through it (small to medium).

## A19 · accepted · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 1.3
- **Question:** Task 1.3 asks for `DocumentCategories`' switches to be exhaustive "with no default". In C#, an enum switch with no discard arm always raises CS8524 (unnamed enum values), so the build would carry a permanent warning, and the domain's other enum switches (`VehicleFieldClears`, `BudgetCalculator`) all end in `_ => throw new ArgumentOutOfRangeException(...)`.
- **Assumed:** Every named member has its own arm and the switch ends in `_ => throw new ArgumentOutOfRangeException(...)`, matching the codebase. The guard against a forgotten member is `DocumentCategoriesTests`, which enumerates `Enum.GetValues<DocumentType>()` and fails on the throw.
- **Why:** Same behaviour for every named member, no standing warning, and the test catches a new member either way.
- **Touches:** src/CarTracker.Domain/Documents/DocumentCategories.cs
- **If wrong:** Delete the two discard arms and add `<NoWarn>CS8524</NoWarn>` to the Domain project (small).

## A20 · accepted · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 1.4, 1.5
- **Question:** database-schema.md gives `ck_documents_dimensions` as `(width IS NULL AND height IS NULL) OR (width > 0 AND height > 0)`. With a width and no height the second half is NULL rather than false, and PostgreSQL passes a CHECK that evaluates to NULL, so the half pair the constraint exists to refuse is accepted. The migration test caught it.
- **Assumed:** The constraint is `(width IS NULL AND height IS NULL) OR (width IS NOT NULL AND height IS NOT NULL AND width > 0 AND height > 0)`, in the configuration, the migration and the snapshot, with a comment saying why the `IS NOT NULL` tests are there.
- **Why:** It is what the spec's constraint means, and the spec's own test ("rejects a width without a height") fails without it.
- **Touches:** src/CarTracker.Data/Configuration/DocumentConfiguration.cs, src/CarTracker.Data/Migrations/*_AddPhotoGalleryAndDocumentCategories*.cs, src/CarTracker.Data/Migrations/CarTrackerDbContextModelSnapshot.cs
- **If wrong:** Nothing to undo; the spec's text is the one to correct.

## A21 · accepted · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 2.6, 4.4, 5.1
- **Question:** The spec writes the variant in lower case (`?variant=thumbnail`). ASP.NET Core minimal APIs bind an enum query parameter case-sensitively, so a `DocumentVariant?` parameter answers 400 to `thumbnail` and 200 to `Thumbnail` (checked in a scratch app). Accept lower case with a string parameter, or keep the enum?
- **Assumed:** Keep `DocumentVariant?` on the endpoint, so the contract carries the enum and the client builds the URL from the generated type: `?variant=Thumbnail` / `Display` / `Original`. This is how the only other enum query parameter travels (`/budget?period=CalendarYear`). Tests in tasks 4 to 6 assert `variant=Thumbnail` where the task text says `variant=thumbnail`.
- **Why:** The standing rule is to source values from the contract rather than hand-type them; a string parameter would drop the enum from the contract.
- **Touches:** src/CarTracker.WebApi/Endpoints/DocumentEndpoints.cs, src/CarTracker.WebApp/src/api/documents.ts
- **If wrong:** Take `string? variant` and `Enum.TryParse(..., ignoreCase: true)` in the endpoint, losing the enum from the contract (small).

## A22 · accepted · medium
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 2.7, 6.7
- **Question:** 2.7 and 6.7 ask for checks "by hand against the AppHost": a partial rendition upload answering 400, the cache headers and `ETag` on `variant=thumbnail`, a 404 for `variant=Display` on a PDF, an HEIC from Safari on an iPhone, `exiftool` on a rendition. Every `/api` route needs an Auth0 token, and the batch runs unattended with no browser session or iPhone.
- **Assumed:** The parts that can be checked without a signed-in browser are: the contract diff (additive), the variant 404 and the rendition rules through the domain tests (`DocumentRenditionTests`), and enum binding plus the `Cache-Control` and `ETag` headers through a scratch minimal API using the same `TypedResults.File(..., entityTag:)` call. The signed-in checks (a real 400 on a partial multipart set, real headers through the gateway, the HEIC and EXIF checks in 6.7, the side-by-side layout) are left for the human and listed in the run's report. The sub-tasks are ticked for the automated part, with this assumption marked.
- **Why:** Driving Auth0 sign-in from an unattended run is not possible, and the alternative of skipping the check entirely tells the reviewer less.
- **Touches:** docs/specs/2026-11-27-photo-gallery-document-categories/tasks.md
- **If wrong:** Run the listed checks by hand before release (about 20 minutes with an iPhone and a desktop browser).

## A23 · accepted · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 5.4, 5.5
- **Question:** The viewer's arrow keys "step in date order", and the grid shows months newest first. Does ArrowRight go to the next tile on screen (an older photo) or to a later date?
- **Assumed:** The viewer steps through the photos in the order the grid shows them: newest first, each photo dated by `documentDate`, else by its upload day (A7), ties broken by newest id. ArrowRight and a left swipe go to the next tile (older), ArrowLeft and a right swipe to the previous one (newer). The issue Compare view (6.4) is separate and always puts the earliest on the left.
- **Why:** What is next on screen is what the owner expects the arrow to show; a viewer running against the grid's order would make the first tile the last photo.
- **Touches:** src/CarTracker.WebApp/src/screens/photos/PhotosPage.tsx, src/CarTracker.WebApp/src/screens/photos/PhotoViewer.tsx
- **If wrong:** Reverse the array the page hands the viewer (one line).

## A24 · accepted · low
- **Batch:** 2026-11-27-photos-and-vehicle-fields
- **Task:** 5.1, 5.3
- **Question:** 5.3 says multi-file upload has "one shared category and date, a title per file", while 5.1 and technical-spec.md say each file's date defaults to its own `lastModified` day and is editable. One shared date cannot default to three different days.
- **Assumed:** One shared category; a title and a date per file, the date defaulting to that file's `lastModified` day (local calendar day) and editable on its row.
- **Why:** It is the only reading that satisfies the per-file default, and photos picked together are often from different days.
- **Touches:** src/CarTracker.WebApp/src/screens/documents/UploadSheet.tsx
- **If wrong:** Replace the per-row date inputs with one shared date field defaulting to the first file's day (small).
