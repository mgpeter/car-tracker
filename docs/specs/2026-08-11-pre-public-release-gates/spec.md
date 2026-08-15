# Spec Requirements Document

> Spec: Pre-public-release gates - isolation, erasure, portability, and a closed door
> Created: 2026-08-11
> Status: Shipped 2026-08-14 in `35a0f06` (0.13.0), with two corrections found by deploying it -
> `0cbef01` (0.13.1) and `3f9f698` (0.13.2). Verified against the live deployment; see `tasks.md` task 9
> for which of the end-to-end checks have real evidence and which are still only covered by tests.
>
> **The sections below are the problem statement as it stood on 2026-08-11 and are deliberately left in the
> present tense.** "There is no account deletion", "two users cannot both have a garage called K & P Motors"
> and the reference-count leak all describe the world this spec was written to end. Rewriting them would
> destroy the argument for the work. What was *built* is recorded in `tasks.md`'s completion notes and in
> DEC-018; where the two disagree, the tasks file is what happened.

## Overview

Close everything that must be true before a stranger can create an account: make the three reference lists
belong to their owner, give a person a way to delete their account and take their data with them, and keep
registration shut until that is proven.

`docs/product/roadmap.md:198` records three gates. This spec closes two of them, retires the third, and adds
two obligations that appear on no list. It does **not** close HTTPS - that is a hosting decision in progress
and needs no code.

### The first gate is not what it says it is

The roadmap describes it as reference data being shared: "one user can rename or re-home another's data". That
is true and it undersells the mechanism.

`ReferenceListEditor` implements rename and re-home as explicit `ExecuteUpdateAsync` statements over
`ServiceRecords`, `MaintenanceTasks`, `WashEntries`, `ExpenseEntries` and `BudgetGroupCategories`
(`ReferenceListEditor.cs:124-126`, `:152-154`, `:207`, `:233`, `:296-297`, `:327-328`). Phase 4.5's isolation is
**one query filter on `Vehicle`** (`CarTrackerDbContext.cs:85`), and it works because every other entity is
reached through a vehicle id that was itself resolved through that filter - "a new endpoint cannot forget to
filter".

These statements do not go through a vehicle. They match on a name.

So the moment two accounts each hold a garage called "K & P Motors", one user renaming theirs issues an
`UPDATE` across the other user's service records. That is not a visibility leak that a second pair of eyes
would catch in review as untidy - it is a **write into another account's data**, and it is armed by the
second user, not by the hundredth.

`ExpenseCategory` has the identical shape and is named in no gate at all. Its cascade reaches two tables, and
`CountCategoryReferencesAsync` (`ReferenceListEditor.cs:249-251`) aggregates reference counts across every
account, so `GET /api/reference/expense-categories` already reports other people's usage as your own.

This is the same failure the query filter was designed to make impossible, in the one place the filter cannot
see. The fix is not to remember harder; it is to give the reference tables the same filter and to route the
cascades through it.

### The two obligations nobody wrote down

There is **no account deletion**. Nothing in the codebase removes a `User`, and there is no vehicle `DELETE`
endpoint to build one from. UK GDPR Art. 17 is not optional once someone who is not you has signed up.

There is **no data access or portability** (Art. 15/20). `docs/product/roadmap.md:151` lists "Export to
Excel/CSV" as an unstarted Phase 5 feature, framed as a convenience. It is also a legal obligation wearing a
convenience's clothes, and the obligation is cheaper to satisfy than the convenience.

## User Stories

### My data is mine, including the parts that look like free text

As someone who signs up after the first user, I want my garages, wash locations and expense categories to be
mine alone, so that another person editing their reference list cannot rewrite my service history.

Today `Garage`, `WashLocation` and `ExpenseCategory` are global tables keyed by name. Two users cannot both
have a garage called "K & P Motors" - the second one to type it silently adopts the first one's row, including
its address and contact. Then a rename by either rewrites both. The columns that point at them -
`ServiceRecord.Garage`, `MaintenanceTask.AssignedGarage`, `Vehicle.DefaultGarage`, `WashEntry.Location`,
`ExpenseEntry.Category`, `BudgetGroupCategory.Category` - all look like free text and are foreign keys, which
is exactly the trap CLAUDE.md already records once.

### I can leave, and leaving means gone

As someone closing my account, I want everything the app holds about me destroyed - vehicles, logs, uploaded
documents, assistant tokens and my login itself - so that deleting my account is not merely hiding it.

The confirmation must be proportionate to the act. `ConfirmButton`'s two-step is calibrated for removing one
fuel fill; it is the wrong instrument for destroying four years of history. The screen must state what will be
destroyed in counts before it will let the action arm.

### I can take it with me

As someone who has entered years of history by hand, I want to download everything I have put in, so that
leaving is not the same as losing it and so that a subject access request has an answer that is not "give me a
fortnight".

### Not everyone gets in yet

As the person deploying this, I want registration closed behind an allowlist, so that the app can sit on a
public domain while the decision to open sign-up stays a separate, later, deliberate act.

This also disarms DEC-016. First-user-claims-all-unowned-vehicles was correct for the single-user migration it
was written for and is a trap on a deployment where a stranger might be first through the door.

## Spec Scope

1. **Per-owner reference lists** - `Garage`, `WashLocation` and `ExpenseCategory` keyed `(OwnerId, Name)`,
   scoped by query filters mirroring the existing `Vehicle` one, with every cascade and reference-count in
   `ReferenceListEditor` routed through that filter.
2. **Account deletion** - `DELETE /api/account`, destroying vehicles and all their children, assistant tokens
   and their audit trail, the owner's reference rows, the document bytes on the volume, the `User` row, and
   the Auth0 identity.
3. **Data export** - `GET /api/account/export`, a JSON document of raw rows for everything the account owns.
4. **Sign-up allowlist** - an unknown identity whose email is not allowed is refused before a `User` row
   exists, with a distinct signed-in-but-not-invited state in the client.
5. **DEC-016 retired** - first-login vehicle adoption becomes opt-in configuration, defaulting to off.

## Out of Scope

- **HTTPS.** Gate two on the roadmap, unmet, and untouched here: it is a deployment decision (currently being
  taken) and needs no code. It remains blocking for public sign-up.
- **Excel/CSV export.** The Phase 5 feature stands on its own merits - per-screen shaping, a spreadsheet
  package, formatting choices. Portability is satisfied by JSON, which needs none of that.
- **Document bytes in the export.** Metadata is included; the files are not. They are already individually
  downloadable through the authenticated `apiBlob()` seam, and streaming a zip of up to 25 MB per document is
  a materially larger feature. The boundary is disclosed in the payload rather than left to be discovered.
- **User-facing per-vehicle delete.** Deletion here is account-scoped. Removing one car from a garage of three
  is a product feature with its own questions (what happens to shared reference rows, is it undoable) and no
  legal deadline.
- **Invite codes, seats, teams, or any sharing model.** The allowlist is a door, not a product.

## Expected Deliverable

1. Two accounts can each hold a garage, wash location and expense category of the same name, and neither can
   observe, count, rename or re-home the other's - verified by a test that fails on today's code.
2. A signed-in user can download everything the app holds about them, and can destroy it, from Settings, with
   the counts stated before the destructive action arms.
3. Opening registration to the public becomes a configuration change rather than an engineering project - the
   only gate left standing is HTTPS.
