# Spec Summary (Lite)

Give garages, wash locations and expense categories the edit/retire/re-home UI the settings design shows, and
round out check-definition management. `ReferenceWriter` only ever *creates* these rows on first use, and only
`GET /api/reference/expense-categories` reads one back — so there is no way to rename a garage, retire an unused
one, or safely remove a mistaken category. The design's guards are the point: these lists are foreign keys
(`ServiceRecord.Garage`, every expense's `Category`, and more), so a delete must be blocked or require re-homing,
and the Fuel category is system-locked because the fuel-to-expense mirror resolves by its exact name.

Check-definition management is mostly API-ready — `PATCH /checks/definitions/{id}` already edits name, cadence,
interval, guidance, order and `IsActive` — so that half is the settings panel that drives retire (via `IsActive`,
keeping logs), inline guidance edits and reorder. No new entities; no schema change expected.
