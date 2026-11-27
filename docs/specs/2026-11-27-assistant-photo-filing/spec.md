# Spec Requirements Document

> Spec: Assistant photo filing
> Created: 2026-11-27
> Status: In progress. Tasks 1-4 and 5.1-5.5 shipped 2026-11-27 in `0.32.0`; the by-hand check in 5.6 (live chat
> key and an MCP client) and follow-up task 6 (audit `offer_to_file` over MCP) are open
> Depends on: @docs/specs/2026-11-27-photo-gallery-document-categories/

## Overview

Whenever a photo or document reaches the assistant, in the app's chat or through an MCP client, offer to file it
in Cambelt by default, already categorised and linked to the record it belongs to. A photo that is not a paper or
issue evidence is offered as a car photo for the Photos screen. Nothing is filed until the owner says so.

## User Stories

### Log it and keep the receipt in one go

As an owner, I want the receipt I photographed for the assistant to be filed with the expense it just logged, so
that I don't upload the same photo a second time on the Documents screen.

Today the chat reads the photo, drafts the expense, and the photo is gone when the turn ends. With this spec,
every attachment gets a filing card under the message the moment it is sent. Once the expense is confirmed, the
assistant fills in the card: Parts receipt, titled from the receipt, dated from it, linked to the new expense. One
press of File stores the original with its previews, and it shows on that expense's "Papers & photos" strip.

### Keep a picture of the car without explaining it

As an owner, I want a photo of the car that isn't a document to be offered for the gallery, so that "look at her
after the wash" ends up somewhere I can find it again.

When the assistant can't place a photo as a paper or as evidence for an issue, it offers it as a Car photo. If
the model says nothing at all, the card still appears with that default.

### File from Claude on my phone

As an owner using Claude through the MCP connection, I want a link that opens Cambelt ready to file the photo I
just showed it, so that a certificate I photographed in a conversation doesn't stay only in that conversation.

An MCP client's model sees the image but has no file to pass on, and MCP has no upload mechanism. So the tool
returns a link to cambelt.app with the category, title, date and record already chosen. The owner opens it,
picks the same photo from the camera roll and saves.

## Spec Scope

1. **`offer_to_file` tool** - one tool in the shared catalogue that validates a filing proposal (category,
   title, date, linked record) against the vehicle, writes nothing, and returns the proposal with a link.
2. **Filing cards in the in-app chat** - every attachment gets a card by default. The browser keeps the original
   file in memory, the tool's result refines the card, and File uploads the original through the documents
   endpoint.
3. **`/file` prefill route** - an authenticated route that turns a filing link into the right screen (Photos or
   Documents) with its upload sheet already filled in.
4. **Prompt and tool guidance** - the chat's system prompt and the MCP tool description tell the model to offer
   filing for every attachment, after any write it belongs to is confirmed, and to fall back to Car photo.
5. **`App:PublicOrigin` setting** - the public address used to make MCP filing links absolute. When blank, links
   are path-only.

## Out of Scope

- Filing without the owner pressing File or saving the upload sheet. The assistant never files on its own.
- An MCP client sending file bytes to the server, in a tool argument or otherwise.
- Keeping chat attachments on the server. Chat still discards them after the turn; filing is a separate upload
  from the browser.
- Reading or extracting anything new from photos beyond what chat already does.
- Batch filing of many attachments in one action. Each card files its own file.

## Expected Deliverable

1. In the in-app assistant, attaching a parts receipt photo and asking to log it, then confirming the expense
   draft, shows a card proposing "Parts receipt" linked to that expense. Pressing File puts the photo on that
   expense's "Papers & photos" strip.
2. Attaching a plain photo of the car with no request shows a card proposing "Car photo", and File adds it to
   the Photos screen.
3. Sharing an MOT certificate photo with Claude over MCP produces a cambelt.app link that opens Documents with the
   upload sheet set to MOT, titled and dated. Choosing the photo and saving files it.

## Spec Documentation

- Technical Specification: @docs/specs/2026-11-27-assistant-photo-filing/sub-specs/technical-spec.md
- API Specification: @docs/specs/2026-11-27-assistant-photo-filing/sub-specs/api-spec.md
