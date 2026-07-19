# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-19-starter-check-selection/spec.md

## Technical Requirements

### Server — expose the template, filter on create

- **One source of truth.** `CheckTemplate.Generic` (in `CarTracker.Domain`) stays the only definition of the
  fifteen. Both the new read endpoint and `VehicleFactory` read *that* list — the endpoint returns it, and the
  factory filters `CheckTemplate.For(0)` by the requested names. The front-end never hardcodes the fifteen, so
  the list it shows cannot drift from the list create applies (the project's central constraint, at the
  add-car seam).
- **Read endpoint** — a vehicle-independent GET returning the fifteen items as `{ name, cadenceLabel,
  intervalDays, guidance }`, in template (`DisplayOrder`) order. It sits with the other reference lookups
  (`/api/reference/…`), behind the API key like everything except `/api/meta`. See api-spec.md.
- **Create request gains an optional selection.** `CreateVehicleRequest` gets
  `IReadOnlyList<string>? SelectedCheckNames = null`. Semantics:
  - `null` → today's behaviour: the whole generic set (backward compatible; existing callers and the MCP path
    are unaffected).
  - non-null and `CheckSource == GenericStarterSet` → the factory keeps only template items whose `Name` is in
    the list (an ordinal set intersection, preserving template order). An empty list therefore creates **no**
    checks — the deselect-all case, equivalent to `None`.
  - ignored when `CheckSource` is `None` or `CopyFromVehicle` (those sources do not draw from the template).
- **Factory change.** `VehicleFactory.CreateAsync` takes the selection through to `ResolveChecksAsync`; the
  `GenericStarterSet` branch becomes `CheckTemplate.For(0)` filtered by the selected names (all when null). No
  change to the transaction, the mileage-reading founding row, or the execution-strategy wrapping.
- **Unknown names are dropped, not rejected.** Filtering by set intersection means a name not in the template
  simply does not match. The names can only originate from the endpoint the UI fetched, so in practice they
  always match; intersecting is the safe, non-throwing behaviour rather than a 400 on a stale name.

### Front-end — the inline toggle list

- **`AddVehicleSheet` fetches the template** when the sheet is open, via a typed client call to the new
  endpoint (TanStack Query, same pattern as the reference-suggestion hooks). Cache it — it never changes within
  a session.
- **Reveal on source.** The toggle list renders directly under the existing "Regular checks" `<Select>` only
  when `draft.checkSource === 'GenericStarterSet'`. Choosing "None" hides it (and the selection is irrelevant);
  switching back reveals it with its previous selection intact.
- **Default all-on.** When the template loads, every check starts selected. Track the selection as a `Set<string>`
  of names in the draft (or a sibling piece of sheet state); toggling a checkbox adds/removes its name.
- **Each row** shows a real `<input type="checkbox">` with a label (the check name) and the cadence label shown
  read-only beside it (e.g. "Weekly", "Quarterly · 1.6 mm legal" using the guidance where present). Real
  controls, greyscale-legible, keyboard-operable — the accessibility bar the rest of the app holds to.
- **Live count.** A header on the list reads "N of 15" and updates as checks toggle, mirroring the
  `<TableControls>` count idiom.
- **Submit.** The create body includes `selectedCheckNames` only when the source is `GenericStarterSet`:
  - all fifteen still selected → send `null` (or omit), so the untouched path is byte-for-byte today's request
    and the byte-for-byte-identical server behaviour is preserved and testable;
  - a strict subset → send the selected names;
  - none selected → send `[]`, which the server reads as no checks.
- **Contract-first.** Regenerate the committed OpenAPI document and the typed front-end client after the server
  changes; the new endpoint and the new request field arrive as an additive contract diff, and the client is
  generated off it, never hand-written.

### Testing

- **Domain** — `VehicleFactory` (or its check-resolution) tests: null selection yields all fifteen (the
  existing assertion, unchanged); a subset yields exactly those in template order; an empty list yields none;
  a selection is ignored for `None`/`CopyFromVehicle`.
- **WebApi** — create with `selectedCheckNames` produces a vehicle whose active definitions are exactly the
  subset; omitting the field reproduces today's fifteen; the contract diff is additive.
- **Front-end** — `AddVehicleSheet` tests: picking "Generic starter set" reveals the fifteen (mock the template
  endpoint), the count reads "15 of 15", deselecting one sends the remaining fourteen names, "None" hides the
  list and sends no selection, and an all-selected submit sends no `selectedCheckNames` (the untouched path).

## Files Touched

- `src/CarTracker.Domain/VehicleFactory.cs` — thread the selection into `ResolveChecksAsync`, filter the
  generic branch.
- `src/CarTracker.WebApi/Endpoints/VehicleEndpoints.cs` — `CreateVehicleRequest.SelectedCheckNames`; pass it to
  the factory.
- A reference/checks endpoint file (e.g. `ReferenceEndpoints.cs`) — the new GET returning `CheckTemplate.Generic`.
- `api-contract/v1.json` + `src/CarTracker.WebApp/src/api/generated/*` — regenerated.
- `src/CarTracker.WebApp/src/screens/AddVehicleSheet.tsx` — fetch, reveal, toggle list, count, submit shape.
- `src/CarTracker.WebApp/src/api/…` — a small typed hook for the template, beside the existing reference hooks.

## External Dependencies (Conditional)

None. No new libraries — this reuses TanStack Query, the existing typed-client codegen, the `Sheet`/`Field`
primitives and the reference-endpoint pattern already in the codebase.
