# Spec Requirements Document

> Spec: Starter-Check Selection on Add-Car
> Created: 2026-07-19
> Status: Complete (2026-07-19)

## Overview

When someone adds a car and chooses the generic starter set, present the fifteen checks it contains and let
them toggle each one in or out before the car is created — so the founding set matches the car rather than being
an all-or-nothing fifteen. Today the choice is binary (all fifteen, or none and start from an empty checks
screen), and the fifteen are never shown at all: they live only in the server's `CheckTemplate.Generic`.

## User Stories

### See the set before you accept it

As the owner adding a car, I want to see the fifteen starter checks and drop the ones that do not apply to this
car, so that its checks screen starts correct instead of carrying items I then have to go and delete.

The generic set is genuinely generic — "Air-con run, 10 minutes" is noise on a car with no air-con, "Power
steering fluid" on an electric-assist rack. Today you either take all fifteen and prune them afterward through
the definitions editor, or take none and build the whole list by hand. Neither is the common case, which is
"mostly these, minus two". Showing the set as a toggle list, defaulted all-on, makes that the one-click path: it
is the same fifteen the server would have applied, laid open so a deselection is a click at the moment of
creation rather than a chore afterward.

### Nothing changes if you do not touch it

As an owner who trusts the defaults, I want to ignore the list entirely and get exactly the fifteen I get today,
so that the new control costs nothing to anyone who does not want it.

The list defaults to every check selected. Add a car without opening or touching it and the created set is
byte-for-byte today's fifteen — the feature is additive, and the "None — I will add my own" option still exists
for the from-scratch case.

## Spec Scope

1. **Expose the generic template** — a read endpoint returns the fifteen `CheckTemplate.Generic` items (name,
   cadence label, interval, guidance) so the front-end can render them; the server stays the single source of
   truth and the list cannot drift from what create actually applies.
2. **Inline toggle list on the add-vehicle sheet** — when "Generic starter set" is the chosen source, the
   fifteen checks expand below the select as labelled checkboxes, defaulted all-on, with a live "N of 15"
   count and each check's cadence shown read-only beside it.
3. **Selected subset on create** — the create request carries the chosen check names; `VehicleFactory` filters
   the generic template to that subset, so exactly the kept checks become the vehicle's `CheckDefinition`s.
4. **Deselect-all equals None** — clearing every check under the generic source creates a car with no checks,
   the same outcome as choosing "None", with no separate code path.

## Out of Scope

- **Editing cadence or guidance at add time.** The toggle list shows each check's cadence read-only; changing
  it, or a check's guidance, stays in the existing per-vehicle definitions editor, which owns the set the moment
  the car lands.
- **Adding custom checks during add-car.** BT53's own K-series and VCU checks are exactly why the generic set is
  fifteen and not eighteen — car-specific checks are added afterward through the definitions CRUD, unchanged by
  this spec.
- **Copy-from-vehicle.** `CheckSource.CopyFromVehicle` exists in the enum but is not surfaced in the UI; giving
  it the same toggle treatment is a later spec, not this one. This spec touches only the generic set.
- **Reordering the checks.** The set keeps the template's fixed display order (weekly, then monthly, then long
  intervals); reordering is a definitions-editor concern, not an add-time one.

## Expected Deliverable

1. On the add-vehicle sheet, choosing "Generic starter set" reveals the fifteen checks as toggles defaulted
   all-on with an "N of 15" count; deselecting some and creating the car yields exactly the kept checks on that
   car's checks screen, in template order.
2. Adding a car without touching the list creates the same fifteen checks as today — verified byte-for-byte
   against the current behaviour — and "None — I will add my own" still creates a car with none.
3. Deselecting every check under the generic source creates a car with no checks, identical to choosing "None".
