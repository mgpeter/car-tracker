# Spec Requirements Document

> Spec: Settings — Reference-List Management
> Created: 2026-07-16
> Status: Complete

## Overview

Give the reference lists — garages, wash locations and expense categories — the edit, retire and re-home UI the
settings design shows, and round out check-definition management. Today `ReferenceWriter` only ever *creates*
these rows, on first use; nothing renames, retires or safely deletes them, and only `GET
/api/reference/expense-categories` reads one back. The design's guards (delete asks first, system rows are
locked, retiring keeps history) are the whole point: a reference list is pointed at by real records, so it
cannot be edited as if it were a free-standing list.

## User Stories

### Tidy the lists without breaking the records

As the owner, I want to rename a garage, retire one I no longer use and remove a category I created by mistake,
so that the pick-lists stay clean without silently rewriting the records that reference them.

The lists grow by accretion: `ReferenceWriter` deliberately does not normalise, so "K & P Motors" and "K&P
Motors" become two rows — an honest record of what was typed, and a judgement left "for the reference-list
editor in settings", which is this. Deciding two names are one place, correcting a typo, dropping a category
that was never right: all edits to a list that records point at. The design draws the guard rails —
`settings.dc.html`'s `delStub` toast reads "Deleting asks first if the category has entries — they must be
re-homed", `removeItem` reads "Removed — existing records keep their saved value", and Fuel and Purchase are
system-locked because "removing Fuel would break the fuel-to-expense mirror".

### Never orphan a record

As the owner, I want a category or garage that records reference to refuse silent deletion, so that a tidy-up can
never leave a row pointing at a name that no longer exists.

The columns that point at these lists look like free text and are foreign keys — `ServiceRecord.Garage`,
`MaintenanceTask.AssignedGarage`, `Vehicle.DefaultGarage`, `WashEntry.Location`, and every expense's `Category`.
Deleting a referenced row must either be blocked or require re-homing its records first; deleting the Fuel
category must be impossible, because the fuel-to-expense mirror resolves by that exact name and losing it is the
£163.16 gap re-opened from the other end.

### Retire a check without losing its history

As the owner, I want to retire a regular check rather than delete it, so that the check drops out of the active
18 while its logs survive.

`CheckDefinition.IsActive` exists precisely for this — its comment reads "Retire a check without deleting its
history." The design's check table has an Active column and says "Retiring a check keeps its history and drops it
from the 18", with "Guidance text edits on tap". The endpoint already supports all of it via `PATCH
/checks/definitions/{id}`; the gap is the settings UI that drives it, and making retire — not delete — the
obvious action.

## Spec Scope

1. **Reference-list read + CRUD API** — list, create, rename/edit and delete for garages and wash locations, and
   list + rename for expense categories, extending `ReferenceEndpoints` beyond its single GET.
2. **Referential-integrity guard on delete** — deleting a referenced garage/wash-location/category is refused
   with the count of records that reference it, or accepted only after re-homing; the write path counts the
   references before it removes the row.
3. **System-locked rows** — `ExpenseCategory.IsSystem` rows may be renamed for display but never deleted, and
   the Fuel category is never even offered for delete, because the mirror depends on its name.
4. **Reference-list settings panels** — editable lists for garages, wash locations and categories, with the
   delete-guard and locked-row affordances the design draws.
5. **Check-definition management panel** — the definitions table (Cadence / Days / Active / Order) with
   retire-via-`IsActive`, inline guidance edit and reorder, driving the `PATCH` that already exists.

## Out of Scope

- **Quick-add drag-reorder configurability.** Which quick-add buttons appear and in what order is a separate
  concern from the reference lists themselves; it is deferred to its own spec and not smuggled into the
  category editor.
- **Merging two reference rows into one.** "K & P Motors" and "K&P Motors" being the same place is a real need,
  but a merge re-points every referencing record and is a heavier operation than rename+retire; called out here,
  specced separately. This spec's rename edits one row's display; it does not fold two together.
- **New reference entities.** Garage, WashLocation and ExpenseCategory all exist with the columns they need
  (`Garage` even carries Contact/Address/Notes already). This is CRUD over existing tables; no new entity, and
  no schema change unless a genuinely missing column surfaces during execution.
- **Deleting a check definition with logs.** A real delete cascades its logs (`ChecksEndpoints` documents this);
  retiring via `IsActive` is the answer the design and the schema both point to, so the panel leads with retire
  and treats delete as the rare "should never have existed" case the endpoint already guards.

## Expected Deliverable

1. In settings on BT53, renaming a garage updates the pick-list everywhere it is offered, and deleting one that a
   service record references is refused with the reference count — nothing is orphaned.
2. The Fuel expense category cannot be deleted (it is system-locked and not offered), and a non-system category
   with no entries can be removed; a category with entries requires re-homing first.
3. The check-definitions panel retires a check by clearing `IsActive` — it drops from the active 18 and its logs
   survive — and edits a definition's guidance and order inline, all through the existing `PATCH`.
