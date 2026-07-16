# Spec Tasks

## Tasks

- [ ] 1. Storage and upload infrastructure
  - [ ] 1.1 Write tests: a streamed upload writes one file under the volume root and one row; `Sha256` matches a known fixture; a byte-identical re-upload is detected as a duplicate
  - [ ] 1.2 Bind the volume root from configuration; write bytes under `{root}/{vehicleId}/{name}`, store the relative path on `FilePath`
  - [ ] 1.3 Compute `Sha256` in the same streaming pass; capture `ContentType` and `SizeBytes` from the received part, never a client field
  - [ ] 1.4 Content-type allow-list (PDF + images) and a size cap; a disallowed or oversize file is a 400
  - [ ] 1.5 Verify all tests pass

- [ ] 2. `DocumentEndpoints`
  - [ ] 2.1 Write tests: GET list/metadata, GET file (inline vs attachment), PATCH metadata/link, DELETE removes row and file; at most one link; a cross-vehicle link is a 400
  - [ ] 2.2 New `DocumentEndpoints.cs` under `/api/vehicles/{registration}/documents`, `VehicleLookup` resolution, no `AnomalyScanner` call
  - [ ] 2.3 POST multipart upload; GET file streams with stored `ContentType` and the right `Content-Disposition`
  - [ ] 2.4 Link validation (target exists and is this vehicle's); DELETE frees the bytes as well as the row
  - [ ] 2.5 Regenerate the contract and TS types; verify the staleness gate is green
  - [ ] 2.6 Verify all tests pass

- [ ] 3. Documents screen
  - [ ] 3.1 Write tests: Papers renders on `<DataTable>`; the photo grid shows `Type = Photo`; link chips navigate; `usePlate()` not `plate={reg}`
  - [ ] 3.2 Port `documents.dc.html` — Papers list on `<DataTable>`, photo-sets grid, upload sheet, simple viewer with download-keeps-the-original
  - [ ] 3.3 Render `DocumentType` and the FK links as chips (`→ service record` / `→ expense` / `→ issue`); no free-form tag table
  - [ ] 3.4 Route, axe sweep, coverage-guard exemptions with reasons for the sheet, viewer and both lists
  - [ ] 3.5 Verify all tests pass

- [ ] 4. Photo baselines and the issues cross-reference
  - [ ] 4.1 Write tests: a photo linked to an `Issue` surfaces in the baseline set carrying `→ issue`; an unlinked photo sits in the baseline
  - [ ] 4.2 Surface the issue-linked condition photos as the baseline set the issues concept compares worsening against
  - [ ] 4.3 Verify all tests pass

- [ ] 5. Prove it end to end on BT53
  - [ ] 5.1 Upload the MOT-pass certificate (`Type = MOT`) linked to the 8 Jul 2026 service record; confirm the `→ service record` chip
  - [ ] 5.2 Confirm View opens the PDF and Download returns the original bytes unchanged
  - [ ] 5.3 Upload a "rear tyre cracking" photo linked to an issue and a "front ¾ baseline" photo with no link; confirm the grid and the `→ issue` chip
  - [ ] 5.4 Confirm a byte-identical re-upload is reported as a duplicate, and DELETE removes both the row and the file
  - [ ] 5.5 Full suite, both builds, codegen gate; update roadmap and CLAUDE.md
