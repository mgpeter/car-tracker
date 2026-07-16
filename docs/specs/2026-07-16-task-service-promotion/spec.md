# Spec Requirements Document

> Spec: Promote a Task to a Service Record
> Created: 2026-07-16
> Status: Planning

## Overview

Wire the one-click promotion README §3.3 already names — turning a completed Workshop task into a
`ServiceRecord` that carries its date, mileage, garage and cost and links back to the task. The link column
already exists and round-trips read-only: `MaintenanceTask.ServiceRecordId` is there, and the comment beside it
in `TaskEndpoints.cs` reads "Set when a task was promoted to a service record. **Promotion itself is M2.**" This
spec is that M2 — wiring, not modelling.

## User Stories

### The job that becomes a record

As the owner, I want to convert a finished Workshop task into a service record in one click, so that the work I
paid a garage for lands in the history without re-typing its date, mileage, garage and cost.

The workbook keeps the Workshop To-Do and the Service History as two sheets, and a completed job has to be
copied by hand from one to the other — which is how a £603.99 cambelt ends up on the to-do list, done, and
never in the history the next-service date derives from. The design already shows both ends of this: the
`tasks.dc.html` `convert` toast reads "Service record created from task — date, mileage, garage and cost carried
over. Review in Service history", and `service-history.dc.html` shows the receiving row labelled "Converted from
workshop task …". The task stays where it is, marked done, now carrying the id of the record it became — so the
history gains a row, the to-do list keeps its provenance, and nothing is entered twice.

### Only the work a garage did

As the owner, I want promotion offered only on Workshop tasks, so that a job I did in the driveway does not
masquerade as a garage service.

A `MaintenanceTask.Kind` is DIY or Workshop, and only Workshop work is a service. DIY work is added as a DIY
record directly, so the promote action is absent on a DIY task rather than disabled — an action you can see but
never use is a worse answer than one that is not there.

## Spec Scope

1. **Promote endpoint** — `POST /api/vehicles/{registration}/tasks/{id}/promote`, creating a `ServiceRecord`
   from a completed Workshop task through `ServiceRecordFactory` and stamping `ServiceRecordId` back on the task.
2. **Carry the fields across** — the task's completed date, its odometer, its assigned garage and its estimated
   cost become the record's `ServiceDate`, `Mileage`, `Garage` and `Cost`; its title and description become the
   `WorkDone`.
3. **Guard the preconditions** — only a `Workshop` task, only one already `Done`, only one not already promoted;
   each refusal is a 400 or 409 the UI can explain, not a silent no-op.
4. **The action on the tasks screen** — a "Convert to service record" affordance on a done Workshop task, taking
   the owner to the new record, with a matching "Converted from workshop task" note on the service side.

## Out of Scope

- **Converting a DIY task.** DIY work is added as a DIY record directly; a promote path for it would be a
  different flow (no garage, often no cost) and the design does not show one. Excluded because the button's
  absence on DIY tasks *is* the design.
- **Promotion over MCP `complete_task`.** An agent completing a task and spawning its service record is the MCP
  spec's concern — that surface owns `complete_task` and will call the same domain path. This spec is the web
  action; duplicating the tool contract here would pre-empt a decision that is not ours.
- **Re-implementing the service-write transaction.** The record, its mileage reading and its mirrored expense
  are `ServiceRecordFactory`'s job and already tested; promotion calls it, it does not re-derive it. Writing a
  second three-row transaction is exactly the divergence the factory exists to prevent.
- **Un-promoting.** Clearing `ServiceRecordId` and deleting the record it points at is a reversal with its own
  questions (does the expense go too?); if it is ever wanted it is its own decision. Promotion is one-way here.

## Expected Deliverable

1. On BT53, a done Workshop task with a cost and a garage promotes in one click to a service record carrying its
   date, mileage, garage and cost; the record appears on the service history screen and the task now shows it is
   linked.
2. The mirrored expense and mileage reading appear too — because promotion goes through `ServiceRecordFactory`,
   the £603.99 moves the spend rollup and the odometer reading is written, with no extra code path.
3. Promoting an already-promoted task, a task that is not Done, or a DIY task is refused with a clear reason, and
   the tasks screen offers the action only where it can succeed.
