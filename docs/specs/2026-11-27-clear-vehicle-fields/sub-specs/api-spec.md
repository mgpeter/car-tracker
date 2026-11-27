# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-11-27-clear-vehicle-fields/spec.md

## Endpoints

### PATCH /api/vehicles/{registration}

**Purpose:** Partial edit of a vehicle's stored inputs. Unchanged except for one new optional property.

**Parameters:**

- `registration` (path) - the plate. Unchanged.
- Body: `UpdateVehicleRequest`, every property optional. New:
  - `clear` - `VehicleField[]`, optional. Each named field is set to null after the merge. Omitted or empty
    means nothing is cleared.

`null` on any other property still means "leave unchanged". That keeps every existing caller, including the
MCP settings tools, working without a change.

**Example:** clear the engine oil spec and set the oil capacity in one call:

```json
{
  "fluids": { "oilCapacityLitres": 4.8 },
  "clear": ["FluidsOilSpec"]
}
```

**Response:** `200` with the recomputed `VehicleSummary`, as now.

**Errors:**

- `400` ValidationProblem - a field both carries a value and is named in `clear`. Keyed by the field
  (`oilSpec`), message "Engine oil cannot be both set and cleared." Nothing is written.
- `400` ValidationProblem - an unknown `VehicleField` string fails model binding, as an unknown enum value does
  on `status` today.
- `400` - the existing validations (end before start, cover type length, negative price) are unchanged.
- `404` - vehicle not found or not owned, unchanged.

## Contract types

- `VehicleField` - new string enum, listed in the technical spec.
- `UpdateVehicleRequest.clear` - new optional `VehicleField[]`.

No response type changes. The OpenAPI diff is additive.
