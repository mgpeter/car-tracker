# Spec Tasks

> Status: Complete (2026-07-19). 395 front-end tests; validation inline on all sheets, date defaults +
> quick-fill on every date field, comboboxes on every place field. Verified in Chrome (light + dark).

## Tasks

- [x] 1. Validation plumbing (the shared seam)
  - [x] 1.1 Tests for `lib/formErrors.ts` (server `errors` → fields; unmatched/collection/dotted → `_`; no-`errors` → message; case-insensitive)
  - [x] 1.2 `api/client.ts` captures the ProblemDetails `errors` map; `ApiError` http variant widened
  - [x] 1.3 `lib/formErrors.ts` (`FieldErrors`, `reportApiError`, `fieldError`, `formError`)
  - [x] 1.4 `Field` `error` prop (aria-invalid + message) + invalid CSS (`--due` border, `--due-wash` ring, `.hint.err`)
  - [x] 1.5 `Field`/`Sheet` tests for the error prop

- [x] 2. Roll inline validation across all sheets
  - [x] 2.1 Representative sheet tests assert inline field errors + a blocked call on missing required fields
  - [x] 2.2 Daily record sheets: fuel, expense, service, mileage, tyres, wash, tasks, issues, equipment
  - [x] 2.3 Remaining: budget, checks-log, data-integrity, add-vehicle, settings reference-lists / check-definitions / fuel-tank / statutory
  - [x] 2.4 All sheet tests pass

- [x] 3. Date defaults + quick-fill
  - [x] 3.1 Tests for `lib/date.ts` (`todayIso`, `addMonths`, `addYears`) and `components/DateQuickFill.tsx`
  - [x] 3.2 `lib/date.ts`; `addMonths` lifted out of `ServiceHistoryPage` and re-imported
  - [x] 3.3 Primary date field defaults to `todayIso()` on add across all date sheets
  - [x] 3.4 `DateQuickFill` wired to service next-due and task target date
  - [x] 3.5 Tests pass

- [x] 4. Combobox + suggestion sources
  - [x] 4.1 Tests for `components/Combobox.tsx`, `lib/recentValues.ts`, `api/reference.ts`
  - [x] 4.2 `components/Combobox.tsx`
  - [x] 4.3 `api/reference.ts` (`useReferenceList` / `useReferenceSuggestions`)
  - [x] 4.4 `lib/recentValues.ts`
  - [x] 4.5 axe coverage (own tests) verified

- [x] 5. Wire comboboxes into every place field
  - [x] 5.1 Reference-backed: service `garage`, task `assignedGarage`, wash `location`
  - [x] 5.2 History-derived: fuel `station`, expense `vendor`, tyre `location`/`tool`, equipment `sourceVendor`
  - [x] 5.3 Sheet tests pass with the combobox in place

- [x] 6. Full verification
  - [x] 6.1 `npm run test` + `npm run build` + `tsc -b` + tokens/coverage green (395 tests)
  - [x] 6.2 Chrome-verified: validation (inline errors, no "Bad Request"), date defaults + quick-fill, comboboxes (recent on focus, filter, free-type) in light and dark
  - [x] 6.3 roadmap.md / CLAUDE.md updated
