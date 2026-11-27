# Technical Specification

This is the technical specification for the spec detailed in @docs/specs/2026-11-27-assistant-photo-filing/spec.md

Builds on `2026-11-27-photo-gallery-document-categories`: its categories, `DocumentKind`, renditions and the
Photos screen must have shipped first.

## Constraints this design follows

- **Chat holds no files.** The transcript lives in the browser and `ChatFiles` discards attachments after each
  turn. So the bytes for filing come from the browser, which still has the file the owner picked.
- **An MCP client cannot hand over a file.** Its model sees pixels, not the file, and MCP has no upload
  mechanism. So MCP filing is a link the owner opens.
- **Writes through the assistant need confirmation** (`PendingWriteStore`). `offer_to_file` writes nothing, so it
  needs no confirmation and no audit row. The write is the owner's own upload from the web app.

## `offer_to_file` tool

- New `[McpServerToolType]` class `FilingTools` in `CarTracker.ModelContextProtocol/Tools`, added to
  `CarTrackerToolCatalogue.ToolTypes` and the MCP registration. Available to read-scope tokens, like the read
  tools. The drift test must pass with it.
- Parameters:
  - `vehicle` (optional, default-vehicle fallback as every tool)
  - `type` (`DocumentType`, required)
  - `title` (required, at most 160 characters)
  - `documentDate` (optional)
  - at most one of `serviceRecordId`, `expenseEntryId`, `issueId`
  - `attachment` (optional, chat only): the 1-based position of the file in the owner's latest message with
    attachments
- Validation, as tool errors with plain messages: unknown vehicle, two links, a link id that is not this
  vehicle's (resolved through the owner-filtered context, so a foreign id reads as not found), title too long.
- Returns `McpResult<FilingOffer>`: the vehicle's registration, the type with its kind, group and label, the
  title, date, `LinkedTo` (the same `DocumentLink` shape documents use), `attachment`, the document allowance
  (`used`, `limit`), `path` and `url`. The human summary reads like "Ready to file as Parts receipt, linked to
  the 14 Nov expense. Open: <url>".
- `path` is `/file?vehicle=<registration>&type=...&title=...&date=...&issueId=...`, built with a URL encoder.
  The server never builds the vehicle slug, because the web app owns that rule (`hrefFor`).
- `url` is `App:PublicOrigin` + `path`, or null when the key is blank.
- Description, because MCP clients have nothing else to go on: call this whenever the user shares a photo or
  document; the tool cannot upload anything and returns a link the user opens to file it; a photo of the car
  that is not a paper or issue evidence is `CarPhoto`; damage or wear for a known issue is `ConditionPhoto`
  linked to it; if the photo led to a write, call this after the write succeeds and link the new record.

## Configuration

- `App:PublicOrigin`, a string such as `https://cambelt.app`. **Blank means filing links are path-only**, and the
  tool's summary says to open Cambelt and paste the path. Set in `deploy/.env.example`, both compose files and
  `docs/deployment-shared-host.md`. Shown on the admin Deployment panel with the other resolved values.
- No trailing slash. A trailing slash is trimmed at bind time rather than rejected.

## In-app chat

### Keeping the originals

- `ChatPanel` keeps each picked `File` beside the prepared `ChatFile` it already makes, and passes both to
  `useChat.send`.
- `useChat` holds the originals in a ref keyed by the "you" entry id, in memory only and never in storage (the
  `clientStorage.ts` registry test must still pass). They are dropped when a card is filed or dismissed, when the
  conversation is cleared, and when the panel unmounts.

### Cards by default

- Each "you" entry with attachments renders one `FilingCard` per file **as soon as the message is sent**, before
  any model output. That makes the offer independent of the model.
- Defaults before any proposal: an image is `CarPhoto`, a PDF is `Other`, the title is the file name without its
  extension, and the date is the file's `lastModified` day. The card is labelled as a default suggestion.
- A `filing_offer` SSE event whose `attachment` matches a card on the latest message with files replaces that
  card's category, title, date and link, and labels it as the assistant's suggestion. An offer with no
  `attachment` applies to the first card that has none yet.
- Actions:
  - **File** uploads the original with renditions through the upload function from the gallery spec (extracted
    to `api/documents.ts`), then shows "Filed" with a link to Photos or Documents by kind. A duplicate shows the
    existing title. An allowance refusal shows the plan message.
  - **Edit** opens the upload sheet pre-filled with the card and the file.
  - **Not now** removes the card and drops the original.
- The card shows the remaining allowance when it is under ten.

### Stream

- `ChatEndpoints` emits a new `filing_offer` event when an `offer_to_file` call completes, carrying the tool's
  `FilingOffer` JSON, beside the existing `tool` status event. Compact JSON, as every SSE frame must be.
- `ChatFiles`' remarks gain one line: filing is a separate upload from the browser, and nothing here changes.

### System prompt

`ChatSystemPrompt` gains a short rule set, matching the tool description: call `offer_to_file` once for each
attachment; if an attachment led to a drafted write, call it after the owner confirms the write and link the new
record; if the write was declined, offer without a link; default unplaceable photos to `CarPhoto`; and never say
a file has been filed, because filing happens when the owner presses File. Changing the frozen prompt invalidates
the prompt cache once, which is acceptable. `SystemPromptTests` gains assertions for the rule.

## `/file` route

- New route `file`, a sibling of `account` and `admin` inside the `AuthGate` branch. It adds nothing to
  `PUBLIC_PATHS`, so `routes.gating.test.tsx` must stay green with it gated. The caveat about a plate reading
  "FILE" is the same as for `account`, and no UK plate can.
- It resolves `vehicle` against the garage list by comparing registrations with case and spaces ignored, then
  `navigate(hrefFor(kind === 'Photo' ? 'photos' : 'documents', reg, prefill), { replace: true })`. Kind comes
  from `/api/document-types`. An unknown type falls back to Documents with `Other`.
- A vehicle that is not in this garage shows "This link is for a vehicle that isn't in your garage" with a link to
  the garage. It never falls back to the default vehicle.
- The Photos and Documents screens open their upload sheet when the query carries `file=1`, pre-filled from it,
  and remove the query on close.
- The link authorises nothing. It is a pre-fill: the owner still picks the file and presses Upload, and the
  upload endpoint validates the link ids again.

## Docs

- `docs/mcp-connect.md`: filing photos from an MCP client, and `App:PublicOrigin`.
- `docs/product/roadmap.md`: an entry for this spec.
- `docs/engineering-log.md`: one entry when it ships.
- Minor `VERSION` bump.

## Tests

Data and Domain, against real PostgreSQL:

- `offer_to_file` returns the proposal for a valid link, rejects a foreign vehicle's issue id (pinned with
  `TestOwner.As`), rejects two links, and leaves every table's row count unchanged.
- `url` is absolute with `App:PublicOrigin` set and null when it's blank, and `path` round-trips through a URL
  parser with a title containing `&`, `#` and spaces.
- A read-scope `AssistantToken` can call it.
- The catalogue drift test and the stable-order test pass with `FilingTools`.

Chat:

- The stream emits `filing_offer` with the tool's JSON when `offer_to_file` completes, and only then.
- `SystemPromptTests` cover the new rule.

Front-end, asserting requests rather than the DOM for writes:

- Sending a message with two attachments renders two default cards before any stream event.
- A `filing_offer` updates the card at its `attachment` position, and nothing else.
- File posts multipart with the original, its renditions, the type and the link id.
- Not now drops the original, and no attachment bytes reach `localStorage`.
- `/file` resolves "bt53akj", "BT53 AKJ" and "BT53AKJ" to the same vehicle, redirects by kind, and refuses a
  vehicle outside the garage.
- Photos and Documents open a pre-filled sheet from `?file=1` and clear the query on close.
