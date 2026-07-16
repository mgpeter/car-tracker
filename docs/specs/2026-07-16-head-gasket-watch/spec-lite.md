# Spec Summary (Lite)

Let an `Issue` name the `CheckDefinition`s that are its early-warning, so its Resolved state can be shown as
contingent on those checks staying current — the head-gasket watch the design shows everywhere and the schema
models nowhere (a `VehicleCard.tsx` comment already says "nothing models which checks are the head-gasket
watch").

The issues screen shows a resolved-but-watched issue's live check status and flags when a watched check lapses;
the dashboard names the lapsed watch above generic overdue checks. It never auto-reopens the issue — it
surfaces the contingency and leaves the decision to the owner, the same principle the anomaly lifecycle
respects.
