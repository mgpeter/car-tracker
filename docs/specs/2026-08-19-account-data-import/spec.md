# Spec Requirements Document

> Spec: Account Data Import - the export read back in
> Created: 2026-08-19
> Status: Planning

## Overview

Read a `GET /api/account/export` file back into an account, cloning its garage, cars and every log row into the
signed-in user's own setup alongside whatever is already there. New rows, new ids, same data.

The export exists because UK GDPR Art. 20 asks for portability, and portability with no way back in is a
half-answer: the file is readable by a person and by nothing else. This closes it.

## User Stories

### Move to another deployment without retyping four years of history

As the owner, I want to export from one deployment and import into another, so that moving hosts, rebuilding a
database or recovering from a mistake does not mean re-entering 13 fuel fills, 18 check definitions and every
service record by hand.

This is the case the app is closest to needing today. The database half of Phase 5's backup ships, but a
`pg_dump` restores a *server*; it cannot move one account onto a deployment that already has others on it.

### Take on a car whose history already exists

As someone buying a car from another Cambelt owner, I want their export of that vehicle to become mine, so
that the car arrives with its real service history rather than starting at the odometer reading on the day I
bought it.

The seller exports, sends me the file, and their car appears in my garage with every fill, service and check
it has ever had. Their account is not touched and mine is not replaced: the cars land beside the ones I
already own.

### See a real garage before committing to typing one

As someone evaluating the app, I want to import a real export, so that every screen has enough history to be
worth looking at without me first spending an evening on data entry.

## Spec Scope

1. **Two-step import** - upload the file to a preview endpoint that reports exactly what it would do and
   writes nothing; a second call commits it against a server-held id.
2. **Faithful row-level clone** - every table the export carries, inserted verbatim under new ids with every
   foreign key remapped, rather than replayed through the write paths that would fire the mirrors a second
   time.
3. **Collision handling by rename** - a registration the account already owns is imported under a modified
   one, proposed by the server and editable in the preview before anything is written.
4. **Reference lists merged by name** - garages, wash locations and expense categories are matched to the
   account's own by name and created only when missing, never overwritten.
5. **A report worth reading** - what was created, what was matched, what was skipped and why, both before and
   after the commit.

## Out of Scope

- **Merging into an existing vehicle.** A matching registration produces a second car, never a merge. Merging
  needs a reliable per-row identity ("is this the same fill I already have?") that no exported row carries.
- **Document files.** The export deliberately carries document rows and not their bytes, so the rows are
  skipped and counted. Importing them would create `Document` rows pointing at files that do not exist, which
  is the exact failure `docs/deployment-synology.md` warns about for a dump restored without its documents
  directory.
- **Assistant tokens and the write-audit trail.** A token row without its secret is not a credential, and an
  audit trail describes writes that happened on another deployment. Importing either would be fabricating a
  record rather than restoring one.
- **The `account` block.** Email, external id and display name are shown in the preview as provenance and are
  written nowhere. An import cannot change who you are.
- **Anomaly flags.** Not imported; re-derived after the rows land, so the queue describes the data that is now
  in the database rather than the data that was in another one.
- **CSV or spreadsheet input.** This reads the JSON the export writes. The still-open "parity with the old
  workflow" line on the roadmap is about a spreadsheet *rendering* of the export and is a separate job.
- **An MCP or chat tool.** Import is a destructive-adjacent bulk write with a confirmation step; widening the
  assistant's surface to include it is its own decision.

## Expected Deliverable

1. Export an account, import the file into a second account, and the second account's dashboard shows the same
   derived figures as the first - MPG, cost per mile, MOT countdown, check statuses - because the rows they
   are computed from are the same rows.
2. Importing a file whose registrations you already own shows each proposed replacement registration in the
   preview, lets you edit it, and after the commit both the original and the imported car exist with their own
   complete histories.
3. A truncated, malformed or foreign JSON file is refused with a message naming what is wrong, and the
   database is unchanged.
