# Spec Requirements Document

> Spec: DVLA/MOT Lookup — reg to vehicle facts
> Created: 2026-07-16
> Status: Planning

## Overview

Look up a registration against the DVLA and DVSA APIs, server-side, to pre-fill the add-car form — make, model,
year, colour, engine, fuel type — and to seed MOT and tax status from the reg, so the owner types a plate
instead of eleven fields. The lookup fills a form the owner confirms; it creates nothing on its own, and its MOT
data seeds a fallback, never a stored countdown.

## User Stories

### A plate, not a form

As the owner, I want to type a registration and press "Look up", so that the add-car form arrives already
filled with make, model, year, colour, engine and fuel type instead of my transcribing them from the V5C.

The add-car flow (`AddVehicleSheet.tsx`, `POST /api/vehicles`) reaches eleven of the vehicle's fields, and today
every one is hand-typed. The design's `garage.dc.html` shows the intended shape: a plate input, a "Look up"
button, and the hint "Fetches make, model, year, colour, engine, MOT and tax status from the DVLA — you confirm
before anything is created." The lookup calls the DVLA server-side and returns un-persisted facts; the owner
reviews them in the same sheet, edits anything wrong, and only then submits — the create path is unchanged.

### Confirm before anything exists

As the owner, I want the lookup to fill the form and stop there, so that a typo'd or wrong-vehicle reg costs me
a glance, not a vehicle I have to delete.

The design is explicit — "you confirm before anything is created" — and the create toast ("Vehicle created — a
fresh scope with empty logs and the default 18 check definitions") fires only on submit. The lookup is a
read: it returns facts, the owner confirms, `VehicleFactory` runs exactly as it does for a hand-typed car,
including the opening `MileageReading` it guarantees. There is no auto-create branch.

### MOT status without a stale countdown

As the owner, I want the DVLA's MOT expiry to seed the car's MOT status, so that a freshly-added car shows a
renewal countdown — but I do not want that date stored as the answer, because a stored MOT expiry is the exact
defect this whole project exists to fix.

MOT expiry in this app is *derived* from the latest MOT `ServiceRecord`'s `NextDueDate`, never stored
(`VehicleEndpoints.cs`: "a stored MOT expiry is exactly how the spreadsheet came to show a red 23-day countdown
for a test that had already passed"). The DVLA's MOT date therefore lands as `MotExpirySeed` — the documented
fallback read only while no MOT record exists — or as an initial MOT `ServiceRecord`, so a real logged pass
still wins. It must never become a figure that overrides a derived one.

## Spec Scope

1. **Server-side lookup endpoint** — a `GET /api/vehicles/lookup/{registration}` that calls the DVLA VES and
   DVSA MOT History APIs, returning un-persisted facts (identity, engine, fuel, MOT and tax status) for the
   owner to confirm. Keys stay server-side.
2. **Add-car pre-fill** — the plate input + "Look up" button in `AddVehicleSheet.tsx`, populating the form
   fields from the response; every field remains editable before submit.
3. **MOT seeding without a stored countdown** — the DVLA MOT expiry seeds `MotExpirySeed` (or an initial MOT
   `ServiceRecord`), never a stored MOT-expiry figure; a subsequent logged pass supersedes it.
4. **Tax status surfacing** — VED expiry and taxed/SORN status from VES map to the existing stored VED inputs
   (`VedExpiry`), which the renewal calculator already reads.
5. **Graceful failure** — an unknown reg, a rate-limit, or an API outage returns a clear "look it up failed,
   enter manually" state; the manual path (today's behaviour) always remains open.

## Out of Scope

- **Auto-refresh on a schedule.** Re-polling the DVLA to keep MOT/tax current is a recurring job that pairs
  naturally with the reminders engine (its own spec) — this spec does the on-demand, owner-initiated lookup
  only. A background refresh that silently overwrites a seed would also risk reintroducing the stored-countdown
  problem without the owner in the loop.
- **Bulk / fleet lookup.** One reg at a time, at add-car. A fleet importer is not a use anyone has, and the
  garage is grown one confirmed car at a time (DEC-007).
- **Writing anything without the confirm step.** No "look up and create" shortcut. The design's whole promise is
  "you confirm before anything is created"; an auto-create path would break it and could persist a wrong-vehicle
  match.
- **Refreshing an *existing* car's facts from the reg.** This is the add-car pre-fill. Re-running a lookup
  against a car already in the garage (to correct a colour, say) is a plausible later feature but is not this
  spec — it raises "which wins, the stored edit or the DVLA value" questions the add path never has.
- **The MOT *history* (past tests, advisories).** DVSA returns a test history; this spec takes only the current
  expiry to seed the countdown. Storing the advisory history is a documents/service-history concern, not this.

## Expected Deliverable

1. In the add-car sheet, typing BT53's reg and pressing "Look up" fills make (Land Rover), model, year (2003),
   colour, engine and fuel type (petrol) from the DVLA, server-side, with the DVLA key never reaching the
   browser; every field stays editable and nothing is created until submit.
2. The looked-up MOT expiry seeds the new car's MOT status so the dashboard shows a renewal countdown, but it
   lands as `MotExpirySeed` / an initial MOT record — a later logged MOT pass supersedes it, and no stored
   MOT-expiry figure exists.
3. An unknown or un-resolvable reg shows a clear failure and leaves the manual-entry path fully usable.
