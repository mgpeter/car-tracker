# Spec Requirements Document

> Spec: Receipt Photo Capture — photo to pre-filled expense
> Created: 2026-07-16
> Status: Planning

## Overview

Capture or upload a receipt photo that becomes an expense's attached evidence and pre-fills the expense's date,
amount and vendor. Built on the Documents feature (its own concurrent spec): the photo is a `Document` linked to
the created `ExpenseEntry`. v1 attaches the photo and the owner types the fields; automatic extraction (OCR) is
a deliberate v2, so v1 ships on Documents with near-zero risk.

## User Stories

### The photo is the receipt

As the owner, I want to snap the garage receipt and have it attached to the expense I'm logging, so that the
paper can go in the bin and the evidence lives with the figure.

The `Document` entity already models this exactly — `Type`, `Title`, `FilePath`, `ContentType`, `SizeBytes`,
`Sha256`, and an `ExpenseEntryId` link "severed (SET NULL), never cascaded" (`Document.cs`). The Documents spec
provides upload and the entity; this spec is the flow that ties a receipt photo to the *expense being created*,
so the attachment and the expense are one action, not two screens the owner must remember to connect.

### The photo fills the form (v1: the human reads it; v2: the machine does)

As the owner, I want the receipt to pre-fill date, amount and vendor, so that logging an expense from a receipt
is confirm-and-save rather than transcribe-from-scratch.

In v1 the pre-fill is honest and manual: the photo is on screen beside the form, the owner reads it and types
the three fields, and the photo is saved as the evidence. This ships on top of Documents with essentially no new
risk. v2 adds extraction — an OCR/vision step (a server-side OCR service, or the MCP assistant reading the
attached photo) that proposes the three fields for the owner to confirm. v1 does not promise automatic
extraction, because a wrong auto-filled amount silently entered is worse than a field the owner typed.

### A receipt expense is just an expense

As the owner, I want a receipt-photo expense to obey the same category rules as any other, so that it can't
become a second way to type a fuel figure and reopen the £163.16 gap.

The expense created here is an ordinary `ExpenseEntry` and respects the reference-category rules: Fuel is
mirror-only (`ReferenceEndpoints.cs`: "a typed fuel expense is the workbook's lumped 'fuel to date' row, and
that is the £163.16 gap"), offered via `GET /api/reference/expense-categories` with `IsMirrorOnly`. The receipt
flow surfaces the same enterable categories and refuses Fuel exactly as the expense form does — a fuel receipt's
figures already come from the fuel log.

## Spec Scope

1. **Attach-on-create** — logging an expense can carry a receipt photo that, on save, becomes a `Document`
   (`Type = Receipt`) linked to the new `ExpenseEntry` via `ExpenseEntryId`, in one flow.
2. **Photo capture / upload** — a camera-or-file input on the add-expense sheet (mobile capture, desktop
   upload), using the Documents feature's upload path — this spec does not re-spec upload.
3. **Manual pre-fill (v1)** — the photo shows beside the form; the owner enters date, amount and vendor with
   the receipt in view. The three fields are ordinary expense fields; the photo is the evidence.
4. **Category rules preserved** — the flow uses the reference-categories endpoint's enterable list and refuses
   Fuel (mirror-only), like the normal expense write.
5. **The link survives the expense** — deleting the expense leaves the `Document` (its `ExpenseEntryId` set
   null), per the Documents design ("the MOT certificate outlives the service row"); the photo is not lost with
   the row.

## Out of Scope

- **Automatic OCR / vision extraction (this is v2).** Reading date/amount/vendor off the image automatically is
  the interesting-but-risky half. Noted here as the planned v2 (a server-side OCR service or the MCP assistant
  reading the photo), deliberately not in v1 — v1 ships on Documents with near-zero risk, and an auto-filled
  wrong amount entered silently is a worse failure than a typed field.
- **Barcode scanning.** README §8 pairs "barcode/receipt photo capture", but a barcode (a product/VIN code) is a
  different capability from a receipt photo (evidence + figures) and earns its own spec; fusing them would blur
  two unrelated inputs.
- **Re-spec'ing upload or the `Document` entity.** Upload, storage on the mounted volume (DEC-005), `Sha256`
  dedupe and the entity are the Documents spec's; this spec references and builds on them. Duplicating them
  here would create two upload paths to keep in step.
- **Non-expense receipts.** A receipt attached to a service record or an issue is the Documents feature's
  general attach flow. This spec is specifically the receipt → *expense* pre-fill.
- **Fuel receipts as a create path.** Fuel is mirror-only; a fuel receipt's figures come from the fuel log, not
  a typed expense. The flow refuses Fuel like the expense form does.

## Expected Deliverable

1. On the add-expense sheet, capturing/uploading a receipt photo and entering date, amount and vendor creates
   the `ExpenseEntry` and a `Document` (`Type = Receipt`) linked to it, in one save; the photo is visible on the
   expense afterwards.
2. The receipt flow offers the same enterable categories as the expense form and refuses Fuel (mirror-only),
   so it cannot reopen the £163.16 fuel-total gap.
3. Deleting the expense leaves the receipt `Document` in place (its `ExpenseEntryId` nulled), and an expense
   with no receipt behaves exactly as today.
