# API Specification

This is the API specification for the spec detailed in @docs/specs/2026-08-06-in-app-chat-assistant/spec.md

Three endpoints in a new `ChatEndpoints` group, mapped under `/api/chat` and behind the standard Auth0
fallback policy — no new scheme, no new policy. They follow the existing groups' shape: registration → id via
`VehicleLookup`, RFC 9457 problem details on failure (which `lib/formErrors.ts` already maps onto fields).

Additive to the committed OpenAPI contract; no existing endpoint changes.

## Endpoints

### POST /api/chat

**Purpose:** Send a user message (with optional files) and run the conversation until the assistant either
finishes its turn or requests a write.

**Request:**

```jsonc
{
  "vehicle": "BT53AKJ",              // optional; the current route's reg. Pre-bound into tool calls.
  "messages": [                       // the full transcript so far, client-held
    { "role": "user", "content": [ { "type": "text", "text": "..." } ] },
    { "role": "assistant", "content": [ /* echoed back verbatim, thinking blocks included */ ] }
  ],
  "files": [                          // optional, max 5 TOTAL, attached to the new user message
    { "mediaType": "image/jpeg",     "data": "<base64, no newlines>" },
    { "mediaType": "application/pdf", "data": "<base64, no newlines>" }
  ]
}
```

- `messages` is the API's own content-block shape, round-tripped unmodified. Assistant content **must** be
  echoed back exactly as received — thinking blocks edited or dropped are rejected upstream.
- `files` is one list, not an `images` list plus a `documents` list: the cap is on what the owner attached,
  and splitting it would make "max 5" mean two different things depending on the mix. The server maps each
  entry to an `image` or `document` content block by its `mediaType`.
- Accepted `mediaType`: `image/jpeg`, `image/png`, `image/webp`, `application/pdf` — the list in the technical
  spec is authoritative and HEIC is converted in the browser before it gets here.
- Files are never persisted (see the spec's Out of Scope) and never logged.
- **No classification field, in either direction.** The client does not say what a file is and the response
  carries no type verdict; the model's reading arrives as ordinary `text` events before any `pending_write`.
  A structured verdict would be a second thing to keep in step with the sentence the owner actually reads.

**Response:** `200` with `Content-Type: text/event-stream`. Events:

| event | data | meaning |
|---|---|---|
| `text` | `{ "delta": "…" }` | assistant text, incremental |
| `tool` | `{ "name": "get_due_items", "status": "running" \| "done" }` | a **read** tool ran automatically — surfaced so the UI can say what it is doing, not for the user to act on |
| `pending_write` | see below | the loop suspended on a write tool; render the draft card |
| `done` | `{ "messages": [ … ] }` | turn complete; the authoritative transcript to store client-side |
| `error` | `{ "detail": "…" }` | see Errors |

`pending_write` payload:

```jsonc
{
  "toolUseId": "toolu_...",
  "tool": "add_service",
  "title": "Add service record",           // from the tool's [Description]
  "arguments": { "serviceDate": "2026-07-08", "type": "MOT", "mileage": 80705, "garage": "K&P Motors" },
  "schema": { /* the tool's JSON Schema, so the card can label and type every field */ }
}
```

**Errors:** `400` on a malformed transcript, an unsupported media type, more than 5 files, or a PDF over the
page cap (say which, and how many pages it had — "too long" without a number is not actionable); `401` when the
Auth0 token is absent or expired; `413` when the body exceeds the configured cap (a phone photo set is the
realistic cause — the message must say so, not surface Kestrel's default); `502` when the upstream Messages
API fails, with the request id in the detail. A `stop_reason` of `refusal` is surfaced as an assistant message
explaining the request was declined, not as an error — it is an HTTP 200 upstream and pretending otherwise
would lose the explanation.

---

### POST /api/chat/confirm

**Purpose:** Execute a suspended write with the owner's final (possibly edited) arguments, then resume the
conversation.

**Request:**

```jsonc
{
  "vehicle": "BT53AKJ",
  "messages": [ /* transcript including the assistant turn that requested the write */ ],
  "toolUseId": "toolu_...",
  "tool": "add_service",
  "arguments": { /* what the owner actually confirmed — may differ from the proposal */ }
}
```

**Behaviour:**

- `tool` must be a **write** tool and must match the `tool_use` block carrying `toolUseId` in the last
  assistant message. A mismatch is `400` — this is the check that stops a crafted request from executing a
  tool the model never proposed.
- `arguments` are validated against the tool's schema and then the tool is invoked in-process, stamping
  `EntrySource.Chat`. Domain validation failures return the same RFC 9457 `errors` map the web writes return,
  so the draft card marks the bad field inline exactly as an add sheet does.
- The real result becomes the `tool_result` block and the loop resumes, so the response is the same SSE stream
  as `POST /api/chat` (and may itself suspend again on a second write — a receipt and an odometer photo in one
  message legitimately produce two).

**Response:** `200`, `text/event-stream`, same event set as above.

**Errors:** `400` on schema-invalid arguments or a `toolUseId`/`tool` mismatch; `404` when the vehicle does
not resolve — which is also how a cross-owner vehicle presents, because the global query filter means it
never resolves rather than being explicitly refused; `409` from tools that model a state conflict (e.g.
`complete_task` on an already-completed task); `502` upstream.

---

### POST /api/chat/decline

**Purpose:** Tell the model the owner refused the write, so the turn completes instead of hanging.

**Request:** `{ "vehicle": "…", "messages": [ … ], "toolUseId": "toolu_...", "reason": "optional free text" }`

**Behaviour:** appends a `tool_result` for `toolUseId` with `is_error: true` and a body stating the owner
declined (plus `reason` when given), then resumes the loop. **Not** simply dropping the block: an unanswered
`tool_use` id is rejected by the API and would break the transcript for every later turn.

**Response:** `200`, `text/event-stream`, same event set.

**Errors:** `400` on an unknown `toolUseId`; `401`; `502`.

---

## Notes

- **No endpoint lists the tools.** The catalogue is server-side and the client learns each tool's shape from
  the `schema` on `pending_write`. A `GET /api/chat/tools` would be a second place for the catalogue to be
  described and to drift from `/mcp`'s.
- **Nothing here writes without `/confirm`.** `POST /api/chat` can run reads and can spend tokens; it cannot
  change a row. That is the whole safety property, and it is enforced by the loop halting on the write-tool
  set — the same set `McpAuditFilter` uses, read from one shared place (technical spec).
- The gateway needs no new route: `/api` already proxies to the WebApi wholesale. **SSE must not be buffered
  by YARP** — verify response buffering is off for this path, or the stream arrives as one lump at the end and
  the streaming UI silently becomes a spinner.
