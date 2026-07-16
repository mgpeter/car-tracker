# Spec Tasks

## Tasks

- [ ] 1. Confirm the Documents seam
  - [ ] 1.1 Write tests: a `Document` (`Type = Receipt`) can be created linked to an `ExpenseEntry` via
        `ExpenseEntryId`; deleting the expense nulls the link and keeps the file (SET NULL, per Documents)
  - [ ] 1.2 Verify the Documents upload path accepts an `expenseEntryId` link and image content-types; if it does
        not, that is a Documents-spec gap to raise, not to patch here
  - [ ] 1.3 Verify tests pass

- [ ] 2. Attach-on-create flow (v1, manual pre-fill)
  - [ ] 2.1 Write tests: create-expense-then-upload sequences correctly; "expense saved, photo failed" is a soft
        warning, not a lost expense; Fuel category is refused as on the normal sheet
  - [ ] 2.2 Add the camera-or-file input + preview to the add-expense sheet, routing bytes through the Documents
        upload path (no new upload code)
  - [ ] 2.3 Sequence the two calls; the photo is shown beside the form for manual entry of date/amount/vendor
  - [ ] 2.4 Use `GET /api/reference/expense-categories`; hide Fuel (mirror-only), matching the API's refusal
  - [ ] 2.5 Verify tests pass

- [ ] 3. Surface the receipt on the expense
  - [ ] 3.1 Write tests: an expense with a linked receipt `Document` shows an indicator/thumbnail; one without
        behaves exactly as today
  - [ ] 3.2 Receipt indicator/thumbnail on `ExpensesPage.tsx`; open the photo from the expense
  - [ ] 3.3 Axe sweep + coverage-guard exemptions
  - [ ] 3.4 Verify tests pass

- [ ] 4. Prove it end to end on BT53
  - [ ] 4.1 Log a real BT53 expense (a non-fuel receipt — e.g. a service part or a wash) with a photo; confirm
        one `ExpenseEntry` + one linked `Document` (`Type = Receipt`) and the photo shows on the expense
  - [ ] 4.2 Attempt a Fuel-category receipt and confirm it is refused (mirror-only); delete the expense and
        confirm the receipt `Document` survives with a null link
  - [ ] 4.3 Full suite, both builds, codegen gate; note v2 (OCR/MCP extraction) as follow-up; update
        roadmap/CLAUDE.md
