# Spec Requirements Document

> Spec: Core Data Model
> Created: 2026-07-14
> Status: Planning

## Overview

Establish the EF Core data model for all 14 entities in README §2, with a vehicle id on every record from the start, plus the initial migration and seed data. This is the foundation every later phase builds on, and its central obligation is that no derived value gets a column to go stale in.

## User Stories

### History survives the move

As the owner, I want the schema to hold everything the 13-sheet workbook holds, so that importing my history loses nothing and I never need to open the spreadsheet again.

Every sheet must map to a table without dropping columns. The workbook carries per-fill fuel data, service records with next-due targets, tyre readings with five pressures and four treads, a wash log, an issues watchlist, an equipment inventory, and reference lists in side columns. If any of these lacks a home, the import is lossy and the spreadsheet stays alive as a shadow system.

### Wrong numbers become impossible, not just fixed

As the owner, I want derived figures to have nowhere to be stored, so that the four defects in the current Dashboard cannot recur in the new system.

Current mileage, MPG, spend totals, MOT countdown, and check status must all be computed from log rows on read. The schema is the enforcement mechanism: if there is no `total_litres` column, nothing can double-count into it. Where the old sheet stored a derived value, this schema stores its inputs instead.

## Spec Scope

1. **Entity model** - All 14 entities from README §2 as EF Core POCOs in `CarTracker.Data`, with a `VehicleId` foreign key on every vehicle-scoped record.
2. **Explicit configurations** - One `IEntityTypeConfiguration<T>` per entity in `Configuration/`, with column types stated explicitly and no reliance on conventions.
3. **Audit and source tracking** - `CreatedAt`, `UpdatedAt`, and `Source` (web/mcp/import/seed) on every mutable entity, per README §6.
4. **Initial migration** - A single migration producing the full schema against PostgreSQL 17.
5. **Seed data** - Expense categories, check definitions (all 18), garages, and the BT53 AKJ vehicle record.

## Out of Scope

- The xlsx importer (its own spec: `2026-07-14-xlsx-importer`)
- The derived-metrics service (its own spec: `2026-07-14-derived-metrics-service`)
- Any Web API endpoints, controllers, or DTOs
- Any React UI
- Document file upload handling — the `Document` entity and its path column exist here, but storage and serving come in Phase 3
- Insurance renewal history — the insurance block is modelled as a single current block per README §2, not a time series
- Multi-vehicle UI or vehicle switching — the schema permits it (DEC-002), nothing surfaces it

## Expected Deliverable

1. `dotnet ef database update` produces a PostgreSQL schema holding all 14 entities, verifiable by inspecting tables in psql.
2. Seed data lands on migration: 18 check definitions, the expense category list, and the BT53 AKJ vehicle record with its purchase state (14 Mar 2026, 76,632 mi) are queryable.
3. A schema review confirms no table carries a derived column — no stored totals, no stored current mileage, no stored MOT countdown.
