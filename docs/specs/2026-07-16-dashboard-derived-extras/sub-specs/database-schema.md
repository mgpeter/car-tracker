# Database Schema

This is the database schema implementation for the spec detailed in @docs/specs/2026-07-16-dashboard-derived-extras/spec.md

The whole spec is three items; **only the tank-range item touches the schema, and it is one nullable column.**
Service-interval templates are constants (no schema) and the units toggle is a localStorage preference (no
backend), so they do not appear here.

## Change: `vehicles.fuel_tank_capacity_litres`

A nullable capacity on the `FluidSpecs` owned block, mapped to a column on `vehicles` exactly as
`oil_capacity_litres` and `coolant_capacity_litres` already are:

```
ALTER TABLE vehicles
  ADD COLUMN fuel_tank_capacity_litres numeric(5,2) NULL;
```

Configured on `VehicleConfiguration` inside the `Fluids` owned-type block, beside `OilCapacityLitres` /
`CoolantCapacityLitres`:

```
fluids.Property(f => f.FuelTankCapacityLitres)
      .HasColumnType("numeric(5,2)")
      .HasColumnName("fuel_tank_capacity_litres");
```

`numeric(5,2)` covers any road-car tank (BT53's Freelander is ~59 L; larger vehicles run to a few hundred
litres) with a comfortable ceiling and two decimals for a precisely-quoted capacity.

Migration: `AddFuelTankCapacity`.

## Rationale

- **It is a fluid capacity, so it lives with the fluid capacities.** `FluidSpecs` is the "at the pump" reference
  block and already holds `OilCapacityLitres` and `CoolantCapacityLitres`; tank capacity is the same kind of
  number at the same place, mapped the same way. It is not a new entity and not a new table.
- **Nullable, and never defaulted.** Full-tank range needs a real tank size, and the app's rule is to show "—"
  rather than invent a figure — a null `MilesPerDay` on the day of purchase, no MPG on one fill. A hardcoded 59 L
  fallback would render a guess in the same typeface as the derived figures, so an unset capacity yields no range
  at all. BT53's ~59 L is entered as a real spec through the vehicle edit path; vehicles are never seeded, so
  nothing pre-populates it.
- **Read only by the derived-metrics service**, to compute full-tank range (`AverageMpg` × capacity-in-gallons)
  on read. The column stores capacity, a fact about the car; it never stores the range, which is derived per §1.

## What this does not change

- No change to `fuel_entries`. `FillLevel` stays descriptive (the fuel-basis spec), and no tank-*level* is
  tracked — which is why the derived figure is full-tank range, not "remaining".
- No table for service-interval templates: they are constants in code, by recommendation, until a per-vehicle
  editable schedule is wanted, which would be its own spec.
- The five defects' fixture is untouched: full-tank range is a new figure over a new input, not a recomputation
  of any existing one.
