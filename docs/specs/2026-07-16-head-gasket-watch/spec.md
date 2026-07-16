# Spec Requirements Document

> Spec: Head-Gasket Watch — checks as an issue's early-warning
> Created: 2026-07-16
> Status: Planning

## Overview

Let an issue on the watchlist declare which regular checks are its early-warning, so its "Monitoring / Resolved"
state can be shown as *contingent* on those checks staying current, and the dashboard can name the watch
instead of listing generic overdue checks. The design shows this concept everywhere and the schema models none
of it — a code comment in `VehicleCard.tsx` already reads "nothing models which checks are the head-gasket
watch."

## User Stories

### The watch that keeps an issue asleep

As the owner, I want to tie the weekly oil-filler-cap and coolant-colour checks to the head-gasket issue, so
that "resolved" means "resolved *and* still being watched", not "resolved and forgotten".

The K-series head gasket is the frailty the whole spec is built around, and the two weekly checks are its
early-warning system — the design says the head-gasket issue is "resolved conditionally … these two checks are
what keep it that way." Today an issue is Monitoring or Resolved with no link to anything. A watch link makes
the contingency real: the issue screen can show "Resolved, contingent on 2 checks — both currently 19 days
overdue", and the dashboard can lead its attention panel with "Head-gasket watch · lapsed" and drop the count
when they are logged, exactly as the design does.

### Name the watch, don't list the checks

As the owner, I want the dashboard to tell me a *named watch* has lapsed, so that I understand the stakes
rather than reading "2 checks overdue" among five others.

The design's attention panel leads with "Head-gasket watch · lapsed — the two K-series early-warning checks are
19 days overdue" and offers "Mark both done". The app's current attention panel derives generic alerts and
cannot say which overdue checks matter more, because it has no concept of a watch.

## Spec Scope

1. **Watch link** — an `Issue` can name the `CheckDefinition`s that are its early-warning (a many-to-many, an
   issue may watch several checks and a check may guard more than one issue).
2. **Contingent status on the issues screen** — a Resolved issue with a watch shows its watch's live check
   status, and flags when a watched check has lapsed ("keeping this resolved depends on 2 checks · both
   overdue").
3. **Named watch on the dashboard** — the attention panel surfaces a lapsed watch by name, above generic
   overdue checks, with a "log the watch's checks" action.
4. **Watch editing** — the link is set and cleared on the issue's edit sheet, choosing from the vehicle's
   check definitions.

## Out of Scope

- **Auto-reopening a Resolved issue** when its watch lapses. The design keeps the issue Resolved and *flags*
  the contingency rather than flipping the status — reopening is a decision for the owner, and silently
  reopening would be the app overruling them (the same principle the anomaly lifecycle respects). The watch
  surfaces the risk; it does not act on it.
- **A generic tagging system for checks.** This is one specific relationship (issue ↔ check), not free-form
  tags. If a broader tag need appears later it is its own spec.
- **Check grouping for its own sake** — the "5 weekly walk-around checks" batch already exists on the checks
  screen; this spec is only the issue-contingency link, not a new grouping primitive.

## Expected Deliverable

1. On the issues screen, linking the head-gasket issue to the two weekly checks and marking it Resolved shows
   "Resolved · contingent on 2 checks", and turns to a warning state when either check is overdue — without the
   issue's status changing.
2. The dashboard's attention panel names a lapsed watch ("Head-gasket watch · lapsed") above the generic
   overdue-check alert, and logging the watched checks clears it.
3. The link round-trips through the issue edit sheet and survives a reload, and an issue with no watch behaves
   exactly as today.
