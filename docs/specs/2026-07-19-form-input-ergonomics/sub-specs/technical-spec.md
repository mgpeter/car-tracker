# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-07-19-form-input-ergonomics/spec.md

## Technical Requirements

### Validation plumbing

- **`api/client.ts`** — in the non-ok branch of `request()`, read the ProblemDetails `errors` object alongside
  `detail`. Extend `ApiError`'s http variant to `{ kind: 'http'; status: number; message: string; errors?:
  Record<string, string[]> }`. `ApiFailure` (`api/queries.ts`) already carries `.error`, so it transparently
  carries the map. The empty-body / non-JSON handling added earlier stays intact.
- **`lib/formErrors.ts`** (new):
  - `type FieldErrors = Record<string, string[]>` — keys are lowercased; the reserved key `_` holds
    form-level messages for the banner.
  - `reportApiError(err: unknown, fieldKeys: readonly string[]): FieldErrors` — if `err` is an `ApiFailure`
    with an `errors` map, lowercase each key; keys present in `fieldKeys` are kept under that key, all other
    keys' messages are appended to `_` (so collection/dotted keys like `Targets`, `Insurance.PeriodEnd`, and
    framework-generated 400 keys are surfaced, never dropped). With no `errors` map, return `{ _: [message] }`.
  - This absorbs the server's inconsistent key casing (`nameof` PascalCase vs hardcoded lowercase).
- **`components/Sheet.tsx` `Field`** — add `error?: string`. When present: the render-prop hands the child
  `aria-invalid` and points `aria-describedby` at the error message's id; render the message as
  `<span className="hint err" role="alert" id=…>`. `error` supersedes `hint` for describedby/message when both
  are set. Purely additive — existing `hint`-only call sites are unchanged.
- **`styles/components.css`** — invalid state styling using semantic tokens only (no raw hex/palette; enforced
  by `tokens.test`):
  - `.field :is(input, select, textarea)[aria-invalid='true'] { border-color: var(--due); box-shadow: 0 0 0 3px var(--due-wash); }`
  - `.field .hint.err { color: var(--due); }`
- **Per-sheet retrofit (~17 sheets)** — replace the duplicated `useState<string | null>(null)` + inline red
  `role="alert"` banner with a `FieldErrors` state. Add a per-sheet `validate()` returning `FieldErrors` for
  required/format rules (mirroring `AddVehicleSheet.validate`), with plain messages. `submit()` runs
  `validate()`; if clean, mutates. The mutation `onError` calls `reportApiError(err, FIELD_KEYS)`. Each
  `<Field>` gains `error={errors['<key>']?.[0]}`. Keep one bottom banner rendering `errors['_']`. Sheet value
  bags already key by wire name, so field↔error alignment is direct.
  - Required-rule sources (from the DTOs / server validators): fuel `litres`/`pricePerLitre`/`mileage` > 0;
    service `type` non-blank, `mileage` present; expense `amount` > 0, `category` chosen; mileage `mileage` >
    0; task `title`; issue `title`; equipment `name`; check-definition `name`/`intervalDays`; reference `name`.

### Dates

- **`lib/date.ts`** (new) — `todayIso(): string` (local `YYYY-MM-DD`), `addMonths(iso, n)`, `addYears(iso, n)`.
  Lift the existing `addMonths` out of `ServiceHistoryPage.tsx` and re-import it there (single copy).
- **Defaults** — each sheet's add-seed sets the primary date field to `todayIso()` on add only; edits keep the
  stored date. Fuel already defaults via its `today` prop.
- **`components/DateQuickFill.tsx`** (new) — renders small text-buttons "+6 months" / "+1 year"; props
  `{ base?: string; onPick: (iso: string) => void }`; base defaults to `todayIso()`. Writes `addMonths(base,
  6 | 12)`. Placed under forward-looking date fields (service `nextDueDate`, task `targetDate`, issue
  follow-up). On the service sheet it coexists with the existing `suggestNextDue` type-driven suggestion and
  respects the `nextDueTouched` idiom.

### Comboboxes

- **`components/Combobox.tsx`** (new) — accessible combobox: `role="combobox"`, `aria-expanded`,
  `aria-controls`, `aria-autocomplete="list"`, a `listbox` of `option`s with `aria-selected` and
  `aria-activedescendant`. Props include the `Field` render-prop shape (`id`, `aria-describedby`,
  `aria-invalid`) plus `value`, `onChange(value)`, `suggestions: { value: string; hint?: string }[]`,
  `placeholder?`. Free-type (value = raw input); focus/click opens the list; typing filters case-insensitive
  contains; ArrowUp/Down/Enter/Escape keyboard nav; selecting sets the value and closes; click-outside closes.
  Drops into `<Field>{(p) => <Combobox {...p} …/>}</Field>`.
- **`api/reference.ts`** (new shared hook) — extract the reference-list query duplicated in
  `ReferenceListsPanel.tsx` and `ExpensesPage.tsx`: `useReferenceList('garages' | 'wash-locations' |
  'expense-categories')`. For combobox use, sort by `referenceCount` desc. Feeds service `garage`, task
  `assignedGarage`, wash `location`.
- **`lib/recentValues.ts`** (new) — `recentValues<T>(rows: T[], selector: (t) => string | null | undefined, n
  = 6): string[]` → distinct, non-empty, most-recent-first, capped. Feeds fuel `station`
  (`summary.fuel.entries[].station`), expense `vendor`, tyre `location`/`tool`, equipment `sourceVendor` from
  the vehicle summary the page already loads. No backend.

### Tests & gates

- Unit tests: `formErrors` (map→fields, residual→`_`, no-errors→`_`), `date`, `recentValues`, `Combobox`
  (filter, keyboard, free-type, axe), `DateQuickFill`.
- Updated sheet tests: assert inline field errors (message present, no "Bad Request" banner) and that a missing
  required field blocks the network call.
- `coverage.test.ts` axe entries for `Combobox` and `DateQuickFill` (own axe test preferred over EXEMPT).
- `tokens.test` / `tsc -b` green; full `npm run test` and `npm run build` green.

## External Dependencies (Conditional)

None. No new libraries — the combobox is hand-rolled to match the field-manual identity under the strict CSP,
consistent with `Spark`/`TimeChart`/`Seg` being hand-rolled. No schema, migration, or endpoint changes.
