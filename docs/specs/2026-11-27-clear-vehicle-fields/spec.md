# Spec Requirements Document

> Spec: Clear vehicle fields
> Created: 2026-11-27
> Status: Complete (2026-11-27) - shipped in `0.30.0`; one by-hand PATCH with a real Auth0 JWT is still owed
> (task 2.5)

## Overview

Let the owner clear a stored vehicle value (a fluid spec, a tyre pressure, an insurance figure, the purchase
price) instead of only overwriting it. Today `PATCH /api/vehicles/{reg}` merges `patch.X ?? stored.X` on every
field, so a blank leaves the old value and every editor on the Vehicle screen says so in its hint.

## User Stories

### Remove a value that was wrong

As an owner, I want to blank a field on the Vehicle screen and save, so that a value I entered by mistake (a
laden tyre pressure the manual never gave, a cabin filter part number for the wrong model) stops showing.

Today the sheet accepts the blank, toasts "Saved" and reopens with the old value, because the server reads the
`null` as "leave unchanged". The only way out is to type a different wrong value.

### Remove a value that no longer applies

As an owner, I want to clear insurance or breakdown details when a policy lapses without renewal, and clear the
purchase price if it was never really known, so that the dashboard and spend figures stop treating stale inputs
as facts.

Clearing the purchase price must take the mirrored Purchase expense with it, so total outlay and cost per mile
move in the same save.

## Spec Scope

1. **Explicit clear list on the vehicle patch** - `VehiclePatch` and `UpdateVehicleRequest` gain a `clear` list
   of `VehicleField` values; each named field is set to null after the merge.
2. **`VehicleField` contract enum** - one member per nullable stored input on the vehicle, so the client reads
   the clearable set from the generated contract rather than hand-typing field paths.
3. **Conflict rule** - a field that is both given a value and named in `clear` is a 400 on that field, not a
   silent pick of one.
4. **Vehicle screen editors send clears** - blanking a field that had a stored value sends it in `clear`, and the
   field hint changes from "blank leaves the stored value" to saying blank clears it.

## Out of Scope

- MCP and chat write tools. They keep "null means unchanged" and gain no clear parameter in this spec. The
  domain supports it, so a later spec can add it along with the draft-card wording the chat confirmation needs.
- `Status` and `IsDefault`. Both are non-nullable lifecycle fields driven from the garage, and the
  `isDefault` 23505 trap stays where it is.
- Create-only facts (make, model, year, purchase date, purchase mileage). They are not on the patch at all.
- Clearing a whole block (all of insurance) in one entry. Clears are per field.
- A database change. Every clearable column is already nullable.

## Expected Deliverable

1. On the Vehicle screen, blanking "Engine oil" in the Fluids & parts sheet and saving leaves the Engine oil row
   showing as not set after the sheet closes and after a reload, with every other fluid unchanged.
2. Blanking the purchase price and saving removes the Purchase row from the expense log and drops the dashboard's
   "including the car itself" clause, in the same save.
3. A PATCH that both sets and clears `fluids.oilSpec` answers 400 with an error on that field and writes nothing.

## Spec Documentation

- Technical Specification: @docs/specs/2026-11-27-clear-vehicle-fields/sub-specs/technical-spec.md
- API Specification: @docs/specs/2026-11-27-clear-vehicle-fields/sub-specs/api-spec.md
