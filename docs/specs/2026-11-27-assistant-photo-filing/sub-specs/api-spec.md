# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-11-27-assistant-photo-filing/spec.md

No new REST endpoint. Filing uses the documents upload endpoint from the gallery spec, unchanged.

## MCP tool

### offer_to_file

**Purpose:** Propose where a shared photo or document should be filed, and return a link that opens Cambelt
ready to file it. Writes nothing.
**Scope:** Read (`McpRead`) and above.
**Parameters:**

| Name | Type | Required | Notes |
|---|---|---|---|
| `vehicle` | string | no | Registration or id; default vehicle when omitted |
| `type` | `DocumentType` | yes | From the grouped list |
| `title` | string | yes | At most 160 characters |
| `documentDate` | date | no | |
| `serviceRecordId` / `expenseEntryId` / `issueId` | int | no | At most one |
| `attachment` | int | no | Chat only: 1-based position in the latest message with files |

**Response:** `McpResult<FilingOffer>`

```json
{
  "registration": "BT53 AKJ",
  "type": "PartsReceipt",
  "kind": "Paper",
  "group": "WorkAndParts",
  "label": "Parts receipt",
  "title": "Euro Car Parts - coolant and thermostat",
  "documentDate": "2026-11-14",
  "linkedTo": { "kind": "Expense", "id": 412, "label": "14 Nov 2026 · Euro Car Parts · £63.20" },
  "attachment": 1,
  "allowance": { "used": 37, "limit": 2000 },
  "path": "/file?vehicle=BT53%20AKJ&type=PartsReceipt&title=...&date=2026-11-14&expenseEntryId=412",
  "url": "https://cambelt.app/file?vehicle=BT53%20AKJ&type=PartsReceipt&..."
}
```

**Errors (tool errors, plain text):** vehicle not found; more than one link; linked record not found for this
vehicle; title longer than 160 characters.

## Chat stream

### event: filing_offer

Emitted when an `offer_to_file` call completes inside a chat turn. `data` is the `FilingOffer` JSON above,
compact. Clients that don't know the event ignore it, as with any SSE event.

## Web route

### GET /file (web app, authenticated)

**Purpose:** Turn a filing link into the right screen with its upload sheet pre-filled.
**Query:** `vehicle`, `type`, `title`, `date`, and at most one of `serviceRecordId`, `expenseEntryId`, `issueId`.
**Behaviour:** Redirects to `/:reg/photos?file=1&...` or `/:reg/documents?file=1&...` by the type's kind. A
vehicle outside the signed-in garage shows a message and never falls back to another vehicle.

## Configuration

- `App:PublicOrigin` (string). Blank means `url` is null and only `path` is returned.
