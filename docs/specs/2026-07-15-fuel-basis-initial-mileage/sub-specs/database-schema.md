# Database Schema

This is the database schema implementation for the spec detailed in @docs/specs/2026-07-15-fuel-basis-initial-mileage/spec.md

Two changes, both to tables created by `InitialSchema`. They ship as one migration,
`FuelBasisAndInitialMileage`.

## fuel_entries.fill_level becomes nullable

```sql
ALTER TABLE fuel_entries ALTER COLUMN fill_level DROP NOT NULL;
```

The `CHECK` constraint is unchanged — `fill_level IN ('Full', 'Half', 'Quarter')` already permits NULL, because
a `CHECK` passes on NULL by SQL's three-valued logic.

**Why nullable.** The column is now descriptive, and the source data does not have it: the workbook's
"Full tank / Half / Quarter" columns are computed range estimates, not a recorded level. `NOT NULL` forces
every writer to assert something it does not know, and the value it would pick — `Full` — is the one that used
to mean "trust this figure". A required field whose default silently means "trustworthy" is worse than no field.

Existing rows keep their value. Nothing reads it.

## mileage_readings.origin gains 'purchase'

```sql
ALTER TABLE mileage_readings DROP CONSTRAINT ck_mileage_readings_origin;
ALTER TABLE mileage_readings ADD CONSTRAINT ck_mileage_readings_origin
  CHECK (origin IN ('purchase', 'manual', 'fuel', 'tyre', 'wash', 'service'));
```

`MileageOrigin` is stored as a lowercase string via `HasConversion`, so the CLR member's ordinal is not
persisted and appending is safe. Only the `CHECK` enumerates the permitted values, so only it changes.

**Why a distinct origin rather than reusing `manual`.** The founding reading and a hand correction are
different facts. "The odometer read 76,632 when I bought it" is a purchase record; "I typed 80,705 in" is an
observation someone made later. Collapsing them loses the ability to answer *where did this car start* — and
that question is the whole basis of miles-since-purchase.

It also makes the invariant checkable: **exactly one `purchase` reading per vehicle**, at the vehicle's
purchase date and mileage. That is not enforced by a constraint (a partial unique index on
`(vehicle_id) WHERE origin = 'purchase'` would be possible, but it would fight the importer-shaped bulk paths
DEC-008 left open and the add-car flow is the only writer today). A test asserts it at creation.

## No new tables

The opening reading is an ordinary `mileage_readings` row. Recording "this vehicle started at X" as anything
other than a mileage reading would give current-mileage two sources to derive from, which is the defect class
DEC-002 exists to prevent.

## Rationale summary

| Change | Reason |
|---|---|
| `fill_level` nullable | It is descriptive now; the source data lacks it, and its old default silently meant "trust this figure" |
| `origin` gains `'purchase'` | The founding reading is a different fact from a correction; miles-since-purchase depends on telling them apart |
| No constraint on one-purchase-per-vehicle | The add-car flow is the only writer; a test asserts it rather than a partial index that would fight bulk paths |
| No new table | Current mileage must derive from exactly one source (DEC-002) |
