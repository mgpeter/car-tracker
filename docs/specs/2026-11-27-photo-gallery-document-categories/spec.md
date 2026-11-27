# Spec Requirements Document

> Spec: Photo gallery and document categories
> Created: 2026-11-27
> Status: In progress. Tasks 1-6 shipped 2026-11-27 in `0.31.0`; follow-up task 7 (accept `"Photo"` as an alias
> for `ConditionPhoto`) is open. Checked by hand on localhost 2026-11-27: rendition headers through the gateway,
> no GPS in either rendition, viewer and cover photo. Still owed by hand: an iPhone HEIC, the Compare layout, and
> the two error answers in 2.7

## Overview

Split a vehicle's files into papers, filed under a grouped set of categories, and photos, shown in a proper
gallery with thumbnails and a full-screen viewer, while keeping one storage path underneath. Show each record's
attached papers and photos on the record itself, so the link from a document to a service record, expense or
issue can be followed in both directions.

## User Stories

### Keep pictures of the car somewhere that is not a filing cabinet

As an owner, I want to keep photos of my car (on the drive, after a wash, on a trip) in a gallery I can browse
full screen, so that they stop being filed as "documents" next to the V5C.

Today every photo is `Type = Photo` on the Documents screen, and an unlinked photo is labelled as the condition
baseline whether it is one or not. The new Photos screen shows thumbnails grouped by month, opens a full-screen
viewer with arrow-key and swipe navigation, and lets one photo be chosen as the car's cover, shown on its garage
card. Condition photos (the baseline and issue evidence) stay distinct from general car photos, so "worsening"
is still measured against a real baseline.

### File a paper under a category I recognise

As an owner, I want to file a paper as a service invoice, a parts receipt, a warranty or a workshop manual, so
that I can find it again by what it is rather than scrolling one "Receipt" or "Other" pile.

Categories are a fixed, server-owned list in six groups. The Documents screen shows papers only, grouped and
searchable, and the upload sheet offers the list grouped.

### See the evidence on the record it belongs to

As an owner, I want to open an issue and see its photos in date order, oldest next to newest, and open a service
record and see its invoice, so that I don't have to switch to another screen and match titles.

Service history, expenses and issues each gain a "Papers & photos" strip listing what is attached, with an
Attach button that opens the upload sheet already linked to that record.

## Spec Scope

1. **Grouped document categories** - `DocumentType` grows into a fixed list in six groups (Ownership, Legal &
   cover, Work & parts, Reference, Photos, Other), served by a new endpoint so the client never hand-types it.
2. **Photo renditions made in the browser** - on upload of any image the browser can decode, the client also
   sends a small thumbnail and a display copy, which fixes HEIC display, makes the grid light and strips camera
   metadata (including GPS) from everything the app shows.
3. **Photos screen** - a per-vehicle gallery of photo-category files with month grouping, a full-screen viewer,
   multi-file upload and a cover photo shown on the garage card.
4. **Documents screen for papers only** - papers grouped by category group, with search and a category filter
   through `useTableView`.
5. **Attachments on records** - a "Papers & photos" strip on service history, expenses and issues, and an
   issue photo timeline with the earliest and latest side by side.

## Out of Scope

- Filing from the in-app assistant or an MCP client. That is
  `docs/specs/2026-11-27-assistant-photo-filing/`, which builds on this spec.
- Owner-editable categories and free-form tags. The list is fixed and server-owned.
- Server-side image processing, and any image library on the server.
- Rewriting the original file. It stays byte-identical (it is evidence and its hash is how duplicates are
  caught), so the original download still carries whatever metadata the camera wrote.
- Albums, captions beyond the existing title and notes, and photo ordering by hand.
- New link targets (equipment, tasks, tyre readings). The three existing links stay as they are.
- Exporting or importing the files themselves.

## Expected Deliverable

1. An HEIC photo uploaded from an iPhone in Safari shows as a thumbnail on the Photos screen in Chrome on a
   desktop and opens full screen in the viewer, while Save still downloads the original HEIC byte for byte.
2. On the Photos screen, a photo chosen as the cover appears on that car's garage card, and the arrow keys step
   through the viewer in date order.
3. Opening an issue that has three photos linked shows all three in date order, with the oldest and newest side
   by side, and a service record with an invoice attached shows the invoice in its "Papers & photos" strip.

## Spec Documentation

- Technical Specification: @docs/specs/2026-11-27-photo-gallery-document-categories/sub-specs/technical-spec.md
- Database Schema: @docs/specs/2026-11-27-photo-gallery-document-categories/sub-specs/database-schema.md
- API Specification: @docs/specs/2026-11-27-photo-gallery-document-categories/sub-specs/api-spec.md
