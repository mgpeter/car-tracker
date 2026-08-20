# Spec Tasks

These are the tasks to be completed for the spec detailed in @docs/specs/2026-08-19-account-data-import/spec.md

> Ordered so that nothing is built before the thing it is validated against. The reading half comes first
> because the whole spec rests on the export's own DTOs being the one definition of the format, and the round
> trip - the headline test - cannot be written until both halves exist, so it lands with the commit rather
> than with the preview.

## Tasks

- [x] 1. **Read the file** - the payload DTOs, the parse, and the refusals
  - [x] 1.1 Write tests for parsing: a well-formed export round-trips into the payload; a truncated file, a
        non-JSON file and a JSON file that is not an export of this app are each refused with a message naming
        what failed; unknown properties are ignored; an absent required field is refused rather than defaulted.
  - [x] 1.2 `ImportPayload` and its per-vehicle block, deserialised into the export's own DTOs from
        `CarTracker.Shared/Logs/` with `AccountExportService.Json` as the options. The vehicle profile
        deserialises into `Vehicle` itself, as the export writes it.
  - [x] 1.3 `ImportReader` - parse, distinguish unreadable from invalid, and cap at 25 MB while reading.
  - [x] 1.4 Structural validation: required fields present, every remapped foreign key resolvable within the
        file, at most one `isVehiclePurchase` per vehicle, no watch link crossing vehicles. Errors keyed
        `vehicles[0].expenses[7].fuelEntryId`.
  - [x] 1.5 Verify tests pass.

- [x] 2. **Preview** - what it would do, writing nothing
  - [x] 2.1 Write tests: a preview writes no rows; reference lists are counted against the account's own by
        name; a registration the account already owns is reported as colliding with a proposal; the proposal
        increments past `-2` when `-2` is taken and truncates to fit `varchar(16)`; skipped documents and
        anomalies are counted; a foreign or expired `importId` is not found.
  - [x] 2.2 `ImportPreview` result shapes (source, reference counts, per-vehicle rows, warnings).
  - [x] 2.3 `RegistrationProposer` - the collision rule, shared by preview and commit.
  - [x] 2.4 `PendingImportStore` on `IMemoryCache`, owner-keyed, 15 minutes, following `PendingWriteStore`.
  - [x] 2.5 `AccountImportService.PreviewAsync`.
  - [x] 2.6 Verify tests pass.

- [x] 3. **Commit** - the id map, the ordered insert, one transaction
  - [x] 3.1 Write tests: **the round trip** (export A, import into empty B, export B, compare with ids,
        timestamps and the account block normalised away); a second import into the same account produces a
        second complete vehicle and leaves the first untouched; **mirror fidelity** (the expense count equals
        the file's, so no mirror fired twice); **derived equality** (`IDerivedMetricsService` returns the same
        figures over both); isolation between two owners; a refused commit writes nothing.
  - [x] 3.2 `ImportIdMap` and the ordered writer, insert order forced by the foreign keys.
  - [x] 3.3 Reference-list merge by name through `ReferenceWriter`, never updating an existing row.
  - [x] 3.4 The transaction, inside `Database.CreateExecutionStrategy().ExecuteAsync(...)`.
  - [x] 3.5 `EntrySource.Import` on every row that carries a source, and the provenance line on the vehicle's
        notes.
  - [x] 3.6 `AnomalyScanner` over each imported vehicle after the rows land, counted in the report.
  - [x] 3.7 Verify tests pass.

- [x] 4. **The two endpoints**
  - [x] 4.1 `AccountImportEndpoints.cs` - `POST /api/account/import/preview` (multipart) and
        `POST /api/account/import/{importId}/commit`, on the `/api/account` group and its authorization.
  - [x] 4.2 Outcome-to-status mapping only; every refusal decided in the service.
  - [x] 4.3 Register the service and the store in DI.
  - [x] 4.4 Regenerate `api-contract/v1.json` and the TypeScript, and confirm the diff is additive.

- [x] 5. **The account screen's other half**
  - [x] 5.1 Write tests for the panel: the file input, the preview panel's headline count, an editable
        registration on a colliding vehicle, the commit report, and an expired preview degrading to
        "upload it again" rather than a dead button.
  - [x] 5.2 `ImportPanel` beside the export in `DangerZonePanel`'s section on `/account`.
  - [x] 5.3 Invalidate every query on a successful commit.
  - [x] 5.4 Verify tests pass.

- [x] 6. **Ship it**
  - [x] 6.1 Bump `VERSION` (minor) in the feature commit.
  - [x] 6.2 Full suite: `dotnet test`, `npx tsc -b`, `npm test`, `npm run build`.
