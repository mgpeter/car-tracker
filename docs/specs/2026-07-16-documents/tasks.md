# Spec Tasks

## Tasks

- [x] 1. Storage and upload infrastructure
  - [x] 1.1 Tests written - `DocumentTests` (15, Testcontainers + a real temp directory): a streamed upload
        writes one file and one row, the hash matches a known fixture, identical bytes resolve to the one file,
        an oversize upload leaves nothing behind, and the allow-list refuses `text/html`.
  - [x] 1.2 `Documents:RootPath` bound in `Program.cs` and resolved to an absolute path there, so the domain
        takes no dependency on the hosting stack for one string. Relative values resolve against the content
        root - the dev default works with no configuration, the container overrides with `Documents__RootPath`.
  - [x] 1.3 **Content-addressed**: the file is named for the SHA-256 of its own bytes under `{root}/{vehicleId}/`,
        hashed *while* streaming through a `CryptoStream` in one pass. Two `scan.pdf`s cannot collide, a
        client-supplied filename never becomes a path component, and an identical re-upload finds its bytes
        already there. `ContentType`/`SizeBytes` come from the received part, never a client field.
  - [x] 1.4 Allow-list (PDF + jpeg/png/webp/heic/heif/gif) and a 25 MB cap enforced **while reading**, not from
        a Content-Length header - the point of a cap is the case where the client's claim is wrong.
  - [x] 1.5 All 15 pass.

- [x] 2. `DocumentEndpoints`
  - [x] 2.1 Tests cover list/split, link validation, re-tag, detach, delete-frees-bytes, and the two seams that
        only exist because storage is not transactional with the database: a row whose file has gone, and a
        file two rows share.
  - [x] 2.2 New `DocumentEndpoints.cs` under `/api/vehicles/{registration}/documents`, `VehicleLookup`
        resolution, and **no `AnomalyScanner` call** - a document is not a derived input, moves no figure and
        trips no detector. The only write path in the app that does not scan.
  - [x] 2.3 POST is `multipart/form-data` (`.DisableAntiforgery()`); GET `/{id}/file` streams the stored bytes
        under the stored content type, `?download=true` switching the disposition. Nothing re-encodes.
  - [x] 2.4 At most one link, and the target must be this vehicle's - the FKs enforce existence, not ownership.
        DELETE removes the row then the bytes, skipping the file if another row still points at it.
  - [x] 2.5 Contract and TS types regenerated - additive only, 468 insertions / 0 deletions.
  - [x] 2.6 All pass - 221 Domain, 155 Data.

- [x] 3. Documents screen
  - [x] 3.1 Tests written - `DocumentsPage.test.tsx` (8): papers list with kind and link chips, "not attached"
        as a real state, the photo grid, the baseline note, view/save on every paper, both empty states, an
        axe sweep, and the table's accessible name.
  - [x] 3.2 Ported: **Papers on `<DataTable>`** (its fifth consumer) and **photo sets as a grid**, which is the
        design's own eyebrow - "PDFs listed, photo sets gridded, they are not the same thing" - and the same
        seam that keeps checks a list.
  - [x] 3.3 Chips are `DocumentType` and the link, nothing more. No free-form tags table was invented to match
        the mock's `identity` / `statutory` chips, and the `→ policy` chip stays unbuilt: there is no `PolicyId`
        on `Document` and this spec does not add one.
  - [x] 3.4 Routed, `usePlate()`, axe-swept. No coverage-guard exemption needed - every new component is local
        to the page and swept with it.
  - [x] 3.5 All pass - 449 front-end.

- [x] 4. Photo baselines and the issues cross-reference
  - [x] 4.1 `A_document_links_to_one_record_and_the_chip_names_it` and
        `Deleting_the_linked_record_severs_the_link_and_keeps_the_document` cover the evidential case; the
        screen test covers the linked/unlinked split in the grid.
  - [x] 4.2 The grid marks issue-linked photos with a `→ issue` pill and states in its footnote that the
        unlinked ones are the baseline "worsening" is measured against.
  - [x] 4.3 All pass.

- [x] 5. Prove it end to end
  - [x] 5.1–5.4 Covered by the Testcontainers suite rather than by hand, because every claim is about a row and
        a file together: filing with a `→ service record`-shaped link, the bytes reading back byte-identical
        (the hash is asserted against an independently computed fixture), a byte-identical refile refused by
        name, and DELETE removing both the row and the file.
  - [x] 5.5 Full suite green (221 Domain, 155 Data, 449 front-end), both builds clean, codegen gate additive
        only. README §3.9, roadmap and CLAUDE.md updated.

## Decisions taken during the port

- **The bytes cannot be an `<img src>` or an `<a href>`.** The app authenticates with an Auth0 bearer, and a
  plain navigation does not carry our `Authorization` header - pointing an image straight at the file endpoint
  gets a 401 and a broken-image icon. Added `apiBlob()` beside `apiRequest()` in `api/client.ts`: the bytes come
  through the same authenticated fetch seam and become an object URL, revoked on unmount so a photo grid does
  not pin every image it has ever shown. This was not in the spec and is the one thing the port could not have
  been written without discovering.
- **"View" opens the object URL in a tab rather than an in-page PDF viewer.** The spec asked for a *simple*
  viewer; a tab is the browser's own, handles PDFs and images alike, and costs no embed. An iframe/object embed
  would be more code for a worse PDF reader than the one already installed.
- **Delete checks whether another row shares the file.** Content-addressing makes that possible, and without the
  check removing one document would pull the bytes out from under its twin.
