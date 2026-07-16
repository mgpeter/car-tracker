# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-16-receipt-photo-capture/spec.md

## Technical Requirements

### This is a flow over two existing things, not new storage

- The receipt is a `Document` and the expense is an `ExpenseEntry`; both entities exist. `Document` already
  carries the `ExpenseEntryId` link, "severed (SET NULL), never cascaded" (`Document.cs`). The Documents spec
  (concurrent) owns upload, storage on the mounted volume (DEC-005), `Sha256` dedupe and the entity. **This spec
  adds no schema and no storage** — it wires the add-expense flow to Documents' upload and links the result.
- If the Documents spec is not yet landed when this is built, that is a hard dependency, not a soft one: this
  flow's photo half *is* the Documents upload. Build order puts Documents first.

### Two calls, not a combined endpoint — and why

- The flow is: **create the expense (`POST /api/expenses`), then upload the photo as a `Document` linked to the
  new `ExpenseEntry`** (the Documents upload endpoint, carrying the `expenseEntryId`). Two existing calls, no
  new endpoint.
- A combined "create expense + attach photo" endpoint was considered and rejected: it would duplicate the
  expense-create validation (category rules, the Fuel refusal) that `ExpenseEndpoints.cs` already owns, or
  couple document-upload multipart handling into the expense endpoint. The two-call order is also honest about
  failure — if the upload fails after the expense is created, the result is an expense with no receipt, which is
  a valid, recoverable state (attach the photo later via the Documents attach flow). The reverse (a receipt with
  no expense) never arises because the expense is created first.
- The front end sequences the two calls and treats "expense saved, photo failed" as a soft warning, not a lost
  expense. No distributed transaction is needed for a photo attachment.

### The expense is an ordinary expense — category rules hold

- The receipt flow submits the same `POST /api/expenses` request as the normal sheet and inherits its rules:
  the enterable categories come from `GET /api/reference/expense-categories`, and **Fuel is `IsMirrorOnly` and
  refused** (`ReferenceEndpoints.cs`, `ExpenseEndpoints.cs`: "Fuel-category entries are refused — they come from
  the fuel log"). The receipt UI hides Fuel exactly as the expense form does, "so the UI hides exactly what the
  API rejects". A receipt cannot become a second path to a typed fuel figure.
- `Document.Type = Receipt` (the enum member exists), `Title` defaulting to something like "Receipt — {vendor}
  {date}", `DocumentDate` = the expense date.

### v1 pre-fill is manual; v2 is extraction

- **v1**: the captured/uploaded image is shown beside the add-expense form; the owner reads date, amount and
  vendor and types them. The pre-fill is the human's, with the evidence in view. No OCR, no model call — this is
  what makes v1 near-zero-risk on top of Documents.
- **v2 (out of scope here, noted)**: an extraction step proposes the three fields. Two honest options, to be
  chosen when v2 is specced: a **server-side OCR/vision service** (an external dependency and key), or the **MCP
  assistant reading the attached photo** (reuses the MCP surface — the assistant already can log expenses;
  reading a receipt it can see is a natural extension). Either way the proposal is *confirmed* by the owner
  before save — never silently entered. v1's UI is built so v2 slots in as "proposed values pre-filling the same
  fields", not a rework.

### Capture / upload UI

- The add-expense sheet gains a camera-or-file input: on mobile, `<input capture>` for a direct snap; on
  desktop, a file picker. The bytes go through the Documents upload path (multipart), not a new one. Image
  types only; size and content-type are the Documents feature's validation to enforce.
- Behind TanStack Query like every write; a preview thumbnail of the chosen photo before save. Every new
  component axe-swept or exempted (`coverage.test.ts`); `usePlate()` is not relevant here (no plate on the
  sheet).
- The expense's existing screen (`ExpensesPage.tsx`) shows a receipt indicator / thumbnail where a linked
  `Document` exists — surfacing that the evidence is attached.

## Verification

- **Attach-on-create**: creating an expense with a photo produces one `ExpenseEntry` and one `Document`
  (`Type = Receipt`, `ExpenseEntryId` = the new expense), verifiable in the DB and on the expense screen.
- **Category rules**: the receipt flow refuses a Fuel-category expense with the same message the expense
  endpoint gives; the mirror-only categories are hidden in the UI.
- **Link survives**: deleting the expense sets the `Document.ExpenseEntryId` to null and leaves the file — assert
  the `Document` row and file remain (the Documents SET NULL behaviour, tested through this flow).
- **Failure**: an upload failure after expense creation leaves the expense saved and shows a soft warning; the
  expense is not rolled back and is not orphaned of anything it needs.
- **Contract**: no new endpoint, so no contract change from this spec beyond what Documents adds; staleness gate
  green.

## External Dependencies (Conditional)

- **None for v1.** v1 is entirely the existing `POST /api/expenses` + the Documents upload path + a capture
  input. No OCR service, no new package, no key.
- **v2 only (not this spec):** if v2 takes the server-side OCR route it needs a vision/OCR service and key; if
  it takes the MCP route it needs the MCP server (its own spec) and no new external dependency. That choice is
  deferred to the v2 spec.
