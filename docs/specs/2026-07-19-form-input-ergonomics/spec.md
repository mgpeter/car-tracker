# Spec Requirements Document

> Spec: Form validation + frictionless data entry
> Created: 2026-07-19
> Status: Complete

## Overview

Make adding and editing records across every sheet fast and forgiving: mark invalid fields inline with a
plain-English message and a red outline instead of a generic "Bad Request" banner, default record dates to
today with quick-fill links for future dates, and offer a "type new or pick a recent one" combobox on every
garage/station/place field. The goal is to make the phone-in-the-driveway case genuinely faster than the
spreadsheet it replaces.

## User Stories

### Know exactly what's missing

As a car owner filling in a form, I want the form to tell me *which* field is wrong and why, so that I can fix
it immediately instead of guessing what "Bad Request" means.

When I submit a fuel fill with no litres, the Litres field gets a red outline and a message beside it
("Litres?") rather than a single red banner at the bottom of the sheet. The same holds for every add/edit
sheet in the app. If the failure is something only the server can know (a duplicate registration, a mirrored
row that can't be edited), I still see the server's own human-readable reason — never "Conflict" or
"Bad Request".

### Add a record with almost no typing

As a car owner logging routine data, I want the date to already say today and the place field to remember where
I usually go, so that a fill-up or a wash takes a couple of taps.

Every add sheet opens with today's date pre-filled. When I record a service and set a next-due date, I can tap
"+6 months" or "+1 year" instead of opening a date picker. When I type the station or garage, the field shows
the places I've used recently (most-used first) and lets me pick one — or type a brand-new name that's kept for
next time.

## Spec Scope

1. **Inline field validation** - Parse the server's existing per-field `errors` map and render each message
   against its field (red outline + `aria-invalid` + message), with a form-level banner only for errors that
   don't map to a field; add lightweight client-side required-field checks for instant feedback.
2. **Date defaults** - Every add sheet's primary date field defaults to today (edits keep their stored date),
   via a shared date helper.
3. **Date quick-fill links** - "+6 months" / "+1 year" shortcuts on forward-looking date fields (service
   next-due, task target, issue follow-up) that fill the date from the record's own date or today.
4. **Recent-value comboboxes** - A custom accessible combobox on every place field (garage, station, wash
   location, vendor, tool, tyre location, equipment source): free-type allowed, recent/known values shown on
   focus and filtered as you type. Garage/wash-location source from their reference-list GETs; the rest derive
   distinct recent values from the vehicle's own history.

## Out of Scope

- New database tables or reference endpoints for stations, vendors, tools, etc. (suggestions for those come
  from existing record history, client-side).
- Server-side validation for the endpoints that currently don't validate at all (tyres/wash create+edit,
  service/task/issue edit) — the client-side checks cover the UX; server hardening is a noted stretch task.
- A DVLA/registration lookup on the add-vehicle sheet (remains in the §8 backlog).
- Changing the expense **category** control (stays a constrained `<select>`; it is a managed list, not
  free-type).
- Centralising the per-screen display date formatters (`shortDate`/`dayMonth`/…) — only `todayIso`/`addMonths`
  are shared here.

## Expected Deliverable

1. Submitting any add/edit sheet with missing/invalid data marks the specific field(s) with a red outline and a
   human message — no "Bad Request"/"Conflict" banner — and a valid submit still saves.
2. Every add sheet opens with today's date; the service sheet's "+6 months"/"+1 year" links fill the next-due
   date, all verified in the browser.
3. Place fields (e.g. fuel station, service garage) show recent/known values on focus, filter as you type, and
   still accept a new value, verified in the browser in both light and dark themes.
