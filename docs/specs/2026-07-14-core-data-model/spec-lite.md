# Spec Summary (Lite)

Establish the EF Core data model for all 14 entities in README §2, with a vehicle id on every record, plus the initial PostgreSQL migration and seed data. Every entity is configured explicitly via `IEntityTypeConfiguration<T>` with stated column types, and carries created/updated timestamps and a source field for audit. The defining constraint is that no derived value gets a column: current mileage, MPG, spend totals, MOT expiry, and check status are all absent by design, so the schema itself forecloses the staleness defects the spreadsheet demonstrates.
