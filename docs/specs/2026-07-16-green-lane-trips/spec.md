# Spec Requirements Document

> Spec: Green-Lane Trips — the outing log the visual identity implies
> Created: 2026-07-16
> Status: Planning

## Overview

Add a `Trip` (outing) log — date, place, terrain, mileage delta — that, on save, prompts the wash reset and
coolant check the field manual prescribes after every green lane, closing the loop between driving BT53 off-road
and the maintenance that off-road use then demands. This is **net-new, speculative scope outside README §1–§8**,
drawn from the app's own visual-identity source; it is larger than a normal feature and should earn an explicit
product decision (a DEC) before implementation. It is written up because the user asked to ideate *from* the
design, and this — a green-lane trip planner — *is* the design's origin.

## User Stories

### The outing that triggers its own aftercare

As the owner, I want logging a green-lane outing to prompt the underside rinse and the coolant recheck the
manual says to do afterwards, so that the maintenance an off-road day creates is captured, not forgotten a week
later.

Every route card in `archive/Sample-design-and-road-trip-tracking-green-lane-field-manual.html` ends the same
way — R1 "Aftercare: rinse underside + arches", R3 "rinse; recheck coolant", and the prep checklist's "Check
coolant and oil before setting off (K-series head-gasket watch)". A Freelander's two known frailties (the
K-series head gasket and the VCU) are precisely what off-road use stresses: wheelspin can seize the VCU, and
heat/strain is the head gasket's enemy. So an outing is not just a diary entry — it is a *maintenance event*.
Saving a `Trip` writes a mileage reading and prompts the wash reset and the coolant/oil check the manual
prescribes, tying the drive to the checks it should reset.

### A record of where the car has actually been worked

As the owner, I want a log of outings — date, place, terrain, difficulty, miles — so that I can see how hard the
car has been used and correlate it with what then needed attention.

The workbook has thirteen sheets and none of them is this: the field manual is a whole domain (routes R1–R4,
difficulty bars, seasonal windows, TRO/legal status, itinerary steps, fuel/aftercare footers) that never made it
into the 17 screens. An outing log is the smallest honest slice of it that carries real maintenance value,
reusing the mileage log, the wash cadence and the coolant check that already exist.

## Spec Scope

This spec **presents scope options and recommends starting small**, because the full field manual is far larger
than one feature and much of it is speculative. The recommended v1 is (a) alone.

1. **(a) The outing log — RECOMMENDED v1.** A `Trip`/`Outing` entity: date, place/route, terrain, difficulty
   (1–3, the manual's bars), an optional end-of-outing odometer, notes. On save it writes a `MileageReading`
   and **prompts** the wash reset and coolant/oil check the manual prescribes — the maintenance-loop half. This
   reuses `MileageReading`, `WashEntry`/wash cadence, and the coolant `CheckLog`; it is the part that pays for
   itself.
2. **Trip → maintenance prompts.** On save, surface "rinse underside + arches" (a wash-cadence reset) and
   "recheck coolant / oil" (the K-series head-gasket checks) as suggested follow-ups — a prompt, never an
   automatic log, consistent with how the app flags rather than acts.
3. **(b) Read-only route reference — LATER, larger.** The manual's routes (R1–R4) with difficulty, seasonal
   window and legal/TRO status as static reference content — the planner half. Bigger, more speculative, and
   delivers no maintenance value; deferred behind (a).
4. **(c) Map — LATER, and CSP-blocked as designed.** The manual's Leaflet map with category filters. The manual
   loads Leaflet from a CDN, which the strict CSP blocks (the same reason the fonts were inlined); a map needs
   self-hosted tiles or a static image. Out of v1.
5. **A DEC before build.** Because this is outside README §1–§8, record a product decision on whether the
   outing log earns implementation, and on the (a)/(b)/(c) staging, before writing code.

## Out of Scope

- **Live TRO / Trailwise2 legal-status integration.** The manual leans hard on "confirm each lane's live status
  before you drive it" (Trailwise2 / TW2). A live legal-status feed is an external, speculative integration with
  no committed source, and a *wrong* "legal today" is a legal risk, not a convenience — firmly out.
- **Guided-trip booking.** R1 and R4 involve phoning Surrey 4x4 Tours / Slindon Safari. Booking is a
  third-party transaction the app has no business owning.
- **The map in v1 (c).** Leaflet from a CDN is CSP-blocked by design; a self-hosted or static map is a
  meaningful build of its own and delivers planning, not maintenance. Deferred.
- **The route reference in v1 (b).** The planner half is real to the design but returns no maintenance value and
  is larger; it waits behind the outing log.
- **A generic "trip" concept for on-road journeys.** This is specifically the green-lane/off-road outing that
  stresses the VCU and head gasket and drives aftercare — not a mileage-logging journal for every drive, which
  the `MileageReading` log already covers.

## Expected Deliverable

1. On a new Trips screen, logging a BT53 outing (date, place, terrain, difficulty, end odometer) creates a
   `Trip`, writes a `MileageReading` from the odometer, and prompts the underside-rinse (wash reset) and the
   coolant/oil recheck — the K-series aftercare the manual prescribes — as suggested, not automatic, follow-ups.
2. The outing appears in a list with its difficulty and terrain, and the mileage reading it wrote shows in the
   mileage log; an outing with no odometer writes no reading.
3. This spec is explicitly flagged as speculative, net-new scope outside README §1–§8, with a DEC recorded
   before implementation — the deliverable includes the decision, not only the code.
