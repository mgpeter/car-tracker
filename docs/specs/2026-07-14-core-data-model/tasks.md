# Spec Tasks

## Tasks

- [x] 1. Project scaffolding and DbContext
  - [x] 1.1 Create the solution and `src/CarTracker.Data`, `src/CarTracker.Shared`, and `tests/CarTracker.Data.Tests` projects targeting .NET 10
  - [x] 1.2 Add Npgsql, EFCore.NamingConventions, xUnit, and Testcontainers.PostgreSQL package references
  - [x] 1.3 Write a Testcontainers fixture that spins up PostgreSQL 17, so every later test asserts against a real database
  - [x] 1.4 Create `CarTrackerDbContext` with `UseSnakeCaseNamingConvention` and `ApplyConfigurationsFromAssembly`
  - [x] 1.5 Add the `IAuditable` interface and `AuditStampingInterceptor` stamping `CreatedAt`/`UpdatedAt` in UTC via `TimeProvider`
  - [x] 1.6 Write tests: timestamps are stamped on insert and update; `Source` is never silently defaulted
  - [x] 1.7 Verify all tests pass — 7 passing against PostgreSQL 17

  **Deviations from the spec, applied 2026-07-14:**
  - 1.3: the fixture starts the container and exposes a connection string; it does not apply migrations, because none exist until task 5. Revisit there.
  - 1.5: implemented as a `SaveChangesInterceptor`, not a `SaveChanges` override. The override could not be tested in task 1 — it needs an entity, and none exist until task 2. An interceptor attaches to any context, so it is testable against a probe entity in the test project. It is also the idiomatic EF Core approach and keeps the DbContext thin.
  - Added `Directory.Packages.props` (central package management, transitive pinning). Not in the spec. EF Core arrives via three parents at three patch versions and `Microsoft.EntityFrameworkCore.Design` is `PrivateAssets=all`, so its version never reached the test project — producing a CS1705 mismatch. Pinning centrally is the durable fix for an eight-project solution.
  - `EntrySource` has no zero member, so `default(EntrySource)` is undefined and an unset `Source` is detectable rather than silently reading as `Web`.

- [x] 2. Vehicle, reference tables, and enums
  - [x] 2.1 Write tests for the vehicle registration unique index — `BT53 AKJ` and `bt53akj` must collide
  - [x] 2.2 Add shared enums to `CarTracker.Shared`: `MaintenanceTaskKind`, `MaintenanceTaskStatus`, `Priority`, `Severity`, `FillLevel`, `IssueStatus`, `EquipmentStatus`, `EntrySource`, `DocumentType`, `MileageOrigin`, plus `VehicleStatus` and `FuelType`
  - [x] 2.3 Add `ExpenseCategory`, `Garage`, and `WashLocation` reference entities with their configurations
  - [x] 2.4 Add the `Vehicle` entity with insurance, breakdown, fluid, and tyre blocks as owned types
  - [x] 2.5 Write the `VehicleConfiguration` with explicit column types, the normalised registration index, the `status` check constraint, and the `is_default` partial unique index (DEC-007)
  - [x] 2.6 Write tests asserting `mot_expiry_seed` exists but no `mot_expiry` column does, and that a second `is_default = true` vehicle is rejected while zero defaults is legal
  - [x] 2.7 Verify all tests pass — 15 passing (7 audit + 8 schema) against PostgreSQL 17

  **Deviations from the spec, applied 2026-07-14:**
  - The spec's `TaskStatus` enum is named `MaintenanceTaskStatus` (and `TaskKind` → `MaintenanceTaskKind` for symmetry): `TaskStatus` is ambiguous with `System.Threading.Tasks.TaskStatus` under implicit usings — the same collision that renamed the entity `Task` → `MaintenanceTask`.
  - `VehicleStatus` and `FuelType` enums added — the schema's check constraints imply them; the original enum list predates DEC-007. Member names match stored strings exactly (`SORN`, `LPG`) so `HasConversion<string>()` needs no custom mapper.
  - The normalised registration is a **stored generated column** (`registration_normalized`) with a unique index, not an expression index — EF cannot model expression indexes, and the generated column is equivalent while working under both `EnsureCreated` and migrations. Schema doc updated to match.
  - `PostgresFixture` gained `EnsureDatabaseAsync(name)`: each DbContext model gets its own database in the container, because `EnsureCreated` is a no-op once *any* tables exist — two models sharing one database means whichever test class runs second silently gets no schema.

- [x] 3. Log entities
  - [x] 3.1 Write tests for the fuel-to-expense mirror link: unique per fill, cascade on delete
  - [x] 3.2 Add `MileageReading` with both indexes, and a test proving a non-monotonic reading inserts without error
  - [x] 3.3 Add `ExpenseEntry` and `FuelEntry` with the `fuel_entry_id` link
  - [x] 3.4 Add `ServiceRecord`, and a test proving an 83,000 mi row above current mileage inserts without error
  - [x] 3.5 Add `TyreReading` and `WashEntry`
  - [x] 3.6 Write tests asserting no `mpg`, `l_per_100km`, `miles_since_last`, or running-total column exists on any log table
  - [x] 3.7 Verify all tests pass — 21 passing against PostgreSQL 17 (also covers category `RESTRICT` delete)

- [x] 4. Checks, tasks, and remaining entities
  - [x] 4.1 Write tests for the `maintenance_tasks` constraints: garage only on Workshop; completed date iff Done
  - [x] 4.2 Add `CheckDefinition` and `CheckLog` scoped through the definition, with no status or next-due column
  - [x] 4.3 Add `MaintenanceTask` with both check constraints and the `service_record_id` promotion link
  - [x] 4.4 Add `BudgetCategory`, `Issue`, and `EquipmentItem` with their constraints
  - [x] 4.5 Add `Document` with its three nullable link FKs and `ON DELETE SET NULL` behaviour
  - [x] 4.6 Write a test proving a check definition with zero logs is queryable and distinguishable from a logged one
  - [x] 4.7 Verify all tests pass — 26 passing against PostgreSQL 17

  **Deviations from the spec, applied 2026-07-14:**
  - `CheckResult` enum added (`OK`/`Attention`/`Failed`) — the `check_logs.result` check constraint implies it; the original enum list omitted it.
  - `MaintenanceTaskKind.Diy` renamed to `DIY` so `HasConversion<string>()` matches the stored `'DIY'` literal, consistent with `SORN`/`LPG`.

- [x] 5. Initial migration and seed data
  - [x] 5.1 Write a test asserting the seeded database contains exactly the 13 expense categories and an empty `vehicles` table (DEC-007 — vehicles are never seeded; the 18-check assertion lives in the importer spec)
  - [x] 5.2 Generate the initial migration with `dotnet ef migrations add InitialSchema`
  - [x] 5.3 Review the generated SQL against `sub-specs/database-schema.md` — column types, check constraints, and indexes must match, including `ix_vehicles_default`
  - [x] 5.4 Seed the 13 expense categories with `is_system = true` (no `source` — reference tables carry no audit block)
  - [x] 5.5 Write a test proving import prerequisites: unique normalised registration index and at-most-one-default partial index behave as specified (covered in `VehicleSchemaTests`)
  - [x] 5.6 Write a schema-wide test enumerating all columns and asserting none matches the known derived-value names
  - [x] 5.7 Verify all tests pass and `dotnet ef database update` produces the full schema — 32 passing; 17 entity tables and 13 seeded categories verified in psql

  **Deviations from the spec, applied 2026-07-14:**
  - Added `DesignTimeDbContextFactory` — EF tooling cannot construct a context whose constructor takes a `TimeProvider`. Reads `CARTRACKER_CONNECTION`, falling back to a local default.
  - 5.4 originally said seed `source = 'seed'`; `expense_categories` has no `source` column, because reference tables carry no audit block. Spec corrected. Consequence: `EntrySource.Seed` has no user at migration time — it exists for auditable rows created from a template, such as the add-car flow's generic starter check set.
  - Tests against the real model now apply **migrations**, not `EnsureCreated`, so the suite verifies the migration that actually ships (closing the deferral noted in task 1.3). The audit-probe context keeps `EnsureCreated` — it is test-only and has no migrations.
  - `MigrationAndSeedTests` uses its own database: "the vehicles table is empty" must assert the migration's behaviour, not race the other classes that insert vehicles into the shared one.
  - Generated SQL verified: 17 entity tables, 30 indexes, 56 check constraints, including `ix_vehicles_default` as a partial unique index and `registration_normalized` as a stored generated column.
