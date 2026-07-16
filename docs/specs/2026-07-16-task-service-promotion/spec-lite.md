# Spec Summary (Lite)

Wire README §3.3's one-click promotion: a completed Workshop task becomes a `ServiceRecord` carrying its date,
mileage, garage and cost, and the task keeps the record's id. This is wiring, not modelling —
`MaintenanceTask.ServiceRecordId` already exists and round-trips read-only, with a `TaskEndpoints.cs` comment
that literally says "Promotion itself is M2."

The new `POST /tasks/{id}/promote` creates the record through `ServiceRecordFactory` (which writes the record, a
mileage reading and a mirrored expense in one transaction) rather than a second write path. Only Workshop tasks
that are Done and not already promoted can convert; DIY work is added as a DIY record directly and shows no
promote action.
