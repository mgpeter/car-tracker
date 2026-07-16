# Spec Summary (Lite)

Capture or upload a receipt photo while logging an expense: on save it becomes a `Document` (`Type = Receipt`)
linked to the new `ExpenseEntry` via the `ExpenseEntryId` the `Document` entity already carries. Built on the
concurrent Documents spec (upload, storage, the entity) — this spec is the receipt → expense flow, not a second
upload path.

v1 is honest and low-risk: the photo shows beside the form and the owner types date, amount and vendor with the
receipt in view. Automatic OCR/vision extraction is a deliberate v2 (a server-side OCR step, or the MCP
assistant reading the photo) — v1 does not promise it, because a silently auto-filled wrong amount is worse than
a typed field. The expense is an ordinary `ExpenseEntry` and respects the category rules: Fuel is mirror-only
and refused, so a receipt cannot reopen the £163.16 gap.
