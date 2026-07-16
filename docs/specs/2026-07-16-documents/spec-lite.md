# Spec Summary (Lite)

Build the 17th and last workbook screen — Documents — as file-upload infrastructure plus a `DocumentEndpoints`
group plus the screen, not a data model: `Document` and its EF configuration already exist in full (verified
against `Document.cs` and `DocumentConfiguration.cs`), so there is no new entity and no migration.

Uploads are multipart, land on the configured volume with the relative path on `FilePath` and a streamed
`Sha256` for dedup, and can link to a service record, expense or issue via the three FKs already on the row —
links severed on delete, never cascaded, because a document outlives what it was attached to. Condition photos
linked to an `Issue` form the baseline set that "worsening" is measured against.
