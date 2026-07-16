# Spec Requirements Document

> Spec: Anomaly Lifecycle — Auto-Reconcile
> Created: 2026-07-16
> Status: Planning

## Overview

Close an open integrity flag automatically when the data that caused it is deleted or edited away, so the queue
reflects what is wrong *now* rather than what was once wrong. `AnomalyScanner` today only ever adds flags; a
flag whose cause disappears stays Open forever, pointing at a row that no longer exists.

## User Stories

### A fixed problem stops being a problem

As the owner, I want a flag to clear itself when I fix or delete the data behind it, so that the queue is a list
of things still wrong and not a graveyard of things I already handled.

The detector is pure and additive: every write re-scans the whole history and returns the anomalies that are
*currently* true, and the scanner persists the new ones. Nothing runs the other way. So when a fill with an
implausible 71 MPG is deleted, the next scan simply does not find that condition — and the Open flag it raised
sits there orphaned, pointing at a fuel entry that is gone. It was found live this session: deleting the stray
fill left the flag open, and it had to be resolved by hand. The dashboard's "1 figure in question" then
overstates reality, which is exactly the kind of drift the integrity axis exists to prevent.

### But my decisions are mine

As the owner, I want the flags I have *judged* to stay judged, so that the app never overrules me with a rule.

A flag I marked **Accepted** ("the odometer really did read that") or **Dismissed** ("not worth it") is a
decision, not an open question — and the existing re-raise rule already protects those from the detector. The
auto-reconcile must respect the same line: it closes **Open** flags whose cause is gone, and never touches an
Accepted or Dismissed one, whatever the data does.

## Spec Scope

1. **Auto-resolve vanished flags** — after each scan, any **Open** flag whose `(Kind, EntityType, EntityId)`
   is no longer in the detector's currently-true set is marked `Corrected` with `ResolvedAt` set and a
   system-written resolution note.
2. **Respect owner decisions** — Accepted and Dismissed flags are never auto-touched; only Open ones reconcile.
3. **Re-raise on return** — because the auto-resolve reuses `Corrected`, and `Corrected` already does not
   suppress a re-raise, re-adding the bad data flags it again. This falls out of the existing rule; the spec
   asserts it rather than building it.

## Out of Scope

- **A distinct `AutoResolved` status.** Reusing `Corrected` was chosen deliberately (owner decision, 2026-07-16)
  to avoid a new enum member, a migration, and a change to `ck_anomalies_resolved_iff_terminal`. The audit
  distinction between "the human corrected it" and "the scanner corrected it" lives in the **resolution note**,
  not in a separate status.
- **Silent deletion.** The flag row is kept, per the queue's own promise that "nothing here is deleted; a
  resolved flag keeps its row and its reason". Auto-resolve is a status change with a note, never a delete.
- **New detectors, or changes to what the three existing ones detect.** This is purely about the lifecycle of a
  flag after it is raised.
- **A UI surface for auto-resolved flags.** They already appear under `?status=all` as Corrected, like any other
  resolved flag; the integrity screen needs no change.

## Expected Deliverable

1. Deleting the fuel entry behind an implausible-MPG flag auto-resolves that flag to `Corrected` with a system
   note on the next scan; the row remains visible under `?status=all`, and the dashboard's open count drops.
2. Editing a service record's mileage down so it no longer exceeds the current odometer auto-resolves its
   `MileageNonMonotonic` flag, while an unrelated Open flag on the same vehicle stays Open.
3. A flag the owner marked **Accepted** is untouched by any subsequent scan, even after the data changes — and
   re-adding data that recreates a previously **Corrected** (including auto-corrected) condition raises a fresh
   flag.
