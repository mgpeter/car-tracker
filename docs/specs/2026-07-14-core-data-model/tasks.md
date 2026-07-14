# Spec Tasks

## Tasks

- [ ] 1. Project scaffolding and DbContext
  - [ ] 1.1 Create the solution and `src/CarTracker.Data`, `src/CarTracker.Shared`, and `tests/CarTracker.Data.Tests` projects targeting .NET 10
  - [ ] 1.2 Add Npgsql, EFCore.NamingConventions, xUnit, and Testcontainers.PostgreSQL package references
  - [ ] 1.3 Write a Testcontainers fixture that spins up PostgreSQL 17 and applies migrations, so every later test asserts against a real database
  - [ ] 1.4 Create `CarTrackerDbContext` with `UseSnakeCaseNamingConvention` and `ApplyConfigurationsFromAssembly`
  - [ ] 1.5 Add the `IAuditable` interface and the `SaveChanges`/`SaveChangesAsync` override stamping `CreatedAt`/`UpdatedAt` in UTC
  - [ ] 1.6 Write tests: timestamps are stamped on insert and update; `Source` is never silently defaulted
  - [ ] 1.7 Verify all tests pass

- [ ] 2. Vehicle, reference tables, and enums
  - [ ] 2.1 Write tests for the vehicle registration unique index — `BT53 AKJ` and `bt53akj` must collide
  - [ ] 2.2 Add shared enums to `CarTracker.Shared`: `TaskKind`, `TaskStatus`, `Priority`, `Severity`, `FillLevel`, `IssueStatus`, `EquipmentStatus`, `EntrySource`, `DocumentType`, `MileageOrigin`
  - [ ] 2.3 Add `ExpenseCategory`, `Garage`, and `WashLocation` reference entities with their configurations
  - [ ] 2.4 Add the `Vehicle` entity with insurance, breakdown, fluid, and tyre blocks as owned types
  - [ ] 2.5 Write the `VehicleConfiguration` with explicit column types and the normalised registration index
  - [ ] 2.6 Write tests asserting `mot_expiry_seed` exists but no `mot_expiry` column does
  - [ ] 2.7 Verify all tests pass

- [ ] 3. Log entities
  - [ ] 3.1 Write tests for the fuel-to-expense mirror link: unique per fill, cascade on delete
  - [ ] 3.2 Add `MileageReading` with both indexes, and a test proving a non-monotonic reading inserts without error
  - [ ] 3.3 Add `ExpenseEntry` and `FuelEntry` with the `fuel_entry_id` link
  - [ ] 3.4 Add `ServiceRecord`, and a test proving an 83,000 mi row above current mileage inserts without error
  - [ ] 3.5 Add `TyreReading` and `WashEntry`
  - [ ] 3.6 Write tests asserting no `mpg`, `l_per_100km`, `miles_since_last`, or running-total column exists on any log table
  - [ ] 3.7 Verify all tests pass

- [ ] 4. Checks, tasks, and remaining entities
  - [ ] 4.1 Write tests for the `maintenance_tasks` constraints: garage only on Workshop; completed date iff Done
  - [ ] 4.2 Add `CheckDefinition` and `CheckLog` scoped through the definition, with no status or next-due column
  - [ ] 4.3 Add `MaintenanceTask` with both check constraints and the `service_record_id` promotion link
  - [ ] 4.4 Add `BudgetCategory`, `Issue`, and `EquipmentItem` with their constraints
  - [ ] 4.5 Add `Document` with its three nullable link FKs and `ON DELETE SET NULL` behaviour
  - [ ] 4.6 Write a test proving a check definition with zero logs is queryable and distinguishable from a logged one
  - [ ] 4.7 Verify all tests pass

- [ ] 5. Initial migration and seed data
  - [ ] 5.1 Write a test asserting the seeded database contains exactly 18 check definitions, one with zero logs
  - [ ] 5.2 Generate the initial migration with `dotnet ef migrations add InitialSchema`
  - [ ] 5.3 Review the generated SQL against `sub-specs/database-schema.md` — column types, check constraints, and indexes must match
  - [ ] 5.4 Transcribe seed data from the workbook: 13 expense categories, 18 check definitions with intervals, garages, wash locations
  - [ ] 5.5 Seed the BT53 AKJ vehicle record with purchase state and the OAT coolant spec
  - [ ] 5.6 Write a schema-wide test enumerating all columns and asserting none matches the known derived-value names
  - [ ] 5.7 Verify all tests pass and `dotnet ef database update` produces the full schema
