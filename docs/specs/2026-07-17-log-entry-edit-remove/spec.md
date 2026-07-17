# Spec Requirements Document

> Spec: Log Entry Edit & Remove
> Created: 2026-07-17
> Status: Planning

## Overview

Make every log's entries correctable and removable — from the UI, symmetrically, and with the shadow and
anomaly bookkeeping the write paths already established. Fuel and service already got `DELETE`/`PATCH`; this
spec finishes the job across mileage, tyres, wash and equipment, gives every add-only sheet an edit mode
reached by clicking the row, and closes three correctness traps sitting in the code it has to touch.

## User Stories

### Fixing a mistyped fill

As the owner, I want to correct a fill I entered wrong, so that a fat-fingered litres or odometer figure does
not permanently poison every figure derived from it.

Today a fill can be deleted but not edited — the expenses screen even tells the reader to "edit the fill" and
there is no endpoint behind those words. Clicking a fuel row opens the same sheet that added it, seeded with the
row's values. Saving re-derives its per-fill MPG, moves the fleet MPG on the dashboard, and updates the mirrored
expense amount in the same transaction — because the fill is the source and the expense is its shadow. The scan
re-runs, so correcting the litres that tripped an implausible-MPG flag clears the flag on save.

### Removing a wrong reading, and watching the number recompute

As the owner, I want to delete an entry I should not have logged, so that the odometer and the logs tell the
truth without a hand-written SQL edit.

A mileage reading, a tyre row, a wash — each can be removed with a footer Delete that first turns into a confirm
naming what else goes with it ("also removes the mirrored expense"). Deleting a reading that is not the latest
leaves current mileage untouched; deleting the latest falls the odometer back to the prior reading, because
current mileage is derived from the newest reading by date, never stored. Any `MileageReading` or mirrored
expense the row created dies with it: the shadow cannot outlive its source.

### The flag that was proving a now-deleted row closes itself

As the owner, I want a flag whose cause I just deleted to resolve itself, so that the integrity queue never
points at a row that no longer exists.

Deleting the fill behind an implausible-MPG flag re-runs the scan; the condition is gone, so the Open flag
auto-resolves to Corrected with a system note rather than orphaning. This is the behaviour the
anomaly-lifecycle-reconcile spec defines, and it is why that spec is a hard prerequisite here — without it, every
delete this spec adds is a way to strand a flag.

## Spec Scope

1. **Edit endpoints where missing** — `PATCH` for fuel and mileage; `PATCH` for tyres and wash. Each follows
   `ServiceEndpoints`/`FuelEndpoints`: resolve `reg`→id, write inside the execution strategy, re-run the scan,
   keep shadows in step, return the flags raised.
2. **Delete endpoints where missing** — `DELETE` for mileage, tyres, wash and equipment. Each cascades its
   shadow reading/expense and re-runs the scan.
3. **Edit/delete UI on every log** — add-only sheets become dual add/edit, opened by clicking a row; a ghost
   Delete with a two-step inline confirm sits in the footer. Tasks, issues and equipment (which already edit)
   gain the same Delete. `<DataTable>` gains an `onRowClick` prop only.
4. **Three correctness fixes in the blast radius** — the expense mirror-refusal must also block service-mirrored
   rows; expense `PATCH`/`DELETE` must re-run the scan; every multi-table edit must run inside the retrying
   execution strategy rather than a bare `SaveChangesAsync`.

## Out of Scope

- **Check logs.** They are append-only "I did this today" events with no list screen to edit from; a mislog is
  corrected by logging again, and the checks screen shows status, not a log history.
- **Budget.** Targets are already edited as a whole document via `PUT /targets`; there is no per-entry row to
  delete.
- **Vehicle DELETE.** Deleting a car is not a log operation and carries its own cascade and confirmation
  concerns; it belongs with the garage, not here.
- **New detectors or domain arithmetic.** This spec adds none — the five-defect fixture is untouched. Editing
  re-runs the *existing* three detectors; that is all.
- **Undo.** Delete is a two-step inline confirm, not a soft-delete with a restore window. Re-entering a removed
  row is the recovery path, consistent with every other write.
- **Auto-reconcile itself.** The retract-on-delete behaviour is owned by
  `2026-07-16-anomaly-lifecycle-reconcile`, a hard prerequisite, not re-specified here.

## Expected Deliverable

1. Clicking a fuel row opens its values in the sheet; changing the litres and saving moves that fill's MPG and
   the dashboard fleet MPG, and the mirrored expense amount follows — with no stored figure anywhere.
2. Deleting a mileage reading that is not the latest leaves current mileage unchanged; deleting the latest falls
   it back to the prior reading. Deleting a fill or service record removes its mirrored expense and reading too.
3. Editing an entry re-raises or clears anomalies correctly, and deleting the cause of an Open flag auto-resolves
   it to Corrected (via the reconcile prerequisite) rather than orphaning it.
4. A service-mirrored expense row is refused for direct edit and delete, pointing the owner at the service
   record — the same guard fuel-mirrored rows already have.
